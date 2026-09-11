using System.Text.Json;
using ConAI.Web.Data;
using ConAI.Web.Notifications;

namespace ConAI.Web.Tests;

public class WebhookPayloadBuilderTests
{
    private static MeetingNotification Sample(
        NotificationEvent notificationEvent = NotificationEvent.Succeeded,
        string excerpt = "**決定**：次の版で出す。",
        string? url = "https://conai.example.com/Meetings/Details/abc") =>
        new(notificationEvent, "定例", new DateTime(2026, 9, 5, 10, 0, 0), excerpt, url,
            notificationEvent == NotificationEvent.Failed ? "生成に失敗しました。" : null);

    [Fact]
    public void Slackは見出しブロックと本文ブロックを送る()
    {
        var json = new SlackPayloadBuilder().BuildJson(Sample());

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("議事録ができました", root.GetProperty("text").GetString());

        var blocks = root.GetProperty("blocks");
        Assert.Equal("header", blocks[0].GetProperty("type").GetString());
        Assert.Equal("議事録ができました", blocks[0].GetProperty("text").GetProperty("text").GetString());

        var body = blocks[1].GetProperty("text").GetProperty("text").GetString();
        Assert.Contains("*定例（2026/09/05 10:00:00）*", body);
        // Slack の太字は星 1 つ。Markdown のまま送ると星が見えてしまう
        Assert.Contains("*決定*：次の版で出す。", body);
        Assert.Contains("<https://conai.example.com/Meetings/Details/abc|議事録を開く>", body);
    }

    [Fact]
    public void 日本語はエスケープせずそのまま送る()
    {
        var json = new SlackPayloadBuilder().BuildJson(Sample());

        Assert.Contains("議事録ができました", json);
        Assert.DoesNotContain("\\u8B70", json);
    }

    [Fact]
    public void Slackは議事録の山括弧をリンクとして解釈させない()
    {
        // 会議名も議事録も利用者が書いた文字列。mrkdwn の <URL|表示名> をそのまま通すと、
        // 通知の中に別の宛先へのリンクを作れてしまう
        var notification = new MeetingNotification(
            NotificationEvent.Succeeded,
            "<https://evil.example|請求書>",
            null,
            "本文にも <https://evil.example|ここ> と & を書ける。",
            "https://conai.example.com/Meetings/Details/abc",
            null);

        var json = new SlackPayloadBuilder().BuildJson(notification);

        using var document = JsonDocument.Parse(json);
        var body = document.RootElement.GetProperty("blocks")[1]
            .GetProperty("text").GetProperty("text").GetString();

        Assert.NotNull(body);
        Assert.DoesNotContain("<https://evil.example|", body);
        Assert.Contains("&lt;https://evil.example|請求書&gt;", body);
        Assert.Contains(" &amp; ", body);
        // こちらが組み立てたリンクは壊さない
        Assert.Contains("<https://conai.example.com/Meetings/Details/abc|議事録を開く>", body);
    }

    [Fact]
    public void リンクが無ければリンク行を省く()
    {
        var json = new SlackPayloadBuilder().BuildJson(Sample(url: null));

        Assert.DoesNotContain("議事録を開く", json);
    }

    [Fact]
    public void GoogleChatは見出しと本文を1つの文字列で送る()
    {
        var json = new GoogleChatPayloadBuilder().BuildJson(Sample());

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetProperty("text").GetString();

        Assert.StartsWith("*議事録ができました*", text);
        Assert.Contains("定例（2026/09/05 10:00:00）", text);
        Assert.EndsWith("https://conai.example.com/Meetings/Details/abc", text);
    }

    [Fact]
    public void 長い抜粋でもリンクは残る()
    {
        var json = new GoogleChatPayloadBuilder().BuildJson(Sample(excerpt: new string('あ', 5000)));

        using var document = JsonDocument.Parse(json);
        var text = document.RootElement.GetProperty("text").GetString();

        Assert.True(text!.Length <= 3500);
        Assert.EndsWith("https://conai.example.com/Meetings/Details/abc", text);
    }

    [Fact]
    public void 失敗の通知は抜粋の代わりに理由を載せる()
    {
        var json = new SlackPayloadBuilder().BuildJson(Sample(NotificationEvent.Failed, excerpt: string.Empty));

        Assert.Contains("議事録の生成に失敗しました", json);
        Assert.Contains("生成に失敗しました。", json);
    }

    [Fact]
    public void 種別は自分の宛先だけを名乗る()
    {
        Assert.Equal(WebhookKind.Slack, new SlackPayloadBuilder().Kind);
        Assert.Equal(WebhookKind.GoogleChat, new GoogleChatPayloadBuilder().Kind);
    }

    [Fact]
    public void Discordは埋め込みで送る()
    {
        var json = new DiscordPayloadBuilder().BuildJson(Sample());

        using var document = JsonDocument.Parse(json);
        var embed = document.RootElement.GetProperty("embeds")[0];

        Assert.Equal("議事録ができました", embed.GetProperty("title").GetString());
        Assert.Equal("https://conai.example.com/Meetings/Details/abc", embed.GetProperty("url").GetString());
        // Discord は Markdown の ** をそのまま解釈する。星 1 つに直す必要はない
        Assert.Contains("**決定**：次の版で出す。", embed.GetProperty("description").GetString());
    }

    [Fact]
    public void Discordは開催日時を本文に入れる()
    {
        var json = new DiscordPayloadBuilder().BuildJson(Sample());

        using var document = JsonDocument.Parse(json);
        var embed = document.RootElement.GetProperty("embeds")[0];

        Assert.Contains("2026/09/05 10:00:00", embed.GetProperty("description").GetString());
        // timestamp は UTC の値として解釈され、閲覧者の時間帯でずらして表示される
        Assert.False(embed.TryGetProperty("timestamp", out _));
    }

    [Fact]
    public void Discordは結果で色を変える()
    {
        static int Color(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32();
        }

        var builder = new DiscordPayloadBuilder();
        var succeeded = Color(builder.BuildJson(Sample()));
        var failed = Color(builder.BuildJson(Sample(NotificationEvent.Failed, excerpt: string.Empty)));

        Assert.NotEqual(succeeded, failed);
    }

    [Fact]
    public void リンクが無ければDiscordの埋め込みからurlを外す()
    {
        var json = new DiscordPayloadBuilder().BuildJson(Sample(url: null));

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("embeds")[0].TryGetProperty("url", out _));
    }

    [Fact]
    public void Discordはメンションを一切効かせない()
    {
        // 会議名や議事録に @everyone が紛れ込んでも、宛先のサーバー全員を呼び出さない
        var json = new DiscordPayloadBuilder().BuildJson(Sample(excerpt: "@everyone 集合してください。"));

        using var document = JsonDocument.Parse(json);
        var parse = document.RootElement.GetProperty("allowed_mentions").GetProperty("parse");

        Assert.Equal(JsonValueKind.Array, parse.ValueKind);
        Assert.Equal(0, parse.GetArrayLength());
    }

    [Fact]
    public void Teamsはアダプティブカードで送る()
    {
        var json = new TeamsPayloadBuilder().BuildJson(Sample());

        using var document = JsonDocument.Parse(json);
        var content = document.RootElement.GetProperty("attachments")[0].GetProperty("content");

        Assert.Equal("AdaptiveCard", content.GetProperty("type").GetString());
        Assert.Equal("1.5", content.GetProperty("version").GetString());
        Assert.Equal("http://adaptivecards.io/schemas/adaptive-card.json", content.GetProperty("$schema").GetString());

        var body = content.GetProperty("body");
        Assert.Equal("議事録ができました", body[0].GetProperty("text").GetString());
        Assert.Equal("定例（2026/09/05 10:00:00）", body[1].GetProperty("text").GetString());
        Assert.Contains("**決定**：次の版で出す。", body[2].GetProperty("text").GetString());

        var action = content.GetProperty("actions")[0];
        Assert.Equal("Action.OpenUrl", action.GetProperty("type").GetString());
        Assert.Equal("https://conai.example.com/Meetings/Details/abc", action.GetProperty("url").GetString());
    }

    [Fact]
    public void リンクが無ければTeamsのボタンを出さない()
    {
        var json = new TeamsPayloadBuilder().BuildJson(Sample(url: null));

        using var document = JsonDocument.Parse(json);
        var content = document.RootElement.GetProperty("attachments")[0].GetProperty("content");

        Assert.False(content.TryGetProperty("actions", out _));
    }

    [Fact]
    public void 長い抜粋はサービスごとの上限に収める()
    {
        var notification = Sample(excerpt: new string('あ', 5000));

        static int TextLength(string json, Func<JsonElement, string?> select)
        {
            using var document = JsonDocument.Parse(json);
            return select(document.RootElement)!.Length;
        }

        Assert.True(TextLength(new DiscordPayloadBuilder().BuildJson(notification),
            root => root.GetProperty("embeds")[0].GetProperty("description").GetString()) <= 3800);

        Assert.True(TextLength(new TeamsPayloadBuilder().BuildJson(notification),
            root => root.GetProperty("attachments")[0].GetProperty("content")
                .GetProperty("body")[2].GetProperty("text").GetString()) <= 3000);
    }
}
