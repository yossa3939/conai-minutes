namespace ConAI.Web.Search;

/// <summary>
/// 10 秒ごとに未索引の会議を拾って索引する。
/// </summary>
public sealed class MeetingSearchIndexWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MeetingSearchIndexWorker> _logger;

    public MeetingSearchIndexWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<MeetingSearchIndexWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SearchLimits.IndexPollInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await IndexBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // ここで抜けると索引が二度と進まず、検索は黙って古い結果を返し続ける
                _logger.LogError(exception, "Search index worker recovered from an unhandled failure");
            }
        }
    }

    private async Task IndexBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();

        var stale = await indexer.ListStaleAsync(
            _timeProvider.GetUtcNow().UtcDateTime, SearchLimits.IndexBatchSize, cancellationToken);

        foreach (var meetingId in stale)
        {
            try
            {
                await indexer.IndexAsync(meetingId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 未索引の会議は更新の古い順に返る。1 件で抜けると、その会議が毎回先頭に居座り、
                // うしろに並ぶ会議が二度と索引されない。落とすのはこの 1 件だけにする。
                _logger.LogWarning(exception, "Skipped meeting {MeetingId} after an indexing failure", meetingId);
            }
        }
    }
}
