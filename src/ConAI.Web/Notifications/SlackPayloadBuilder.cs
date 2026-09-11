using System.Text.Json;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public sealed class SlackPayloadBuilder : IWebhookPayloadBuilder
{
    /// <summary>section ブロックの上限は 3,000 文字。余白を見て 2,800 に収める。</summary>
    private const int MaxTextChars = 2800;

    public WebhookKind Kind => WebhookKind.Slack;

    public string BuildJson(MeetingNotification notification)
    {
        var heading = NotificationHeadings.Text(notification.Event);
        var subject = Escape(NotificationText.BuildSubject(notification));
        var detail = MinutesExcerpt.ToSingleAsteriskBold(Escape(NotificationText.BuildDetail(notification)));
        var link = notification.Url is null ? null : $"<{notification.Url}|議事録を開く>";
        var body = NotificationText.Compose($"*{subject}*\n{detail}".TrimEnd(), link, MaxTextChars);

        var payload = new
        {
            // 通知一覧とスマートフォンの通知に出る要約。blocks だけだと「メッセージが届きました」になる
            text = heading,
            blocks = new object[]
            {
                new { type = "header", text = new { type = "plain_text", text = heading, emoji = true } },
                new { type = "section", text = new { type = "mrkdwn", text = body } }
            }
        };

        return JsonSerializer.Serialize(payload, WebhookJson.Options);
    }

    /// <summary>
    /// mrkdwn は生の &lt; &gt; を <c>&lt;URL|表示名&gt;</c> の記法として読む。
    /// 利用者が書いた文字列をそのまま渡すと、通知の中に別の宛先へのリンクを作れてしまう。
    /// Slack が定める 3 文字を実体参照に直す（受け取った側では元の文字に戻って見える）。
    /// </summary>
    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal);
}
