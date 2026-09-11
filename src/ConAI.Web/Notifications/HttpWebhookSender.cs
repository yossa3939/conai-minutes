using System.Globalization;
using System.Net;
using System.Text;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public sealed class HttpWebhookSender : IWebhookSender
{
    public const string ClientName = "webhook";

    /// <summary>読むのは待ち時間だけ。宛先が大きな本文を返しても、ここで打ち切る。</summary>
    private const int MaxBodyChars = 4096;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _timeProvider;

    public HttpWebhookSender(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    {
        _httpClientFactory = httpClientFactory;
        _timeProvider = timeProvider;
    }

    public async Task<WebhookSendResult> SendAsync(
        WebhookKind kind, string url, string json, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };

        try
        {
            // 本文は待ち時間を読むためだけに使う。ヘッダーが来た時点で返し、
            // 本文は必要な分だけ読む（宛先がいくらでも送ってこられるため）
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await ReadBodyAsync(response, cancellationToken);

            return Classify(kind, response, body);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 停止の指示は握りつぶさない
            throw;
        }
        catch (TaskCanceledException)
        {
            // HttpClient.Timeout に達した場合。相手が重いだけのことがあるので後で試す
            return new WebhookSendResult(WebhookSendOutcome.Retryable, null, null, "宛先が時間内に応答しませんでした。");
        }
        catch (HttpRequestException)
        {
            return new WebhookSendResult(WebhookSendOutcome.Retryable, null, null, "宛先に接続できませんでした。");
        }
    }

    private static async Task<string?> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var buffer = new char[MaxBodyChars];
            var read = await reader.ReadBlockAsync(buffer, cancellationToken);

            return new string(buffer, 0, read);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            // 本文の途中で切れても、状態コードだけで判定は付く
            return null;
        }
    }

    private WebhookSendResult Classify(WebhookKind kind, HttpResponseMessage response, string? body)
    {
        var status = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            return new WebhookSendResult(WebhookSendOutcome.Succeeded, status, null, null);
        }

        var name = WebhookKinds.DisplayName(kind);
        var code = status.ToString(CultureInfo.InvariantCulture);

        return status switch
        {
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden =>
                new WebhookSendResult(WebhookSendOutcome.Revoked, status, null,
                    $"{name} に拒否されました（HTTP {code}）。URL を登録し直してください。"),

            (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone =>
                new WebhookSendResult(WebhookSendOutcome.Revoked, status, null,
                    $"宛先が見つかりません（HTTP {code}）。チャット側で受信 Webhook が削除された可能性があります。URL を作り直してください。"),

            (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests or >= 500 =>
                new WebhookSendResult(WebhookSendOutcome.Retryable, status,
                    RetryAfterReader.Read(response, body, _timeProvider),
                    $"{name} が一時的に受け取れませんでした（HTTP {code}）。"),

            // 転送は追いかけない。追いかけると許可リストの外へ送ることになる
            >= 300 and < 400 =>
                new WebhookSendResult(WebhookSendOutcome.Permanent, status, null,
                    $"宛先が転送を返しました（HTTP {code}）。URL を登録し直してください。"),

            _ => new WebhookSendResult(WebhookSendOutcome.Permanent, status, null,
                $"{name} が受け取りませんでした（HTTP {code}）。")
        };
    }
}
