using System.Threading.Channels;

namespace ConAI.Web.Notifications;

public interface INotificationQueue
{
    ValueTask EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<NotificationRequest> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed class NotificationQueue : INotificationQueue
{
    // ワーカーが会議を並行して扱うため、読み手を 1 本に限る設定は置かない
    private readonly Channel<NotificationRequest> _channel = Channel.CreateUnbounded<NotificationRequest>();

    public ValueTask EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(request, cancellationToken);

    public IAsyncEnumerable<NotificationRequest> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
