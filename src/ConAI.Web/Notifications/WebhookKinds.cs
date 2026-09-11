using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

/// <summary>種別ごとの表示名と URL の見本。画面とエラーメッセージで同じ文言を使う。</summary>
public static class WebhookKinds
{
    public static IReadOnlyList<WebhookKind> All { get; } =
    [
        WebhookKind.Slack,
        WebhookKind.Discord,
        WebhookKind.MicrosoftTeams,
        WebhookKind.GoogleChat
    ];

    public static string DisplayName(WebhookKind kind) => kind switch
    {
        WebhookKind.Slack => "Slack",
        WebhookKind.Discord => "Discord",
        WebhookKind.MicrosoftTeams => "Microsoft Teams",
        WebhookKind.GoogleChat => "Google Chat",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知の宛先種別です。")
    };

    /// <summary>入力を間違えた利用者に見せる URL の形。</summary>
    public static string UrlExample(WebhookKind kind) => kind switch
    {
        WebhookKind.Slack => "https://hooks.slack.com/services/…",
        WebhookKind.Discord => "https://discord.com/api/webhooks/…",
        WebhookKind.MicrosoftTeams => "https://<名前>.logic.azure.com/workflows/…/triggers/…",
        WebhookKind.GoogleChat => "https://chat.googleapis.com/v1/spaces/…",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知の宛先種別です。")
    };
}
