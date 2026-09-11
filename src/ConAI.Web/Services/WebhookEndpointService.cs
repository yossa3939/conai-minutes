using System.Globalization;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Services;

public sealed class WebhookEndpointService : IWebhookEndpointService
{
    private const string DuplicateMessage = "同じ宛先がすでに登録されています。";

    private readonly ApplicationDbContext _db;
    private readonly IWebhookUrlProtector _protector;
    private readonly NotificationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookEndpointService> _logger;

    public WebhookEndpointService(
        ApplicationDbContext db,
        IWebhookUrlProtector protector,
        IOptions<NotificationOptions> options,
        TimeProvider timeProvider,
        ILogger<WebhookEndpointService> logger)
    {
        _db = db;
        _protector = protector;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        await _db.WebhookEndpoints
            .Where(e => e.OwnerId == ownerId)
            .OrderBy(e => e.Name)
            .ThenBy(e => e.Id)
            .ToListAsync(cancellationToken);

    public Task<WebhookEndpoint?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
        _db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, cancellationToken);

    public async Task<(SaveEndpointResult Result, string? Error)> CreateAsync(
        string ownerId, WebhookEndpointEdit edit, CancellationToken cancellationToken)
    {
        var count = await _db.WebhookEndpoints.CountAsync(e => e.OwnerId == ownerId, cancellationToken);
        if (count >= _options.MaxEndpointsPerUser)
        {
            var limit = _options.MaxEndpointsPerUser.ToString("N0", CultureInfo.InvariantCulture);
            return (SaveEndpointResult.LimitReached,
                $"宛先は 1 人 {limit} 件までです。使わない宛先を削除してから登録してください。");
        }

        var url = edit.Url?.Trim() ?? string.Empty;
        var error = WebhookUrlValidator.Validate(edit.Kind, url);
        if (error is not null)
        {
            return (SaveEndpointResult.InvalidUrl, error);
        }

        var fingerprint = _protector.Fingerprint(url);
        var duplicated = await _db.WebhookEndpoints
            .AnyAsync(e => e.OwnerId == ownerId && e.UrlFingerprint == fingerprint, cancellationToken);
        if (duplicated)
        {
            return (SaveEndpointResult.Duplicate, DuplicateMessage);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        _db.WebhookEndpoints.Add(new WebhookEndpoint
        {
            OwnerId = ownerId,
            Name = edit.Name.Trim(),
            Kind = edit.Kind,
            ProtectedUrl = _protector.Protect(url),
            UrlHint = WebhookUrlValidator.BuildHint(url),
            UrlFingerprint = fingerprint,
            NotifyOnSuccess = edit.NotifyOnSuccess,
            NotifyOnFailure = edit.NotifyOnFailure,
            IsEnabled = edit.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // 上の確認から保存までの間に、別の要求が同じ URL を入れたときに来る。
            // DB の一意制約が最後の砦なので、そのときは同じ言い分に揃える
            var raced = await _db.WebhookEndpoints
                .AnyAsync(e => e.OwnerId == ownerId && e.UrlFingerprint == fingerprint, cancellationToken);
            if (raced)
            {
                return (SaveEndpointResult.Duplicate, DuplicateMessage);
            }

            // 一意制約でないなら DB 側の障害。重複と言い換えると、
            // 利用者は誤った案内を受け取り、原因はどこにも残らない
            _logger.LogError(exception, "Webhook endpoint could not be saved for owner {OwnerId}", ownerId);
            throw;
        }

        return (SaveEndpointResult.Saved, null);
    }

    public async Task<(SaveEndpointResult Result, string? Error)> UpdateAsync(
        Guid id, string ownerId, WebhookEndpointEdit edit, CancellationToken cancellationToken)
    {
        var endpoint = await _db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, cancellationToken);
        if (endpoint is null)
        {
            return (SaveEndpointResult.NotFound, null);
        }

        var url = edit.Url?.Trim();
        // 空欄は「URL を変えない」の意味。保存済みの値は画面に出せないので、
        // 名前を直すたびにチャット側の管理画面へ取りに行かせない
        if (!string.IsNullOrEmpty(url))
        {
            var error = WebhookUrlValidator.Validate(endpoint.Kind, url);
            if (error is not null)
            {
                return (SaveEndpointResult.InvalidUrl, error);
            }

            var fingerprint = _protector.Fingerprint(url);
            var duplicated = await _db.WebhookEndpoints
                .AnyAsync(e => e.OwnerId == ownerId && e.UrlFingerprint == fingerprint && e.Id != id, cancellationToken);
            if (duplicated)
            {
                return (SaveEndpointResult.Duplicate, DuplicateMessage);
            }

            endpoint.ProtectedUrl = _protector.Protect(url);
            endpoint.UrlHint = WebhookUrlValidator.BuildHint(url);
            endpoint.UrlFingerprint = fingerprint;

            // 送信結果は前の URL に付いたもの。作り直した宛先の状態として見せない
            endpoint.LastStatus = WebhookDeliveryStatus.None;
            endpoint.LastError = null;
            endpoint.LastAttemptedAt = null;
        }

        endpoint.Name = edit.Name.Trim();
        endpoint.NotifyOnSuccess = edit.NotifyOnSuccess;
        endpoint.NotifyOnFailure = edit.NotifyOnFailure;
        endpoint.IsEnabled = edit.IsEnabled;
        endpoint.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

        await _db.SaveChangesAsync(cancellationToken);
        return (SaveEndpointResult.Saved, null);
    }

    public async Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        var endpoint = await _db.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == id && e.OwnerId == ownerId, cancellationToken);
        if (endpoint is null)
        {
            return false;
        }

        _db.WebhookEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<WebhookEndpoint>> ListSubscribersAsync(
        string ownerId, NotificationEvent notificationEvent, CancellationToken cancellationToken)
    {
        var query = _db.WebhookEndpoints.Where(e => e.OwnerId == ownerId && e.IsEnabled);

        query = notificationEvent switch
        {
            NotificationEvent.Succeeded => query.Where(e => e.NotifyOnSuccess),
            NotificationEvent.Failed => query.Where(e => e.NotifyOnFailure),
            // 手動送信は「今これを送る」という指示なので、成功・失敗の購読設定では絞らない
            _ => query
        };

        return await query.OrderBy(e => e.Name).ThenBy(e => e.Id).ToListAsync(cancellationToken);
    }

    public Task<bool> HasEnabledAsync(string ownerId, CancellationToken cancellationToken) =>
        _db.WebhookEndpoints.AnyAsync(e => e.OwnerId == ownerId && e.IsEnabled, cancellationToken);

    public async Task MarkSucceededAsync(Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return;
        }

        endpoint.LastStatus = WebhookDeliveryStatus.Succeeded;
        endpoint.LastAttemptedAt = _timeProvider.GetUtcNow().UtcDateTime;
        endpoint.LastError = null;
        // 自動で無効にした宛先も、テスト送信が通ったら使える状態に戻す
        endpoint.IsEnabled = true;

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid id, string error, bool disable, CancellationToken cancellationToken)
    {
        var endpoint = await _db.WebhookEndpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (endpoint is null)
        {
            return;
        }

        endpoint.LastStatus = WebhookDeliveryStatus.Failed;
        endpoint.LastAttemptedAt = _timeProvider.GetUtcNow().UtcDateTime;
        endpoint.LastError = error.Length <= WebhookLimits.MaxErrorChars
            ? error
            : error[..WebhookLimits.MaxErrorChars];

        if (disable)
        {
            endpoint.IsEnabled = false;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }
}
