using ConAI.Web.Data;

namespace ConAI.Web.Services;

/// <summary>画面から受け取る宛先の値。<paramref name="Url"/> が空なら、保存済みの URL を変えない。</summary>
public sealed record WebhookEndpointEdit(
    string Name,
    WebhookKind Kind,
    string? Url,
    bool NotifyOnSuccess,
    bool NotifyOnFailure,
    bool IsEnabled);
