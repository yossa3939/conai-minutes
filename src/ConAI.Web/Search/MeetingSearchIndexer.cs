using ConAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Search;

public sealed class MeetingSearchIndexer : IMeetingSearchIndexer
{
    private readonly ApplicationDbContext _db;
    private readonly ISearchTokenizer _tokenizer;

    public MeetingSearchIndexer(ApplicationDbContext db, ISearchTokenizer tokenizer)
    {
        _db = db;
        _tokenizer = tokenizer;
    }

    public async Task IndexAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        var meeting = await _db.Meetings
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == meetingId, cancellationToken);

        if (meeting is null)
        {
            // 巡回で拾ってから索引するまでに消された会議。次の巡回では列挙されない。
            return;
        }

        var document = await _db.MeetingSearchDocuments
            .FirstOrDefaultAsync(d => d.MeetingId == meetingId, cancellationToken);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if (document is null)
        {
            // FTS5 の行は rowid で結ぶ。先に採番しないと入れる先が決まらない
            document = new MeetingSearchDocument { MeetingId = meeting.Id, OwnerId = meeting.OwnerId };
            _db.MeetingSearchDocuments.Add(document);
            await _db.SaveChangesAsync(cancellationToken);
        }

        document.OwnerId = meeting.OwnerId;
        document.IndexedUpdatedAt = meeting.UpdatedAt;
        document.TokenizerVersion = SearchLimits.TokenizerVersion;

        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM MeetingSearchIndex WHERE rowid = {0}",
            [document.Rowid],
            cancellationToken);

        await _db.Database.ExecuteSqlRawAsync(
            "INSERT INTO MeetingSearchIndex(rowid, Title, Minutes, Transcription, TranslatedTranscription) "
            + "VALUES ({0}, {1}, {2}, {3}, {4})",
            [
                document.Rowid,
                Join(meeting.Title, cancellationToken),
                Join(meeting.Minutes, cancellationToken),
                Join(meeting.Transcription, cancellationToken),
                Join(meeting.TranslatedTranscription, cancellationToken)
            ],
            cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        // 途中で落ちれば両方が戻り、次の巡回で最初からやり直される
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> ListStaleAsync(
        DateTime now, int batchSize, CancellationToken cancellationToken)
    {
        var cutoff = now - SearchLimits.IndexQuietPeriod;

        return await Unindexed(_db.Meetings.Where(m => m.UpdatedAt <= cutoff))
            .OrderBy(m => m.UpdatedAt)
            .Select(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountUnindexedAsync(string ownerId, CancellationToken cancellationToken) =>
        await Unindexed(_db.Meetings.Where(m => m.OwnerId == ownerId))
            .CountAsync(cancellationToken);

    /// <summary>
    /// 「索引が古い」を「最新の索引行が存在しない」の否定として書く。
    /// 行が無い場合と、行はあるが古い場合と、版番号が違う場合が、これひとつに畳まれる。
    /// 生 SQL で書かないのは、日時が TEXT で保存されるためである。
    /// 書式が 1 文字でも食い違えば、判定は例外ではなく静かに常に真か常に偽へ倒れる。
    /// </summary>
    private IQueryable<Meeting> Unindexed(IQueryable<Meeting> meetings) =>
        meetings.Where(m => !_db.MeetingSearchDocuments.Any(d =>
            d.MeetingId == m.Id
            && d.IndexedUpdatedAt >= m.UpdatedAt
            && d.TokenizerVersion == SearchLimits.TokenizerVersion));

    /// <summary>FTS5 のトークナイザは unicode61 で、空白と記号で切るだけの働きをする。</summary>
    private string Join(string text, CancellationToken cancellationToken) =>
        string.Join(' ', _tokenizer.Tokenize(text, cancellationToken));
}
