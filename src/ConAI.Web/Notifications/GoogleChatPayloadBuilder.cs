using System.Text.Json;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public sealed class GoogleChatPayloadBuilder : IWebhookPayloadBuilder
{
    /// <summary>1 メッセージの上限は 4,096 文字。余白を見て 3,500 に収める。</summary>
    private const int MaxTextChars = 3500;

    public WebhookKind Kind => WebhookKind.GoogleChat;

    public string BuildJson(MeetingNotification notification)
    {
        var heading = NotificationHeadings.Text(notification.Event);
        var subject = NotificationText.BuildSubject(notification);
        var detail = MinutesExcerpt.ToSingleAsteriskBold(NotificationText.BuildDetail(notification));
        var text = NotificationText.Compose(
            $"*{heading}*\n{subject}\n{detail}".TrimEnd(), notification.Url, MaxTextChars);

        return JsonSerializer.Serialize(new { text }, WebhookJson.Options);
    }
}
