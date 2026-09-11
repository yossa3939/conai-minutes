using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingDataModelTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingDataModelTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 会議と添付を保存して読み戻せる()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = "user-1",
            Title = "定例会議",
            HeldAt = new DateTime(2026, 8, 17, 10, 0, 0, DateTimeKind.Unspecified),
            LiveMode = true,
            TranslateMode = true,
            TargetLanguage = "en"
        };
        meeting.Files.Add(new MeetingFile
        {
            Kind = MeetingFileKind.Reference,
            OriginalFileName = "資料.pdf",
            Extension = ".pdf",
            ContentType = "application/pdf",
            SizeBytes = 1234
        });
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        var loaded = await db.Meetings.Include(m => m.Files).FirstAsync(m => m.Id == meeting.Id);

        Assert.Equal("定例会議", loaded.Title);
        Assert.Equal(GenerationStatus.None, loaded.GenerationStatus);
        Assert.Equal(DateTimeKind.Unspecified, loaded.HeldAt!.Value.Kind);
        Assert.Single(loaded.Files);
    }

    [Fact]
    public async Task 会議を消すと添付も連鎖削除される()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting { OwnerId = "user-2", Title = "削除対象" };
        meeting.Files.Add(new MeetingFile
        {
            Kind = MeetingFileKind.Media,
            OriginalFileName = "a.mp3",
            Extension = ".mp3",
            ContentType = "audio/mpeg",
            SizeBytes = 10
        });
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        var fileId = meeting.Files.First().Id;

        db.Meetings.Remove(meeting);
        await db.SaveChangesAsync();

        Assert.Null(await db.MeetingFiles.FindAsync(fileId));
    }

    [Fact]
    public async Task 議事録テンプレートを保存して読み戻せる()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var template = new MinutesTemplate
        {
            OwnerId = "template-user-1",
            Name = "標準",
            Body = "## 決定事項\n- 決定事項\n",
            IsDefault = true
        };
        db.MinutesTemplates.Add(template);
        await db.SaveChangesAsync();

        var loaded = await db.MinutesTemplates.FirstAsync(t => t.Id == template.Id);

        Assert.Equal("標準", loaded.Name);
        Assert.Contains("決定事項", loaded.Body);
        Assert.True(loaded.IsDefault);
    }

    [Fact]
    public async Task 既定のテンプレートは利用者ごとに1件しか保存できない()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.MinutesTemplates.Add(new MinutesTemplate
        {
            OwnerId = "template-user-2",
            Name = "1 件目",
            Body = "## 要約\n",
            IsDefault = true
        });
        await db.SaveChangesAsync();

        db.MinutesTemplates.Add(new MinutesTemplate
        {
            OwnerId = "template-user-2",
            Name = "2 件目",
            Body = "## 要約\n",
            IsDefault = true
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task 既定でないテンプレートは同じ利用者で何件でも保存できる()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        db.MinutesTemplates.AddRange(
            new MinutesTemplate { OwnerId = "template-user-3", Name = "A", Body = "## A\n" },
            new MinutesTemplate { OwnerId = "template-user-3", Name = "B", Body = "## B\n" });
        await db.SaveChangesAsync();

        var count = await db.MinutesTemplates.CountAsync(t => t.OwnerId == "template-user-3");

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task テンプレートを消すと会議の参照はnullに戻る()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var template = new MinutesTemplate
        {
            OwnerId = "template-user-4",
            Name = "消す対象",
            Body = "## 要約\n"
        };
        db.MinutesTemplates.Add(template);
        await db.SaveChangesAsync();

        var meeting = new Meeting
        {
            OwnerId = "template-user-4",
            Title = "参照している会議",
            MinutesTemplateId = template.Id
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        db.MinutesTemplates.Remove(template);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var loaded = await db.Meetings.FirstAsync(m => m.Id == meeting.Id);

        Assert.Null(loaded.MinutesTemplateId);
    }

    [Fact]
    public void 対応言語は11種で未知のコードを弾く()
    {
        Assert.Equal(11, SupportedLanguages.All.Count);
        Assert.True(SupportedLanguages.IsSupported("ja"));
        Assert.True(SupportedLanguages.IsSupported("th"));
        Assert.True(SupportedLanguages.IsSupported("zh-TW"));
        Assert.Equal("中文（繁体）", SupportedLanguages.DisplayName("zh-TW"));
        Assert.False(SupportedLanguages.IsSupported("xx"));
        Assert.False(SupportedLanguages.IsSupported(null));
        Assert.Equal("日本語", SupportedLanguages.DisplayName("ja"));
    }

    [Fact]
    public async Task 往復を消すと根拠も連鎖削除される()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting { OwnerId = "chat-cascade", Title = "予算会議" };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        var turn = new ChatTurn
        {
            OwnerId = "chat-cascade",
            Question = "決定事項は何ですか",
            Answer = "増額要求を承認しました。",
            CreatedAt = new DateTime(2026, 9, 4, 10, 0, 0, DateTimeKind.Utc),
            Sources = { new ChatTurnSource { MeetingId = meeting.Id, MeetingTitle = "予算会議", Order = 0 } }
        };
        db.ChatTurns.Add(turn);
        await db.SaveChangesAsync();

        db.ChatTurns.Remove(turn);
        await db.SaveChangesAsync();

        Assert.Empty(await db.ChatTurnSources.AsNoTracking().Where(s => s.ChatTurnId == turn.Id).ToListAsync());
    }

    [Fact]
    public async Task 会議を消しても往復は残り根拠の会議IDだけがNULLになる()
    {
        Guid turnId;
        Guid meetingId;

        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meeting = new Meeting { OwnerId = "chat-setnull", Title = "消える会議" };
            db.Meetings.Add(meeting);
            await db.SaveChangesAsync();
            meetingId = meeting.Id;

            var turn = new ChatTurn
            {
                OwnerId = "chat-setnull",
                Question = "何が決まりましたか",
                Answer = "期日を来週にしました。",
                CreatedAt = new DateTime(2026, 9, 4, 11, 0, 0, DateTimeKind.Utc),
                Sources = { new ChatTurnSource { MeetingId = meeting.Id, MeetingTitle = "消える会議", Order = 0 } }
            };
            db.ChatTurns.Add(turn);
            await db.SaveChangesAsync();
            turnId = turn.Id;
        }

        using (var scope = _factory.CreateScope())
        {
            // 会議の削除は MeetingService を通る。SQLite が ON DELETE SET NULL を実行するので、
            // アプリ側から根拠を書き戻す処理は持たない。
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            Assert.Equal(DeleteResult.Deleted, await meetings.DeleteAsync(meetingId, "chat-setnull", CancellationToken.None));
        }

        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sources = await db.ChatTurnSources.AsNoTracking()
                .Where(s => s.ChatTurnId == turnId)
                .ToListAsync();

            var source = Assert.Single(sources);
            Assert.Null(source.MeetingId);
            Assert.Equal("消える会議", source.MeetingTitle);
            Assert.True(await db.ChatTurns.AsNoTracking().AnyAsync(t => t.Id == turnId));
        }
    }
}
