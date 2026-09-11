using System.Text.Json;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public sealed class DiscordPayloadBuilder : IWebhookPayloadBuilder
{
    /// <summary>embed の description は 4,096 文字まで。余白を見て 3,800 に収める。</summary>
    private const int MaxDescriptionChars = 3800;

    private const int GreenColor = 3066993;
    private const int RedColor = 15158332;
    private const int BlueColor = 3447003;

    public WebhookKind Kind => WebhookKind.Discord;

    public string BuildJson(MeetingNotification notification)
    {
        var subject = NotificationText.BuildSubject(notification);
        var detail = NotificationText.BuildDetail(notification);
        var description = NotificationText.Compose(
            $"**{subject}**\n{detail}".TrimEnd(), tail: null, MaxDescriptionChars);

        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = NotificationHeadings.Text(notification.Event),
                    description,
                    // url を入れると title がリンクになる。無いときは送らない
                    url = notification.Url,
                    color = Color(notification.Event)
                }
            },
            // 会議名や議事録に紛れた @everyone で、宛先のサーバー全員を呼び出さないため
            allowed_mentions = new { parse = Array.Empty<string>() }
        };

        return JsonSerializer.Serialize(payload, WebhookJson.Options);
    }

    private static int Color(NotificationEvent notificationEvent) => notificationEvent switch
    {
        NotificationEvent.Succeeded => GreenColor,
        NotificationEvent.Failed => RedColor,
        _ => BlueColor
    };
}
