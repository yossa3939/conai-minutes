using ConAI.Web.Data;
using ConAI.Web.Notifications;

namespace ConAI.Web.Tests;

public class WebhookUrlValidatorTests
{
    [Theory]
    [InlineData(WebhookKind.Slack, "https://hooks.slack.com/services/T00000000/B00000000/pQ7x")]
    [InlineData(WebhookKind.Discord, "https://discord.com/api/webhooks/123456789/abcdef")]
    [InlineData(WebhookKind.Discord, "https://discordapp.com/api/webhooks/123456789/abcdef")]
    [InlineData(WebhookKind.Discord, "https://ptb.discord.com/api/webhooks/123456789/abcdef")]
    [InlineData(WebhookKind.Discord, "https://canary.discord.com/api/webhooks/123456789/abcdef")]
    [InlineData(WebhookKind.MicrosoftTeams, "https://prod-12.japaneast.logic.azure.com/workflows/abc/triggers/manual/paths/invoke?sig=x")]
    [InlineData(WebhookKind.GoogleChat, "https://chat.googleapis.com/v1/spaces/AAAA/messages?key=k&token=t")]
    public void 許可された宛先は通る(WebhookKind kind, string url)
    {
        Assert.Null(WebhookUrlValidator.Validate(kind, url));
    }

    [Theory]
    [InlineData(WebhookKind.Slack, "https://hooks.example.com/services/T/B/x")]
    [InlineData(WebhookKind.Slack, "https://hooks.slack.com/webhook/T/B/x")]
    [InlineData(WebhookKind.Discord, "https://discord.com/api/channels/1/messages")]
    [InlineData(WebhookKind.MicrosoftTeams, "https://prod-12.logic.azure.com.example.net/workflows/a/triggers/b")]
    [InlineData(WebhookKind.MicrosoftTeams, "https://prod-12.logic.azure.com/workflows/a")]
    [InlineData(WebhookKind.GoogleChat, "https://chat.googleapis.com/v2/spaces/AAAA/messages")]
    public void 許可されない宛先は種別の例を添えて弾く(WebhookKind kind, string url)
    {
        var error = WebhookUrlValidator.Validate(kind, url);

        Assert.NotNull(error);
        Assert.Contains(WebhookKinds.UrlExample(kind), error);
    }

    [Fact]
    public void 空の宛先は入力を促す()
    {
        Assert.Equal("宛先 URL を入力してください。", WebhookUrlValidator.Validate(WebhookKind.Slack, "  "));
    }

    [Fact]
    public void httpは弾く()
    {
        Assert.NotNull(WebhookUrlValidator.Validate(
            WebhookKind.Slack, "http://hooks.slack.com/services/T/B/pQ7x"));
    }

    [Fact]
    public void ポート443以外は弾く()
    {
        Assert.NotNull(WebhookUrlValidator.Validate(
            WebhookKind.Slack, "https://hooks.slack.com:8443/services/T/B/pQ7x"));
    }

    [Fact]
    public void 長すぎる宛先は文字数で弾く()
    {
        var url = "https://hooks.slack.com/services/T/B/" + new string('x', 2100);

        Assert.Equal("宛先 URL は 2,048 文字以内で入力してください。", WebhookUrlValidator.Validate(WebhookKind.Slack, url));
    }

    [Fact]
    public void ホストの大文字小文字は区別しない()
    {
        Assert.Null(WebhookUrlValidator.Validate(
            WebhookKind.Slack, "https://Hooks.Slack.COM/services/T/B/pQ7x"));
    }

    [Fact]
    public void マスク断片はホストと末尾4文字だけを含む()
    {
        var hint = WebhookUrlValidator.BuildHint("https://hooks.slack.com/services/T00000000/B00000000/pQ7x");

        Assert.Equal("hooks.slack.com/…/pQ7x", hint);
        Assert.DoesNotContain("T00000000", hint);
    }
}
