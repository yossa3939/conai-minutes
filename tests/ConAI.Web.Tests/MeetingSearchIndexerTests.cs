using ConAI.Web.Data;
using ConAI.Web.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingSearchIndexerTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingSearchIndexerTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void 静止待ちの間は索引しない()
    {
        // Live 中は 2 秒ごとに文字起こしが伸びる。伸びるたびに索引すると、同じ会議を延々と作り直す。
        var updatedAt = Now.AddSeconds(-5);

        Assert.False(SearchStaleness.IsStale(Now, updatedAt, indexedUpdatedAt: null, tokenizerVersion: null));
    }

    [Fact]
    public void 索引が無ければ古いと判定される()
    {
        Assert.True(SearchStaleness.IsStale(
            Now, Now.AddMinutes(-1), indexedUpdatedAt: null, tokenizerVersion: null));
    }

    [Fact]
    public void 会議のほうが新しければ古いと判定される()
    {
        Assert.True(SearchStaleness.IsStale(
            Now, Now.AddMinutes(-1), indexedUpdatedAt: Now.AddMinutes(-2), tokenizerVersion: 1));
    }

    [Fact]
    public void 版番号が違えば古いと判定される()
    {
        Assert.True(SearchStaleness.IsStale(
            Now, Now.AddMinutes(-1), indexedUpdatedAt: Now.AddMinutes(-1),
            tokenizerVersion: SearchLimits.TokenizerVersion + 1));
    }

    [Fact]
    public void 索引が追いついていれば古くない()
    {
        Assert.False(SearchStaleness.IsStale(
            Now, Now.AddMinutes(-1), indexedUpdatedAt: Now.AddMinutes(-1),
            tokenizerVersion: SearchLimits.TokenizerVersion));
    }

    [Fact]
    public async Task 未索引の列挙が純粋関数の判定と一致する()
    {
        // 判定の定義は SearchStaleness にあり、問い合わせは LINQ で書き直している。
        // 2 つが食い違うと、索引され続けるか、まったく索引されないかという静かな挙動になる。
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();

        var fresh = await AddMeetingAsync(db, "parity", "たった今の会議", Now.AddSeconds(-1));
        var stale = await AddMeetingAsync(db, "parity", "十分前の会議", Now.AddMinutes(-10));

        var listed = await indexer.ListStaleAsync(Now, batchSize: 100, CancellationToken.None);

        Assert.Contains(stale.Id, listed);
        Assert.DoesNotContain(fresh.Id, listed);
    }

    [Fact]
    public async Task 索引すると未索引から外れる()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();

        var meeting = await AddMeetingAsync(db, "indexed-once", "予算検討会", Now.AddMinutes(-10));

        await indexer.IndexAsync(meeting.Id, CancellationToken.None);

        var listed = await indexer.ListStaleAsync(Now, batchSize: 100, CancellationToken.None);
        Assert.DoesNotContain(meeting.Id, listed);

        var document = await db.MeetingSearchDocuments.FirstAsync(d => d.MeetingId == meeting.Id);
        Assert.Equal(SearchLimits.TokenizerVersion, document.TokenizerVersion);

        var rows = await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM MeetingSearchIndex WHERE rowid = {0}", document.Rowid)
            .FirstAsync();
        Assert.Equal(1, rows);
    }

    [Fact]
    public async Task 索引しなおしても行が増えない()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();

        var meeting = await AddMeetingAsync(db, "reindex", "予算検討会", Now.AddMinutes(-10));

        await indexer.IndexAsync(meeting.Id, CancellationToken.None);
        await indexer.IndexAsync(meeting.Id, CancellationToken.None);

        var documents = await db.MeetingSearchDocuments.CountAsync(d => d.MeetingId == meeting.Id);
        Assert.Equal(1, documents);
    }

    [Fact]
    public async Task 未索引の件数が利用者ごとに数えられる()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();

        await AddMeetingAsync(db, "count-mine", "自分の会議", Now.AddMinutes(-10));
        await AddMeetingAsync(db, "count-other", "他人の会議", Now.AddMinutes(-10));

        Assert.Equal(1, await indexer.CountUnindexedAsync("count-mine", CancellationToken.None));
    }

    private static async Task<Meeting> AddMeetingAsync(
        ApplicationDbContext db, string ownerId, string title, DateTime updatedAt)
    {
        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = title,
            Minutes = "予算の話をした",
            CreatedAt = updatedAt,
            UpdatedAt = updatedAt
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        return meeting;
    }
}
