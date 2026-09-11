using ConAI.Web.Data;
using ConAI.Web.Notifications;

namespace ConAI.Web.Services;

public enum SaveEndpointResult
{
    Saved,
    NotFound,
    InvalidUrl,
    Duplicate,
    LimitReached
}

public interface IWebhookEndpointService
{
    Task<IReadOnlyList<WebhookEndpoint>> ListAsync(string ownerId, CancellationToken cancellationToken);

    Task<WebhookEndpoint?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    Task<(SaveEndpointResult Result, string? Error)> CreateAsync(
        string ownerId, WebhookEndpointEdit edit, CancellationToken cancellationToken);

    /// <summary>種別は登録時に決めたものを使い、<see cref="WebhookEndpointEdit.Kind"/> は見ない。</summary>
    Task<(SaveEndpointResult Result, string? Error)> UpdateAsync(
        Guid id, string ownerId, WebhookEndpointEdit edit, CancellationToken cancellationToken);

    Task<bool> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>そのイベントを購読している、有効な宛先だけを返す。</summary>
    Task<IReadOnlyList<WebhookEndpoint>> ListSubscribersAsync(
        string ownerId, NotificationEvent notificationEvent, CancellationToken cancellationToken);

    Task<bool> HasEnabledAsync(string ownerId, CancellationToken cancellationToken);

    Task MarkSucceededAsync(Guid id, CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string error, bool disable, CancellationToken cancellationToken);
}
