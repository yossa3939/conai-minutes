using System.Runtime.CompilerServices;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Infrastructure;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class NotificationWorkerTests
{
    private const string SlackUrl = "https://hooks.slack.com/services/T00000000/B00000000/pQ7x";

    /// <summary>積み込みが必ず失敗する待ち行列。生成の結果が巻き添えにならないことを見るために使う。</summary>
    private sealed class ThrowingNotificationQueue : INotificationQueue
    {
        public ValueTask EnqueueAsync(NotificationRequest request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("通知の待ち行列が壊れている");

        public async IAsyncEnumerable<NotificationRequest> DequeueAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    /// <summary>会議名で送信を見分け、片方の会議の送信だけを返さないままにする。</summary>
    private sealed class GatedWebhookSender : IWebhookSender
    {
        public const string BlockedTitle = "とまる会議";

        private readonly TaskCompletionSource _blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>止まっている会議を追い越して届いた送信。</summary>
        public TaskCompletionSource Overtaken { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WebhookSendResult> SendAsync(
            WebhookKind kind, string url, string json, CancellationToken cancellationToken)
        {
            if (json.Contains(BlockedTitle, StringComparison.Ordinal))
            {
                await _blocked.Task;
            }
            else
            {
                Overtaken.TrySetResult();
            }

            return new WebhookSendResult(WebhookSendOutcome.Succeeded, 200, null, null);
        }

        public void Release() => _blocked.TrySetResult();
    }

    private static async Task AddEndpointAsync(ConAIWebApplicationFactory factory, string ownerId)
    {
        using var scope = factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var (result, _) = await service.CreateAsync(
            ownerId,
            new WebhookEndpointEdit("Slack", WebhookKind.Slack, SlackUrl, true, true, true),
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);
    }

    private static async Task<Guid> CreateQueuedAsync(ConAIWebApplicationFactory factory, string ownerId)
    {
        using var scope = factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.CreateAsync(
            ownerId,
            new Meeting { Title = "通知つき", Transcription = "話者A: 本文。" },
            CancellationToken.None);

        Assert.True(await meetings.TryMarkQueuedAsync(meeting.Id, ownerId, CancellationToken.None));
        return meeting.Id;
    }

    private static async Task<Guid> CreateAsync(ConAIWebApplicationFactory factory, string ownerId, string title)
    {
        using var scope = factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.CreateAsync(
            ownerId,
            new Meeting { Title = title, Transcription = "話者A: 本文。" },
            CancellationToken.None);

        return meeting.Id;
    }

    private static async Task<Meeting> WaitForAsync(
        ConAIWebApplicationFactory factory, Guid id, GenerationStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (true)
        {
            using var scope = factory.CreateScope();
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await meetings.GetForGenerationAsync(id, CancellationToken.None);

            if (meeting is not null && meeting.GenerationStatus == expected)
            {
                return meeting;
            }

            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, timeout.Token);
        }
    }

    private static async Task<FakeWebhookSend> WaitForOneSendAsync(ConAIWebApplicationFactory factory)
    {
        var sender = factory.Services.GetRequiredService<FakeWebhookSender>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (sender.Sent.Count == 0)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, timeout.Token);
        }

        return sender.Sent.Single();
    }

    [Fact]
    public async Task 生成に成功すると成功の通知が送られる()
    {
        using var factory = new ConAIWebApplicationFactory();
        await AddEndpointAsync(factory, "worker-notify-a");
        var id = await CreateQueuedAsync(factory, "worker-notify-a");

        await factory.Services.GetRequiredService<IGenerationQueue>().EnqueueAsync(id, CancellationToken.None);
        await WaitForAsync(factory, id, GenerationStatus.Succeeded);

        var sent = await WaitForOneSendAsync(factory);
        Assert.Equal(WebhookKind.Slack, sent.Kind);
        Assert.Contains("議事録ができました", sent.Json);
    }

    [Fact]
    public async Task 生成に失敗すると失敗の通知が送られる()
    {
        using var factory = new ConAIWebApplicationFactory();
        var fake = factory.Services.GetRequiredService<FakeGeminiContentClient>();
        fake.Handler = (_, _) => throw new InvalidOperationException("模擬障害");

        await AddEndpointAsync(factory, "worker-notify-b");
        var id = await CreateQueuedAsync(factory, "worker-notify-b");

        await factory.Services.GetRequiredService<IGenerationQueue>().EnqueueAsync(id, CancellationToken.None);
        await WaitForAsync(factory, id, GenerationStatus.Failed);

        var sent = await WaitForOneSendAsync(factory);
        Assert.Contains("議事録の生成に失敗しました", sent.Json);
        Assert.Contains(GenerationWorker.FailureMessage, sent.Json);
    }

    [Fact]
    public async Task 宛先が無ければ何も送らずに終わる()
    {
        using var factory = new ConAIWebApplicationFactory();
        var id = await CreateQueuedAsync(factory, "worker-notify-c");

        await factory.Services.GetRequiredService<IGenerationQueue>().EnqueueAsync(id, CancellationToken.None);
        await WaitForAsync(factory, id, GenerationStatus.Succeeded);

        // 送るものが無いことは、少し待っても送信が現れないことで見る
        await Task.Delay(200);
        Assert.True(factory.Services.GetRequiredService<FakeWebhookSender>().Sent.Count == 0);
    }

    [Fact]
    public async Task 返らない会議の通知が次の会議を待たせない()
    {
        var sender = new GatedWebhookSender();
        using var factory = new ConAIWebApplicationFactory
        {
            ConfigureServices = services => services.AddSingleton<IWebhookSender>(sender)
        };

        // 宛先の送信間隔は宛先ごとに掛かる。会議を別々の利用者のものにして、間隔を待たずに済ませる
        await AddEndpointAsync(factory, "worker-notify-e");
        await AddEndpointAsync(factory, "worker-notify-f");
        var blocked = await CreateAsync(factory, "worker-notify-e", GatedWebhookSender.BlockedTitle);
        var next = await CreateAsync(factory, "worker-notify-f", "すすむ会議");

        var queue = factory.Services.GetRequiredService<INotificationQueue>();
        await queue.EnqueueAsync(new NotificationRequest(blocked, NotificationEvent.Manual), CancellationToken.None);
        await queue.EnqueueAsync(new NotificationRequest(next, NotificationEvent.Manual), CancellationToken.None);

        try
        {
            // 1 件ずつ処理していると、先に積んだ会議で止まったまま時間切れになる
            await sender.Overtaken.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            sender.Release();
        }
    }

    [Fact]
    public async Task 通知を積む処理が例外を投げても生成は成功のまま残る()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ConfigureServices = services => services.AddSingleton<INotificationQueue, ThrowingNotificationQueue>()
        };

        var id = await CreateQueuedAsync(factory, "worker-notify-d");

        await factory.Services.GetRequiredService<IGenerationQueue>().EnqueueAsync(id, CancellationToken.None);

        var meeting = await WaitForAsync(factory, id, GenerationStatus.Succeeded);
        Assert.Null(meeting.GenerationError);
        Assert.Contains("## 決定事項", meeting.Minutes);
    }
}
