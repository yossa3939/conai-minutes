using ConAI.Web.Data;
using ConAI.Web.Search;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace ConAI.Web.Tests;

public class MeetingSearchServiceTests : IClassFixture<MeetingSearchServiceTests.SearchFactory>
{
    private readonly SearchFactory _factory;

    public MeetingSearchServiceTests(SearchFactory factory) => _factory = factory;

    [Fact]
    public async Task 索引した会議が検索で該当する()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "予算検討会", minutes: "島田さんから予算の見直しについて話があった");

        var outcome = await SearchAsync(owner, "予算");

        Assert.Equal(SearchStatus.Found, outcome.Status);
        Assert.Equal(1, outcome.Total);
        Assert.Equal(SearchMatchKind.Minutes, outcome.Hits[0].MatchedIn);
        Assert.Contains("予算", outcome.Hits[0].Excerpt);
    }

    [Fact]
    public async Task 文字起こしにしか無い語でも該当する()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "昼食の話", transcription: "来期の採用について話した");

        var outcome = await SearchAsync(owner, "採用");

        Assert.Equal(SearchStatus.Found, outcome.Status);
        Assert.Equal(SearchMatchKind.Transcript, outcome.Hits[0].MatchedIn);
        Assert.Equal("文字起こしに一致", outcome.Hits[0].Excerpt);
    }

    [Fact]
    public async Task 翻訳文字起こしにしか無い語でも該当する()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(
            owner,
            "海外拠点との定例",
            minutes: "昼食の話",
            transcription: "We discussed the new warehouse.",
            translatedTranscription: "新しい倉庫について話した");

        var outcome = await SearchAsync(owner, "倉庫");

        // 索引の 4 列目。議事録にも文字起こしにも無いので、判定は文字起こし扱いになる
        Assert.Equal(SearchStatus.Found, outcome.Status);
        Assert.Equal(SearchMatchKind.Transcript, outcome.Hits[0].MatchedIn);
    }

    [Fact]
    public async Task 会議名に一致した会議が上位に来る()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");
        await AddAndIndexAsync(owner, "予算検討会", minutes: "昼食の話をした");

        var outcome = await SearchAsync(owner, "予算");

        // bm25 の重みは会議名が 10、議事録が 3。会議名に含まれる語は主題を直接表す。
        Assert.Equal("予算検討会", outcome.Hits[0].Title);
    }

    [Fact]
    public async Task 更新すると古い語では該当しなくなる()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        var meeting = await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Meetings.FindAsync(meeting.Id);
            stored!.Minutes = "昼食の話をした";
            stored.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();
            await indexer.IndexAsync(meeting.Id, CancellationToken.None);
        }

        Assert.Equal(SearchStatus.Empty, (await SearchAsync(owner, "予算")).Status);
        Assert.Equal(SearchStatus.Found, (await SearchAsync(owner, "昼食")).Status);
    }

    [Fact]
    public async Task 版番号を変えると未索引として数えられる()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        var meeting = await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var document = db.MeetingSearchDocuments.First(d => d.MeetingId == meeting.Id);
            document.TokenizerVersion = SearchLimits.TokenizerVersion + 1;
            await db.SaveChangesAsync();
        }

        var outcome = await SearchAsync(owner, "予算");

        Assert.Equal(1, outcome.IndexingRemaining);
        Assert.Contains(outcome.Notes, note => note.Contains("索引を作成中です"));
    }

    [Fact]
    public async Task 他人の会議は検索結果に出ない()
    {
        var mine = $"search-{Guid.NewGuid():N}";
        var yours = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(yours, "予算検討会", minutes: "予算の話をした");

        var outcome = await SearchAsync(mine, "予算");

        Assert.Equal(SearchStatus.Empty, outcome.Status);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public async Task 辞書に無い語は引き直しで該当する()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "型番 zx-4400 の在庫を確認する");

        // 記号を挟んだ型番は分かち書きで割れるため、FTS5 では当たらないことがある。
        var outcome = await SearchAsync(owner, "zx-4400");

        Assert.True(outcome.Status is SearchStatus.Found or SearchStatus.Fallback);
        Assert.Equal(1, outcome.Total);
    }

    [Fact]
    public async Task 引き直しでも該当しなければ空が返る()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");

        var outcome = await SearchAsync(owner, "存在しない語句");

        Assert.Equal(SearchStatus.Empty, outcome.Status);
        Assert.Equal(0, outcome.Total);
    }

    [Fact]
    public async Task 長すぎる検索語では引き直しが走らない()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");

        // 200 文字を超える入力を丸ごと照合すると、全走査の費用を払ったうえで必ず 0 件になる。
        var outcome = await SearchAsync(owner, new string('ぁ', SearchLimits.MaxFallbackQueryChars + 1));

        Assert.Equal(SearchStatus.Empty, outcome.Status);
        Assert.DoesNotContain(outcome.Notes, note => note == SearchNotes.FellBackToLike);
    }

    [Fact]
    public async Task 語が多い検索では切り詰めの注記が付く()
    {
        var owner = $"search-{Guid.NewGuid():N}";
        await AddAndIndexAsync(owner, "定例会", minutes: "予算の話をした");

        var query = string.Join(' ', Enumerable.Range(0, 40).Select(i => $"予算{i}"));
        var outcome = await SearchAsync(owner, query);

        Assert.Contains(SearchNotes.QueryTruncated, outcome.Notes);
    }

    [Fact]
    public async Task ページングが総件数と一致する()
    {
        var owner = $"search-{Guid.NewGuid():N}";

        for (var i = 0; i < SearchLimits.PageSize + 3; i++)
        {
            await AddAndIndexAsync(owner, $"予算検討会 {i}", minutes: "予算の話をした");
        }

        var first = await SearchAsync(owner, "予算", page: 1);
        var second = await SearchAsync(owner, "予算", page: 2);

        Assert.Equal(SearchLimits.PageSize + 3, first.Total);
        Assert.Equal(SearchLimits.PageSize, first.Hits.Count);
        Assert.Equal(3, second.Hits.Count);
        Assert.Empty(first.Hits.Select(h => h.MeetingId).Intersect(second.Hits.Select(h => h.MeetingId)));
    }

    private async Task<SearchOutcome> SearchAsync(string ownerId, string query, int page = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IMeetingSearchService>();

        return await search.SearchAsync(ownerId, query, page, CancellationToken.None);
    }

    private async Task<Meeting> AddAndIndexAsync(
        string ownerId,
        string title,
        string minutes = "",
        string transcription = "",
        string translatedTranscription = "")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = title,
            Minutes = minutes,
            Transcription = transcription,
            TranslatedTranscription = translatedTranscription,
            HeldAt = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        // ワーカーを待つと 1 件あたり最大 20 秒かかる。索引の入り口を直に呼ぶ。
        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();
        await indexer.IndexAsync(meeting.Id, CancellationToken.None);

        return meeting;
    }

    /// <summary>索引ワーカーが 10 秒ごとに索引を作り直すと、版番号を変えた直後の観測が揺れる。
    /// この試験は索引の入り口を直に呼ぶため、常駐ワーカーは要らない。</summary>
    public sealed class SearchFactory : ConAIWebApplicationFactory
    {
        public SearchFactory() => ConfigureServices = services => services.RemoveAll<IHostedService>();
    }
    [Fact]
    public async Task 上限を超える検索語は契約側でも弾かれる()
    {
        // 長さの検証はエンドポイントにもあるが、サービスを直接呼ぶ呼び出し元が増えても
        // 契約が自分を守れるようにする。ChatService.AskAsync と同じ守り方である。
        using var factory = new ConAIWebApplicationFactory();
        using var scope = factory.Services.CreateScope();
        var search = scope.ServiceProvider.GetRequiredService<IMeetingSearchService>();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => search.SearchAsync(
            "search-service-too-long",
            new string('あ', SearchLimits.MaxQueryChars + 1),
            page: 1,
            CancellationToken.None));
    }

}
