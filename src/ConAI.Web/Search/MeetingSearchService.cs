using ConAI.Web.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Search;

public sealed class MeetingSearchService : IMeetingSearchService
{
    // FTS5 は EF が扱えないため、この 2 つだけ生 SQL で書く。
    // bm25 の重みは会議名 10、議事録 3、文字起こしと翻訳 1。
    // 戻り値は負の数で、小さいほど関連が強いため昇順に並べる
    private const string MatchListSql = """
        SELECT d.MeetingId,
               m.Title,
               m.HeldAt,
               m.Minutes,
               bm25(MeetingSearchIndex, 10.0, 3.0, 1.0, 1.0) AS Score
        FROM MeetingSearchIndex
        JOIN MeetingSearchDocuments d ON d.Rowid = MeetingSearchIndex.rowid
        JOIN Meetings m ON m.Id = d.MeetingId
        WHERE MeetingSearchIndex MATCH $query
          AND d.OwnerId = $ownerId
        ORDER BY Score, d.Rowid
        LIMIT $take OFFSET $skip
        """;

    private const string MatchCountSql = """
        SELECT COUNT(*)
        FROM MeetingSearchIndex
        JOIN MeetingSearchDocuments d ON d.Rowid = MeetingSearchIndex.rowid
        WHERE MeetingSearchIndex MATCH $query
          AND d.OwnerId = $ownerId
        """;

    private readonly ApplicationDbContext _db;
    private readonly ISearchTokenizer _tokenizer;
    private readonly IMeetingSearchIndexer _indexer;

    public MeetingSearchService(
        ApplicationDbContext db, ISearchTokenizer tokenizer, IMeetingSearchIndexer indexer)
    {
        _db = db;
        _tokenizer = tokenizer;
        _indexer = indexer;
    }

    public async Task<SearchOutcome> SearchAsync(
        string ownerId, string query, int page, CancellationToken cancellationToken)
    {
        // 長さの検証はエンドポイントにもあるが、サービスを直接呼ぶ呼び出し元が
        // 増えても契約が自分を守れるように、ここでもう一度確かめる。
        if (query.Length > SearchLimits.MaxQueryChars)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "検索語が文字数の上限を超えています。");
        }

        var trimmed = query.Trim();
        var normalized = _tokenizer.Normalize(trimmed);
        var tokens = _tokenizer.Tokenize(trimmed, cancellationToken);
        var surfaces = _tokenizer.Surfaces(trimmed, cancellationToken);

        var currentPage = Math.Clamp(page, 1, SearchLimits.MaxPage);
        var skip = (currentPage - 1) * SearchLimits.PageSize;

        var notes = new List<string>();
        var status = SearchStatus.Empty;
        var total = 0;
        IReadOnlyList<SearchHit> hits = [];

        if (tokens.Count > 0)
        {
            var (expression, truncated) = SearchQuery.BuildMatch(tokens);

            if (truncated)
            {
                notes.Add(SearchNotes.QueryTruncated);
            }

            total = await CountMatchesAsync(ownerId, expression, cancellationToken);

            if (total > 0)
            {
                hits = await ListMatchesAsync(ownerId, expression, surfaces, skip, cancellationToken);
                status = SearchStatus.Found;
            }
        }

        // トークンが 0 個の場合も引き直す。助詞だけを打った場合と、辞書が歯が立たない記号列を
        // 打った場合の区別が付かない以上、照合してから 0 件を返すほうが誤解が少ない
        if (status == SearchStatus.Empty
            && normalized.Length > 0
            && normalized.Length <= SearchLimits.MaxFallbackQueryChars)
        {
            var pattern = SearchQuery.BuildLikePattern(normalized);
            var fallback = Fallback(ownerId, pattern);

            total = await fallback.CountAsync(cancellationToken);

            if (total > 0)
            {
                hits = await ListFallbackAsync(fallback, normalized, skip, cancellationToken);
                status = SearchStatus.Fallback;
                notes.Add(SearchNotes.FellBackToLike);
            }
        }

        var remaining = await _indexer.CountUnindexedAsync(ownerId, cancellationToken);

        if (remaining > 0)
        {
            notes.Add(SearchNotes.Indexing(remaining));
        }

        return new SearchOutcome(status, total, currentPage, remaining, hits, notes);
    }

    private async Task<int> CountMatchesAsync(
        string ownerId, string expression, CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)_db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = MatchCountSql;
            command.Parameters.AddWithValue("$query", expression);
            command.Parameters.AddWithValue("$ownerId", ownerId);

            return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    private async Task<IReadOnlyList<SearchHit>> ListMatchesAsync(
        string ownerId,
        string expression,
        IReadOnlyList<string> surfaces,
        int skip,
        CancellationToken cancellationToken)
    {
        var connection = (SqliteConnection)_db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = MatchListSql;
            command.Parameters.AddWithValue("$query", expression);
            command.Parameters.AddWithValue("$ownerId", ownerId);
            command.Parameters.AddWithValue("$take", SearchLimits.PageSize);
            command.Parameters.AddWithValue("$skip", skip);

            var hits = new List<SearchHit>();

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var meetingId = Guid.Parse(reader.GetString(0));
                var title = reader.GetString(1);
                var heldAt = reader.IsDBNull(2) ? (DateTime?)null : reader.GetDateTime(2);
                var minutes = reader.GetString(3);

                hits.Add(Describe(meetingId, title, heldAt, minutes, surfaces));
            }

            return hits;
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// 会議名と議事録本文をそのまま照合する。Meetings は EF が管理する通常のテーブルなので、
    /// 生 SQL にせず ESCAPE 付きの LIKE で書く。
    /// </summary>
    private IQueryable<Meeting> Fallback(string ownerId, string pattern) =>
        _db.Meetings
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId
                && (EF.Functions.Like(m.Title, pattern, "\\")
                    || EF.Functions.Like(m.Minutes, pattern, "\\")))
            .OrderByDescending(m => m.UpdatedAt);

    private async Task<IReadOnlyList<SearchHit>> ListFallbackAsync(
        IQueryable<Meeting> fallback, string normalized, int skip, CancellationToken cancellationToken)
    {
        var rows = await fallback
            .Skip(skip)
            .Take(SearchLimits.PageSize)
            .Select(m => new { m.Id, m.Title, m.HeldAt, m.Minutes })
            .ToListAsync(cancellationToken);

        // 引き直しは正規化した入力を丸ごと照合している。位置探しにも同じ文字列を使う。
        string[] surfaces = [normalized];

        return [.. rows.Select(row => Describe(row.Id, row.Title, row.HeldAt, row.Minutes, surfaces))];
    }

    private SearchHit Describe(
        Guid meetingId, string title, DateTime? heldAt, string minutes, IReadOnlyList<string> surfaces)
    {
        var (kind, excerpt) = SearchExcerpt.Describe(
            _tokenizer.Normalize(title), _tokenizer.Normalize(minutes), surfaces);

        return new SearchHit(meetingId, title, heldAt, excerpt, kind);
    }
}
