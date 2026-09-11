using System.Threading.Channels;

namespace ConAI.Web.Infrastructure;

public interface IGenerationQueue
{
    ValueTask EnqueueAsync(Guid meetingId, CancellationToken cancellationToken);

    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

public sealed class GenerationQueue : IGenerationQueue
{
    private readonly Channel<Guid> _channel =
        Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions { SingleReader = true });

    public ValueTask EnqueueAsync(Guid meetingId, CancellationToken cancellationToken) =>
        _channel.Writer.WriteAsync(meetingId, cancellationToken);

    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
