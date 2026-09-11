using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Notifications;

public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken);

    /// <summary>画面のテスト送信。<see cref="WebhookEndpoint.IsEnabled"/> を見ず、成功したら有効に戻す。</summary>
    Task<WebhookSendResult> SendTestAsync(Guid endpointId, string ownerId, CancellationToken cancellationToken);
}

public sealed class NotificationDispatcher : INotificationDispatcher
{
    private const string DecryptFailureMessage = "保存した URL を読み出せませんでした。URL を登録し直してください。";
    private const string UnknownFailureMessage = "送信できませんでした。";

    private readonly ApplicationDbContext _db;
    private readonly IWebhookEndpointService _endpoints;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IWebhookUrlProtector _protector;
    private readonly IWebhookSender _sender;
    private readonly IWebhookPacer _pacer;
    private readonly INotificationDelay _delay;
    private readonly NotificationOptions _options;
    private readonly IReadOnlyDictionary<WebhookKind, IWebhookPayloadBuilder> _builders;
    private readonly ILogger<NotificationDispatcher> _logger;

    public NotificationDispatcher(
        ApplicationDbContext db,
        IWebhookEndpointService endpoints,
        IServiceScopeFactory scopeFactory,
        IWebhookUrlProtector protector,
        IWebhookSender sender,
        IWebhookPacer pacer,
        INotificationDelay delay,
        IOptions<NotificationOptions> options,
        IEnumerable<IWebhookPayloadBuilder> builders,
        ILogger<NotificationDispatcher> logger)
    {
        _db = db;
        _endpoints = endpoints;
        _scopeFactory = scopeFactory;
        _protector = protector;
        _sender = sender;
        _pacer = pacer;
        _delay = delay;
        _options = options.Value;
        _builders = builders.ToDictionary(builder => builder.Kind);
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        var meeting = await _db.Meetings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == request.MeetingId, cancellationToken);

        if (meeting is null)
        {
            _logger.LogWarning("Notification skipped: meeting {MeetingId} was not found", request.MeetingId);
            return;
        }

        var subscribers = await _endpoints.ListSubscribersAsync(meeting.OwnerId, request.Event, cancellationToken);
        if (subscribers.Count == 0)
        {
            return;
        }

        var notification = MeetingNotificationFactory.Create(meeting, request.Event, _options);

        // 1 件ずつ送ると、再送を繰り返す宛先の後ろで残りが待たされる。
        // 宛先ごとに送り、1 件が落ちても残りは最後まで走らせる
        await Task.WhenAll(subscribers
            .Select(endpoint => SendWithRetryAsync(endpoint, notification, cancellationToken))
            .ToArray());
    }

    public async Task<WebhookSendResult> SendTestAsync(
        Guid endpointId, string ownerId, CancellationToken cancellationToken)
    {
        var endpoint = await _endpoints.GetAsync(endpointId, ownerId, cancellationToken);
        if (endpoint is null)
        {
            // 画面側でも 404 を返すが、削除と同時に押されたときのために両方で見る
            return new WebhookSendResult(WebhookSendOutcome.Permanent, null, null, "宛先が見つかりません。");
        }

        var url = await ResolveUrlAsync(endpoint, _endpoints);
        if (url is null)
        {
            return new WebhookSendResult(WebhookSendOutcome.Revoked, null, null, DecryptFailureMessage);
        }

        var json = _builders[endpoint.Kind].BuildJson(MeetingNotificationFactory.CreateTest(_options));

        await _delay.WaitAsync(_pacer.Reserve(endpoint.Id), cancellationToken);
        var result = await _sender.SendAsync(endpoint.Kind, url, json, cancellationToken);

        // テスト送信は人が結果を待っている。再送で待たせず、そのまま返す
        await RecordAsync(_endpoints, endpoint, result, retries: 0);
        return result;
    }

    private async Task SendWithRetryAsync(
        WebhookEndpoint endpoint, MeetingNotification notification, CancellationToken cancellationToken)
    {
        // 送信結果の記録に使う DbContext は同時に触れない。宛先ごとに別のスコープを開く
        await using var scope = _scopeFactory.CreateAsyncScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        var url = await ResolveUrlAsync(endpoint, endpoints);
        if (url is null)
        {
            return;
        }

        var json = _builders[endpoint.Kind].BuildJson(notification);
        var attempt = 0;
        WebhookSendResult result;

        while (true)
        {
            await _delay.WaitAsync(_pacer.Reserve(endpoint.Id), cancellationToken);
            result = await _sender.SendAsync(endpoint.Kind, url, json, cancellationToken);

            if (result.Outcome != WebhookSendOutcome.Retryable || attempt >= _options.MaxRetries)
            {
                break;
            }

            // 再送そのものを残す。成功で終わっても、相手が不安定だったことを後から追える
            _logger.LogWarning(
                "Webhook retry {Attempt} for {EndpointId} ({Kind}) status {Status}",
                attempt + 1, endpoint.Id, endpoint.Kind, result.StatusCode);

            // 1 秒 → 4 秒 → 16 秒。相手が待ち時間を返したときはそれに従う。
            // 再送の上限を増やしても待ち時間が伸び続けないよう、相手の指定と同じ値で頭打ちにする
            var backoff = TimeSpan.FromSeconds(Math.Pow(4, attempt));
            await _delay.WaitAsync(
                result.RetryAfter ?? (backoff > RetryAfterReader.Max ? RetryAfterReader.Max : backoff),
                cancellationToken);
            attempt++;
        }

        await RecordAsync(endpoints, endpoint, result, attempt);
    }

    private async Task<string?> ResolveUrlAsync(WebhookEndpoint endpoint, IWebhookEndpointService endpoints)
    {
        if (_protector.TryUnprotect(endpoint.ProtectedUrl, out var url))
        {
            return url;
        }

        // 鍵を失っても、読めなくなった 1 件だけを止める
        _logger.LogWarning("Webhook endpoint {EndpointId} could not be decrypted", endpoint.Id);
        await endpoints.MarkFailedAsync(endpoint.Id, DecryptFailureMessage, disable: true, CancellationToken.None);
        return null;
    }

    private async Task RecordAsync(
        IWebhookEndpointService endpoints, WebhookEndpoint endpoint, WebhookSendResult result, int retries)
    {
        // 停止の指示が出ていても結果は残す。残さないと、次に開いた画面が古い状態を見せる
        var token = CancellationToken.None;

        // URL とホストは記録に残さない
        if (result.Outcome == WebhookSendOutcome.Succeeded)
        {
            // 成功は Debug。会議 1 件で最大 10 宛先ぶん出るため、既定の水準では静かにしておく
            _logger.LogDebug(
                "Webhook delivered to {EndpointId} ({Kind}) status {Status} after {Retries} retries",
                endpoint.Id, endpoint.Kind, result.StatusCode, retries);

            await endpoints.MarkSucceededAsync(endpoint.Id, token);
            return;
        }

        _logger.LogWarning(
            "Webhook failed for {EndpointId} ({Kind}) outcome {Outcome} status {Status} after {Retries} retries",
            endpoint.Id, endpoint.Kind, result.Outcome, result.StatusCode, retries);

        await endpoints.MarkFailedAsync(
            endpoint.Id,
            result.Message ?? UnknownFailureMessage,
            disable: result.Outcome == WebhookSendOutcome.Revoked,
            token);
    }
}
