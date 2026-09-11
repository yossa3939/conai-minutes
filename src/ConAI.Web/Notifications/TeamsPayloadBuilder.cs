using System.Text.Json;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public sealed class TeamsPayloadBuilder : IWebhookPayloadBuilder
{
    /// <summary>TextBlock の実用上の上限に合わせて 3,000 に収める。</summary>
    private const int MaxTextChars = 3000;

    public WebhookKind Kind => WebhookKind.MicrosoftTeams;

    public string BuildJson(MeetingNotification notification)
    {
        var detail = NotificationText.Compose(
            NotificationText.BuildDetail(notification), tail: null, MaxTextChars);

        var body = new List<object>
        {
            new { type = "TextBlock", size = "Large", weight = "Bolder", wrap = true,
                  text = NotificationHeadings.Text(notification.Event) },
            new { type = "TextBlock", weight = "Bolder", wrap = true,
                  text = NotificationText.BuildSubject(notification) }
        };

        if (detail.Length > 0)
        {
            body.Add(new { type = "TextBlock", wrap = true, text = detail });
        }

        // $ で始まるキーは匿名型で書けないため辞書で組む
        var content = new Dictionary<string, object?>
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard",
            ["version"] = "1.5",
            ["body"] = body
        };

        if (notification.Url is { } url)
        {
            content["actions"] = new object[]
            {
                new { type = "Action.OpenUrl", title = "議事録を開く", url }
            };
        }

        var payload = new
        {
            type = "message",
            attachments = new object[]
            {
                new { contentType = "application/vnd.microsoft.card.adaptive", content }
            }
        };

        return JsonSerializer.Serialize(payload, WebhookJson.Options);
    }
}
