namespace ConAI.Web.Notifications;

/// <summary>
/// 通知の待ち。<see cref="Task.Delay(TimeSpan, TimeProvider, CancellationToken)"/> を直接呼ばず
/// 差し替え口を挟むのは、再送の間隔をテストから見えるようにするため。
/// </summary>
public interface INotificationDelay
{
    Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class NotificationDelay : INotificationDelay
{
    private readonly TimeProvider _timeProvider;

    public NotificationDelay(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken) =>
        duration <= TimeSpan.Zero
            ? Task.CompletedTask
            : Task.Delay(duration, _timeProvider, cancellationToken);
}
