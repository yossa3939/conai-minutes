using ConAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingSearchSchemaTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingSearchSchemaTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task マイグレーションで仮想テーブルとトリガーが作られる()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var names = await db.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master "
                + "WHERE name IN ('MeetingSearchDocuments', 'MeetingSearchIndex', 'MeetingSearchDocuments_ad')")
            .ToListAsync();

        Assert.Contains("MeetingSearchDocuments", names);
        Assert.Contains("MeetingSearchIndex", names);
        Assert.Contains("MeetingSearchDocuments_ad", names);
    }

    [Fact]
    public async Task 会議を消すとトリガーで索引の行も消える()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting { OwnerId = "schema-delete", Title = "削除される会議" };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        var document = new MeetingSearchDocument
        {
            MeetingId = meeting.Id,
            OwnerId = meeting.OwnerId,
            IndexedUpdatedAt = meeting.UpdatedAt,
            TokenizerVersion = 1
        };
        db.MeetingSearchDocuments.Add(document);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO MeetingSearchIndex(rowid, Title, Minutes, Transcription, TranslatedTranscription) "
            + "VALUES ({0}, {1}, '', '', '')",
            document.Rowid, "削除 会議");

        db.Meetings.Remove(await db.Meetings.FirstAsync(m => m.Id == meeting.Id));
        await db.SaveChangesAsync();

        // 外部キーの連鎖で索引行が消え、トリガーが FTS5 の行を消す。
        var remaining = await db.Database
            .SqlQueryRaw<int>(
                "SELECT COUNT(*) AS Value FROM MeetingSearchIndex WHERE rowid = {0}", document.Rowid)
            .FirstAsync();

        Assert.Equal(0, remaining);
    }
}
