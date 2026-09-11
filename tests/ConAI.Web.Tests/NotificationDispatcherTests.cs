using System.Collections.Concurrent;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class NotificationDispatcherTests
{
    private const string OwnerId = "dispatch-owner";
    private const string SlackUrl = "https://hooks.slack.com/services/T00000000/B00000000/pQ7x";
    private const string ChatUrl = "https://chat.googleapis.com/v1/spaces/AAAA/messages";

    private sealed class ScriptedWebhookSender : IWebhookSender
    {
        private readonly ConcurrentQueue<WebhookSendResult> _scripted = new();

        public ConcurrentQueue<FakeWebhookSend> Sent { get; } = new();

        public WebhookSendResult Default { get; set; } = new(WebhookSendOutcome.Succeeded, 200, null, null);

        /// <summary>送信を途中で止めたいテストのための差し込み口。記録したあとに待つ。</summary>
        public Func<WebhookKind, Task>? OnSend { get; set; }

        public void Script(params WebhookSendResult[] results)
        {
            foreach (var result in results)
            {
                _scripted.Enqueue(result);
            }
        }

        public async Task<WebhookSendResult> SendAsync(
            WebhookKind kind, string url, string json, CancellationToken cancellationToken)
        {
            Sent.Enqueue(new FakeWebhookSend(kind, json));

            if (OnSend is not null)
            {
                await OnSend(kind);
            }

            return _scripted.TryDequeue(out var result) ? result : Default;
        }
    }

    private sealed class RecordingDelay : INotificationDelay
    {
        public ConcurrentQueue<TimeSpan> Waits { get; } = new();

        public Task WaitAsync(TimeSpan duration, CancellationToken cancellationToken)
        {
            Waits.Enqueue(duration);
            return Task.CompletedTask;
        }
    }

    /// <summary>送信間隔そのものは WebhookPacerTests で見る。ここでは再送の間隔だけを見たい。</summary>
    private sealed class ZeroPacer : IWebhookPacer
    {
        public TimeSpan Reserve(Guid endpointId) => TimeSpan.Zero;
    }

    private sealed class Harness : IDisposable
    {
        public ScriptedWebhookSender Sender { get; } = new();

        public RecordingDelay Delay { get; } = new();

        public ConAIWebApplicationFactory Factory { get; }

        public Harness(string? maxRetries = null)
        {
            var sender = Sender;
            var delay = Delay;

            Factory = new ConAIWebApplicationFactory
            {
                ExtraSettings = maxRetries is null
                    ? new Dictionary<string, string?>()
                    : new Dictionary<string, string?> { ["Notifications:MaxRetries"] = maxRetries },
                ConfigureServices = services =>
                {
                    services.AddSingleton<IWebhookSender>(sender);
                    services.AddSingleton<INotificationDelay>(delay);
                    services.AddSingleton<IWebhookPacer, ZeroPacer>();
                }
            };
        }

        public IReadOnlyList<TimeSpan> Backoffs => Delay.Waits.Where(wait => wait > TimeSpan.Zero).ToList();

        public void Dispose() => Factory.Dispose();
    }

    private static async Task<Guid> AddMeetingAsync(ConAIWebApplicationFactory factory, string minutes = "決めたこと")
    {
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var meeting = new Meeting
        {
            OwnerId = OwnerId,
            Title = "定例",
            Minutes = minutes,
            GenerationStatus = GenerationStatus.Succeeded
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        return meeting.Id;
    }

    private static async Task<Guid> AddEndpointAsync(
        ConAIWebApplicationFactory factory,
        string name,
        WebhookKind kind = WebhookKind.Slack,
        string url = SlackUrl,
        bool onSuccess = true,
        bool onFailure = true,
        bool enabled = true)
    {
        using var scope = factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var (result, error) = await service.CreateAsync(
            OwnerId, new WebhookEndpointEdit(name, kind, url, onSuccess, onFailure, enabled), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);
        Assert.Null(error);

        var endpoints = await service.ListAsync(OwnerId, CancellationToken.None);
        return endpoints.Single(e => e.Name == name).Id;
    }

    private static async Task DispatchAsync(ConAIWebApplicationFactory factory, NotificationRequest request)
    {
        using var scope = factory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        await dispatcher.DispatchAsync(request, CancellationToken.None);
    }

    private static async Task<WebhookEndpoint?> GetAsync(ConAIWebApplicationFactory factory, Guid endpointId)
    {
        using var scope = factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        return await service.GetAsync(endpointId, OwnerId, CancellationToken.None);
    }

    [Fact]
    public async Task 購読している宛先すべてに1通ずつ送る()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        await AddEndpointAsync(harness.Factory, "Slack");
        await AddEndpointAsync(harness.Factory, "Chat", WebhookKind.GoogleChat, ChatUrl);

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Equal(2, harness.Sender.Sent.Count);
        Assert.Contains(harness.Sender.Sent, sent => sent.Kind == WebhookKind.Slack);
        Assert.Contains(harness.Sender.Sent, sent => sent.Kind == WebhookKind.GoogleChat);
        Assert.All(harness.Sender.Sent, sent => Assert.Contains("議事録ができました", sent.Json));
    }

    [Fact]
    public async Task 返らない宛先が同じ会議の残りを待たせない()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);

        // 宛先は名前順に並ぶ。先頭の Slack をわざと返さず、あとの Chat が追い越せることを見る
        await AddEndpointAsync(harness.Factory, "A とまる");
        await AddEndpointAsync(harness.Factory, "B すすむ", WebhookKind.GoogleChat, ChatUrl);

        var chatArrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Sender.OnSend = kind =>
        {
            if (kind != WebhookKind.GoogleChat)
            {
                return chatArrived.Task;
            }

            chatArrived.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded))
                .WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            // 直列のままだと Slack が返らずここまで来る。止まった送信を解いてから終わる
            chatArrived.TrySetResult();
        }

        Assert.Equal(2, harness.Sender.Sent.Count);
    }

    [Fact]
    public async Task 購読していない宛先には送らない()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        await AddEndpointAsync(harness.Factory, "失敗だけ", onSuccess: false);

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Empty(harness.Sender.Sent);
    }

    [Fact]
    public async Task 手動送信は購読設定で絞らない()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        await AddEndpointAsync(harness.Factory, "どちらも切", onSuccess: false, onFailure: false);

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Manual));

        var sent = Assert.Single(harness.Sender.Sent);
        Assert.Contains("議事録を共有します", sent.Json);
    }

    [Fact]
    public async Task 一時失敗は1秒4秒16秒の間隔で3回まで再送する()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        var endpointId = await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Default = new WebhookSendResult(WebhookSendOutcome.Retryable, 503, null, "受け取れませんでした。");

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Equal(4, harness.Sender.Sent.Count);
        Assert.Equal(
            [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(16)],
            harness.Backoffs);

        var endpoint = await GetAsync(harness.Factory, endpointId);
        Assert.Equal(WebhookDeliveryStatus.Failed, endpoint!.LastStatus);
        // 一時失敗では宛先を落とさない。落とすと、相手が復旧しても通知が来なくなる
        Assert.True(endpoint.IsEnabled);
    }

    [Fact]
    public async Task 再送の間隔は60秒で頭打ちにする()
    {
        // 再送の上限は設定で 5 まで上げられる。4 の累乗のままだと最後の 1 回で 4 分以上待つ
        using var harness = new Harness(maxRetries: "5");
        var meetingId = await AddMeetingAsync(harness.Factory);
        await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Default = new WebhookSendResult(WebhookSendOutcome.Retryable, 503, null, "受け取れませんでした。");

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Equal(6, harness.Sender.Sent.Count);
        Assert.Equal(
            [
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(4),
                TimeSpan.FromSeconds(16),
                TimeSpan.FromSeconds(60),
                TimeSpan.FromSeconds(60)
            ],
            harness.Backoffs);
    }

    [Fact]
    public async Task 相手が待ち時間を返したらその値に従う()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Script(
            new WebhookSendResult(WebhookSendOutcome.Retryable, 429, TimeSpan.FromSeconds(7), "混んでいます。"),
            new WebhookSendResult(WebhookSendOutcome.Succeeded, 200, null, null));

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Equal(2, harness.Sender.Sent.Count);
        Assert.Equal([TimeSpan.FromSeconds(7)], harness.Backoffs);
    }

    [Fact]
    public async Task 失効は再送せず宛先を無効にする()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        var endpointId = await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Default = new WebhookSendResult(
            WebhookSendOutcome.Revoked, 404, null, "宛先が見つかりません（HTTP 404）。");

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Single(harness.Sender.Sent);
        Assert.Empty(harness.Backoffs);

        var endpoint = await GetAsync(harness.Factory, endpointId);
        Assert.False(endpoint!.IsEnabled);
        Assert.Contains("宛先が見つかりません", endpoint.LastError);
    }

    [Fact]
    public async Task 恒久失敗は再送せず宛先は有効のまま残す()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        var endpointId = await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Default = new WebhookSendResult(
            WebhookSendOutcome.Permanent, 400, null, "Slack が受け取りませんでした（HTTP 400）。");

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        Assert.Single(harness.Sender.Sent);

        var endpoint = await GetAsync(harness.Factory, endpointId);
        Assert.True(endpoint!.IsEnabled);
        Assert.Equal(WebhookDeliveryStatus.Failed, endpoint.LastStatus);
    }

    [Fact]
    public async Task 復号できない宛先はその1件だけを無効にする()
    {
        using var harness = new Harness();
        var meetingId = await AddMeetingAsync(harness.Factory);
        var brokenId = await AddEndpointAsync(harness.Factory, "壊れた");
        var healthyId = await AddEndpointAsync(harness.Factory, "無事", WebhookKind.GoogleChat, ChatUrl);

        using (var scope = harness.Factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var broken = await db.WebhookEndpoints.SingleAsync(e => e.Id == brokenId);
            broken.ProtectedUrl = "これは保護された値ではない";
            await db.SaveChangesAsync();
        }

        await DispatchAsync(harness.Factory, new NotificationRequest(meetingId, NotificationEvent.Succeeded));

        var sent = Assert.Single(harness.Sender.Sent);
        Assert.Equal(WebhookKind.GoogleChat, sent.Kind);

        var broken2 = await GetAsync(harness.Factory, brokenId);
        Assert.False(broken2!.IsEnabled);
        Assert.Equal("保存した URL を読み出せませんでした。URL を登録し直してください。", broken2.LastError);

        var healthy = await GetAsync(harness.Factory, healthyId);
        Assert.True(healthy!.IsEnabled);
    }

    [Fact]
    public async Task 会議が無ければ何も送らない()
    {
        using var harness = new Harness();
        await AddEndpointAsync(harness.Factory, "Slack");

        await DispatchAsync(harness.Factory, new NotificationRequest(Guid.NewGuid(), NotificationEvent.Succeeded));

        Assert.Empty(harness.Sender.Sent);
    }

    [Fact]
    public async Task テスト送信は無効な宛先にも送り成功したら有効に戻す()
    {
        using var harness = new Harness();
        var endpointId = await AddEndpointAsync(harness.Factory, "止まっている", enabled: false);

        using var scope = harness.Factory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var result = await dispatcher.SendTestAsync(endpointId, OwnerId, CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Succeeded, result.Outcome);
        var sent = Assert.Single(harness.Sender.Sent);
        Assert.Contains("ConAI からのテスト通知です", sent.Json);

        var endpoint = await GetAsync(harness.Factory, endpointId);
        Assert.True(endpoint!.IsEnabled);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, endpoint.LastStatus);
    }

    [Fact]
    public async Task テスト送信は再送しない()
    {
        using var harness = new Harness();
        var endpointId = await AddEndpointAsync(harness.Factory, "Slack");
        harness.Sender.Default = new WebhookSendResult(WebhookSendOutcome.Retryable, 503, null, "受け取れませんでした。");

        using var scope = harness.Factory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var result = await dispatcher.SendTestAsync(endpointId, OwnerId, CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
        Assert.Single(harness.Sender.Sent);
    }

    [Fact]
    public async Task 他人の宛先へはテスト送信しない()
    {
        using var harness = new Harness();
        var endpointId = await AddEndpointAsync(harness.Factory, "Slack");

        using var scope = harness.Factory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();
        var result = await dispatcher.SendTestAsync(endpointId, "別の人", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Permanent, result.Outcome);
        Assert.Empty(harness.Sender.Sent);
    }
}
