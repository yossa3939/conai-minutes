using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

/// <summary>宛先の種別ごとに、そのサービスが受け取る JSON を組み立てる。</summary>
public interface IWebhookPayloadBuilder
{
    WebhookKind Kind { get; }

    string BuildJson(MeetingNotification notification);
}
