using ConAI.Web.Notifications;
using ConAI.Web.Services;

namespace ConAI.Web.Infrastructure;

public sealed class GenerationWorker : BackgroundService
{
    public const string FailureMessage = "議事録の生成に失敗しました。しばらく待ってからやり直してください。";

    private readonly IGenerationQueue _queue;
    private readonly INotificationQueue _notifications;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GenerationWorker> _logger;

    public GenerationWorker(
        IGenerationQueue queue,
        INotificationQueue notifications,
        IServiceScopeFactory scopeFactory,
        ILogger<GenerationWorker> logger)
    {
        _queue = queue;
        _notifications = notifications;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var meetingId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await RunOneAsync(meetingId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // ここで抜けると BackgroundService が既定で静かに停止し、以後の生成が永久に Queued のまま残る。
                _logger.LogError(exception, "Generation worker recovered from an unhandled failure for meeting {MeetingId}", meetingId);
            }
        }
    }

    private async Task RunOneAsync(Guid meetingId, CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();

        try
        {
            await meetings.MarkRunningAsync(meetingId, stoppingToken);

            var generation = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();
            var outcome = await generation.GenerateAsync(meetingId, stoppingToken);

            // Media から起こしたときだけ文字起こしを書き戻す。Live の追記や手編集を上書きしない。
            // TranslatedTranscription も Live が書く欄なので、Transcription と同じ条件で守る。
            await meetings.MarkSucceededAsync(
                meetingId,
                outcome.TranscribedFromMedia ? outcome.Result.Transcription : null,
                outcome.TranscribedFromMedia ? outcome.Result.TranslatedTranscription ?? string.Empty : null,
                outcome.Result.Minutes,
                stoppingToken);

            _logger.LogInformation("Generation finished for meeting {MeetingId}", meetingId);

            await EnqueueNotificationAsync(meetingId, NotificationEvent.Succeeded, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 停止中の打ち切りは、再起動時に ResetInterruptedJobsAsync が拾う。
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Generation failed for meeting {MeetingId}", meetingId);

            try
            {
                await meetings.MarkFailedAsync(meetingId, FailureMessage, CancellationToken.None);
                await EnqueueNotificationAsync(meetingId, NotificationEvent.Failed, CancellationToken.None);
            }
            catch (Exception markFailure)
            {
                // 後始末に失敗しても、次のジョブは処理し続ける。取り残しは起動時の ResetInterruptedJobsAsync が拾う。
                _logger.LogError(markFailure, "Failed to mark meeting {MeetingId} as failed", meetingId);
            }
        }
    }

    private async Task EnqueueNotificationAsync(
        Guid meetingId, NotificationEvent notificationEvent, CancellationToken cancellationToken)
    {
        try
        {
            await _notifications.EnqueueAsync(new NotificationRequest(meetingId, notificationEvent), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // 通知は生成の結果を左右しない。積めなくても、決まった成否はそのまま残す
            _logger.LogError(exception, "Failed to enqueue a notification for meeting {MeetingId}", meetingId);
        }
    }
}
