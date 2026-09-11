namespace ConAI.Web.Notifications;

/// <summary>宛先ごとに 1 秒あたり 1 通へ抑える。</summary>
public interface IWebhookPacer
{
    /// <summary>次に送ってよくなるまでの待ち時間を返し、その枠を予約する。待つのは呼び出し側。</summary>
    TimeSpan Reserve(Guid endpointId);
}

public sealed class WebhookPacer : IWebhookPacer
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    /// <summary>この数を超えたら、過ぎた予約を捨てる。宛先を作り続けても記録が増え続けないようにする。</summary>
    private const int PruneThreshold = 1000;

    private readonly Dictionary<Guid, DateTimeOffset> _nextAllowed = new();
    private readonly Lock _gate = new();
    private readonly TimeProvider _timeProvider;

    public WebhookPacer(TimeProvider timeProvider) => _timeProvider = timeProvider;

    public TimeSpan Reserve(Guid endpointId)
    {
        var now = _timeProvider.GetUtcNow();

        lock (_gate)
        {
            var sendAt = _nextAllowed.TryGetValue(endpointId, out var reserved) && reserved > now ? reserved : now;
            _nextAllowed[endpointId] = sendAt + Interval;

            if (_nextAllowed.Count > PruneThreshold)
            {
                Prune(now);
            }

            return sendAt - now;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var expired in _nextAllowed.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
        {
            _nextAllowed.Remove(expired);
        }
    }
}
