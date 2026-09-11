using System.Collections.Concurrent;
using ConAI.Web.Data;
using ConAI.Web.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ConAI.Web.Tests;

public class MeetingSearchIndexWorkerTests
{
    [Fact]
    public async Task 巡回で静止した会議が索引される()
    {
        // 試験ホストの常駐ワーカーを外し、こちらが作ったワーカーだけが動くようにする。
        // 外さないと、索引が本物のワーカーの仕業なのか判別できない。
        using var factory = new ConAIWebApplicationFactory
        {
            ConfigureServices = services => services.RemoveAll<IHostedService>()
        };

        Guid meetingId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meeting = new Meeting
            {
                OwnerId = "worker-owner",
                Title = "予算検討会",
                Minutes = "予算の話をした",
                CreatedAt = DateTime.UtcNow.AddMinutes(-10),
                UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
            };

            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            meetingId = meeting.Id;
        }

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var worker = new MeetingSearchIndexWorker(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            time,
            NullLogger<MeetingSearchIndexWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        // StartAsync が返っても ExecuteAsync の開始は保証されない（開始はワーカー側へ委ねられる）。
        // そのため時計はここで一気に進めず、待ちの周回ごとに進める。
        var indexed = await WaitForIndexAsync(factory, time, meetingId, TimeSpan.FromSeconds(10));

        await worker.StopAsync(CancellationToken.None);

        Assert.True(indexed);
    }

    [Fact]
    public async Task 索引に失敗した会議があっても後続の会議は索引される()
    {
        // 未索引の会議は更新の古い順に返る。先頭の 1 件が毎回例外を投げると、
        // バッチ単位で受け止めるだけでは、そのうしろに並ぶ会議が二度と索引されない。
        var poison = Guid.NewGuid();
        var healthy = Guid.NewGuid();
        var indexer = new StubIndexer(poison, healthy);

        var services = new ServiceCollection();
        services.AddSingleton<IMeetingSearchIndexer>(indexer);

        await using var provider = services.BuildServiceProvider();

        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var worker = new MeetingSearchIndexWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            time,
            NullLogger<MeetingSearchIndexWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);

        var indexed = await WaitForAsync(time, () => indexer.Indexed.Contains(healthy), TimeSpan.FromSeconds(10));

        await worker.StopAsync(CancellationToken.None);

        Assert.True(indexed);
    }

    private static async Task<bool> WaitForAsync(FakeTimeProvider time, Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            time.Advance(SearchLimits.IndexPollInterval);

            if (done())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private sealed class StubIndexer : IMeetingSearchIndexer
    {
        private readonly Guid _poison;
        private readonly Guid _healthy;

        public StubIndexer(Guid poison, Guid healthy)
        {
            _poison = poison;
            _healthy = healthy;
        }

        public ConcurrentBag<Guid> Indexed { get; } = [];

        public Task IndexAsync(Guid meetingId, CancellationToken cancellationToken)
        {
            if (meetingId == _poison)
            {
                throw new InvalidOperationException("この会議は解析できない");
            }

            Indexed.Add(meetingId);

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> ListStaleAsync(
            DateTime now, int batchSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([_poison, _healthy]);

        public Task<int> CountUnindexedAsync(string ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private static async Task<bool> WaitForIndexAsync(
        ConAIWebApplicationFactory factory, FakeTimeProvider time, Guid meetingId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            // 巡回のきっかけはこの Advance。初回は ExecuteAsync がまだ始まっておらず空振りするため、
            // 周回のたびに進めて、タイマーが登録された後の周回で発火させる。
            time.Advance(SearchLimits.IndexPollInterval);

            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            if (await db.MeetingSearchDocuments.AnyAsync(d => d.MeetingId == meetingId))
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }
}
