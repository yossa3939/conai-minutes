namespace ConAI.Web.Notifications;

public enum WebhookSendOutcome
{
    Succeeded = 0,
    /// <summary>時間をおけば通る見込みがある。再送の対象。</summary>
    Retryable = 1,
    /// <summary>何度送っても通らない。宛先は有効なまま残す。</summary>
    Permanent = 2,
    /// <summary>宛先そのものが失効した。自動で無効にする。</summary>
    Revoked = 3
}

public sealed record WebhookSendResult(
    WebhookSendOutcome Outcome,
    int? StatusCode,
    TimeSpan? RetryAfter,
    string? Message);
