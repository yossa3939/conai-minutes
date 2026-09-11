using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

/// <summary>宛先へ 1 回だけ送る。再送の判断は呼び出し側（dispatcher）が持つ。</summary>
public interface IWebhookSender
{
    Task<WebhookSendResult> SendAsync(WebhookKind kind, string url, string json, CancellationToken cancellationToken);
}
