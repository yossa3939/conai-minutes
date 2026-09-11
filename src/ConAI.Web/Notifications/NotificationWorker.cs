using ConAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Notifications;

public sealed class NotificationWorker : BackgroundService
{
    private readonly INotificationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationWorker> _logger;

    public NotificationWorker(
        INotificationQueue queue,
        IServiceScopeFactory scopeFactory,
        IOptions<NotificationOptions> options,
        ILogger<NotificationWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1 件ずつ処理すると、再送を繰り返す宛先を持つ会議の後ろで、
        // 他の利用者の通知まで数分待たされる。同時に扱う会議数に幅を持たせる
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = _options.MaxConcurrentMeetings,
            CancellationToken = stoppingToken
        };

        await Parallel.ForEachAsync(_queue.DequeueAllAsync(stoppingToken), options, async (request, token) =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
                await dispatcher.DispatchAsync(request, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // ここで抜けると残りの会議まで打ち切られ、以後の通知が二度と送られない
                _logger.LogError(
                    exception,
                    "Notification worker recovered from an unhandled failure for meeting {MeetingId}",
                    request.MeetingId);
            }
        });
    }
}
