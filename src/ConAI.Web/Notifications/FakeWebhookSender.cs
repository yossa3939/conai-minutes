using System.Collections.Concurrent;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

/// <summary>種別と本文だけを記録する。URL は資格情報なので残さない。</summary>
public sealed record FakeWebhookSend(WebhookKind Kind, string Json);

/// <summary>テストと開発用。外へ出さずに送信を記録する。</summary>
public sealed class FakeWebhookSender : IWebhookSender
{
    private readonly ConcurrentQueue<FakeWebhookSend> _sent = new();

    public IReadOnlyCollection<FakeWebhookSend> Sent => _sent;

    public Task<WebhookSendResult> SendAsync(
        WebhookKind kind, string url, string json, CancellationToken cancellationToken)
    {
        _sent.Enqueue(new FakeWebhookSend(kind, json));

        return Task.FromResult(new WebhookSendResult(WebhookSendOutcome.Succeeded, 200, null, null));
    }

    public void Clear() => _sent.Clear();
}
