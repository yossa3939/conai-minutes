using System.Globalization;
using ConAI.Web.Data;
using ConAI.Web.Services;

namespace ConAI.Web.Notifications;

/// <summary>
/// 宛先 URL が、その種別のチャットサービスのものかを見る。
/// 許可リストに載ったホストと経路だけを通す。任意の URL を通すと、
/// 認証済みの利用者が社内ネットワークへ向けて要求を投げさせられる。
/// </summary>
public static class WebhookUrlValidator
{
    private const string TeamsHostSuffix = ".logic.azure.com";

    private static readonly string[] DiscordHosts =
        ["discord.com", "discordapp.com", "ptb.discord.com", "canary.discord.com"];

    /// <summary>妥当なら null、そうでなければ利用者向けのエラー文を返す。</summary>
    public static string? Validate(WebhookKind kind, string? url)
    {
        var value = url?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            return "宛先 URL を入力してください。";
        }

        if (value.Length > WebhookLimits.MaxUrlChars)
        {
            var limit = WebhookLimits.MaxUrlChars.ToString("N0", CultureInfo.InvariantCulture);
            return $"宛先 URL は {limit} 文字以内で入力してください。";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Port != 443
            || !MatchesKind(kind, uri))
        {
            return $"{WebhookKinds.DisplayName(kind)} の Webhook URL は {WebhookKinds.UrlExample(kind)} の形です。";
        }

        return null;
    }

    /// <summary>画面に出す断片。どの宛先かは見分けられるが、断片からは投稿できない。</summary>
    public static string BuildHint(string url)
    {
        var value = url.Trim();
        var host = Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri.Host : string.Empty;
        var tail = value.Length <= 4 ? value : value[^4..];
        var hint = $"{host}/…/{tail}";

        return hint.Length <= WebhookLimits.MaxHintChars ? hint : hint[..WebhookLimits.MaxHintChars];
    }

    private static bool MatchesKind(WebhookKind kind, Uri uri)
    {
        var host = uri.Host;
        var path = uri.AbsolutePath;

        return kind switch
        {
            WebhookKind.Slack =>
                Same(host, "hooks.slack.com") && path.StartsWith("/services/", StringComparison.Ordinal),
            WebhookKind.Discord =>
                DiscordHosts.Any(allowed => Same(host, allowed)) && path.Contains("/api/webhooks/", StringComparison.Ordinal),
            // 前方一致ではなく接尾辞で見る。前方一致だと logic.azure.com.example.net が通ってしまう
            WebhookKind.MicrosoftTeams =>
                host.EndsWith(TeamsHostSuffix, StringComparison.OrdinalIgnoreCase)
                && host.Length > TeamsHostSuffix.Length
                && path.Contains("/workflows/", StringComparison.Ordinal)
                && path.Contains("/triggers/", StringComparison.Ordinal),
            WebhookKind.GoogleChat =>
                Same(host, "chat.googleapis.com") && path.StartsWith("/v1/spaces/", StringComparison.Ordinal),
            _ => false
        };
    }

    private static bool Same(string host, string expected) =>
        string.Equals(host, expected, StringComparison.OrdinalIgnoreCase);
}
