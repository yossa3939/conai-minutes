using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private static async Task<(IMeetingService Service, IServiceScope Scope)> CreateAsync(ConAIWebApplicationFactory factory)
    {
        var scope = factory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMeetingService>(), scope);
    }

    [Fact]
    public async Task 作成した会議は所有者から取得できる()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var created = await service.CreateAsync("owner-a", new Meeting { Title = "会議A" }, CancellationToken.None);
        var loaded = await service.GetAsync(created.Id, "owner-a", CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal("会議A", loaded!.Title);
        Assert.Equal("owner-a", loaded.OwnerId);
    }

    [Fact]
    public async Task 他人の会議は取得できない()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var created = await service.CreateAsync("owner-a", new Meeting { Title = "会議A" }, CancellationToken.None);

        Assert.Null(await service.GetAsync(created.Id, "owner-b", CancellationToken.None));
    }

    [Fact]
    public async Task 一覧は自分の会議だけを更新日時降順で返す()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var older = await service.CreateAsync("owner-list", new Meeting { Title = "古い" }, CancellationToken.None);
        await service.UpdateAsync(older.Id, "owner-list", new MeetingEdit("古い", null, true, false, "ja", string.Empty, string.Empty, string.Empty), CancellationToken.None);
        var newer = await service.CreateAsync("owner-list", new Meeting { Title = "新しい" }, CancellationToken.None);
        await service.UpdateAsync(newer.Id, "owner-list", new MeetingEdit("新しい", null, true, false, "ja", string.Empty, string.Empty, string.Empty), CancellationToken.None);
        await service.CreateAsync("owner-other", new Meeting { Title = "他人" }, CancellationToken.None);

        var list = await service.ListAsync("owner-list", CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal("新しい", list[0].Title);
    }

    [Fact]
    public async Task 文字起こしの追記は末尾に足される()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "追記" }, CancellationToken.None);

        await service.AppendTranscriptAsync(meeting.Id, "一文目。", "First.", CancellationToken.None);
        await service.AppendTranscriptAsync(meeting.Id, "二文目。", "Second.", CancellationToken.None);
        var loaded = await service.GetAsync(meeting.Id, "owner-a", CancellationToken.None);

        Assert.Equal("一文目。二文目。", loaded!.Transcription);
        Assert.Equal("First.Second.", loaded.TranslatedTranscription);
    }

    [Fact]
    public async Task 未知の言語コードは基本情報更新で拒否する()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "言語" }, CancellationToken.None);

        var updated = await service.UpdateAsync(
            meeting.Id, "owner-a", new MeetingEdit("言語", null, true, true, "xx", string.Empty, string.Empty, string.Empty), CancellationToken.None);

        Assert.False(updated);
    }

    [Fact]
    public async Task 生成中の会議は削除を拒否する()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "生成中" }, CancellationToken.None);
        Assert.True(await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None));

        Assert.Equal(DeleteResult.JobInProgress, await service.DeleteAsync(meeting.Id, "owner-a", CancellationToken.None));
    }

    [Fact]
    public async Task 二重のキュー投入は拒否する()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "二重" }, CancellationToken.None);

        Assert.True(await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None));
        Assert.False(await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None));
    }

    [Fact]
    public async Task 完了すると結果が保存され状態がSucceededになる()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "完了" }, CancellationToken.None);
        await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None);
        await service.MarkRunningAsync(meeting.Id, CancellationToken.None);
        await service.MarkSucceededAsync(meeting.Id, "本文", "Body", "# 議事録", CancellationToken.None);

        var loaded = await service.GetAsync(meeting.Id, "owner-a", CancellationToken.None);

        Assert.Equal(GenerationStatus.Succeeded, loaded!.GenerationStatus);
        Assert.Equal("# 議事録", loaded.Minutes);
        Assert.Null(loaded.GenerationError);
    }

    [Fact]
    public async Task MarkSucceededAsyncはnullの文字起こしを書き換えない()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync(
            "owner-a",
            new Meeting { Title = "手編集", Transcription = "手で書いた原文" },
            CancellationToken.None);

        await service.MarkSucceededAsync(meeting.Id, null, null, "議事録", CancellationToken.None);

        var loaded = await service.GetAsync(meeting.Id, "owner-a", CancellationToken.None);

        Assert.Equal("手で書いた原文", loaded!.Transcription);
        Assert.Equal("議事録", loaded.Minutes);
        Assert.Equal(GenerationStatus.Succeeded, loaded.GenerationStatus);
    }

    [Fact]
    public async Task MarkSucceededAsyncは値を渡せば文字起こしを書く()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "新規" }, CancellationToken.None);

        await service.MarkSucceededAsync(meeting.Id, "起こした原文", "translated", "議事録", CancellationToken.None);

        var loaded = await service.GetAsync(meeting.Id, "owner-a", CancellationToken.None);

        Assert.Equal("起こした原文", loaded!.Transcription);
        Assert.Equal("translated", loaded.TranslatedTranscription);
        Assert.Equal("議事録", loaded.Minutes);
        Assert.Equal(GenerationStatus.Succeeded, loaded.GenerationStatus);
    }

    [Fact]
    public async Task 実行中の会議はTryMarkQueuedAsyncがfalseを返す()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "実行中" }, CancellationToken.None);
        Assert.True(await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None));
        await service.MarkRunningAsync(meeting.Id, CancellationToken.None);

        Assert.False(await service.TryMarkQueuedAsync(meeting.Id, "owner-a", CancellationToken.None));
    }

    [Fact]
    public async Task 同時に2回TryMarkQueuedAsyncを呼んでも成功は1回だけ()
    {
        var (service, scope) = await CreateAsync(_factory);
        var meeting = await service.CreateAsync("owner-race", new Meeting { Title = "同時" }, CancellationToken.None);
        scope.Dispose();

        var scope1 = _factory.CreateScope();
        var scope2 = _factory.CreateScope();
        try
        {
            var service1 = scope1.ServiceProvider.GetRequiredService<IMeetingService>();
            var service2 = scope2.ServiceProvider.GetRequiredService<IMeetingService>();

            var results = await Task.WhenAll(
                service1.TryMarkQueuedAsync(meeting.Id, "owner-race", CancellationToken.None),
                service2.TryMarkQueuedAsync(meeting.Id, "owner-race", CancellationToken.None));

            Assert.Single(results.Where(success => success).ToList());
        }
        finally
        {
            scope1.Dispose();
            scope2.Dispose();
        }
    }

    [Fact]
    public async Task 中断されたジョブは起動時にFailedへ倒す()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-reset", new Meeting { Title = "中断" }, CancellationToken.None);
        await service.TryMarkQueuedAsync(meeting.Id, "owner-reset", CancellationToken.None);

        var reset = await service.ResetInterruptedJobsAsync(CancellationToken.None);
        var loaded = await service.GetAsync(meeting.Id, "owner-reset", CancellationToken.None);

        Assert.True(reset >= 1);
        Assert.Equal(GenerationStatus.Failed, loaded!.GenerationStatus);
        Assert.Equal("サーバ再起動により中断されました。", loaded.GenerationError);
    }

    [Fact]
    public async Task 添付の追加と削除は所有者チェックを通る()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;

        var meeting = await service.CreateAsync("owner-a", new Meeting { Title = "添付" }, CancellationToken.None);
        var file = await service.AddFileAsync(meeting.Id, "owner-a", new MeetingFile
        {
            Kind = MeetingFileKind.Media,
            OriginalFileName = "a.mp3",
            Extension = ".mp3",
            ContentType = "audio/mpeg",
            SizeBytes = 100
        }, CancellationToken.None);

        Assert.NotNull(file);
        Assert.Null(await service.GetFileAsync(meeting.Id, "owner-b", file!.Id, CancellationToken.None));
        Assert.False(await service.RemoveFileAsync(meeting.Id, "owner-b", file.Id, CancellationToken.None));
        Assert.True(await service.RemoveFileAsync(meeting.Id, "owner-a", file.Id, CancellationToken.None));
    }

    [Fact]
    public async Task テンプレートの保存は自分の会議にだけ効く()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        var meeting = await service.CreateAsync("tpl-owner", new Meeting { Title = "会議" }, CancellationToken.None);
        var template = await templates.GetDefaultAsync("tpl-owner", CancellationToken.None);
        var others = await templates.GetDefaultAsync("tpl-other", CancellationToken.None);

        // 自分のテンプレートを指していても、他人の会議には効かない
        Assert.Equal(
            SetTemplateResult.MeetingNotFound,
            await service.SetMinutesTemplateAsync(meeting.Id, "tpl-other", others.Id, CancellationToken.None));
        Assert.Equal(
            SetTemplateResult.Updated,
            await service.SetMinutesTemplateAsync(meeting.Id, "tpl-owner", template.Id, CancellationToken.None));

        var loaded = await service.GetAsync(meeting.Id, "tpl-owner", CancellationToken.None);
        Assert.Equal(template.Id, loaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 消えた会議へのテンプレート指定は失敗する()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        var template = await templates.GetDefaultAsync("vanished-owner", CancellationToken.None);

        Assert.Equal(
            SetTemplateResult.MeetingNotFound,
            await service.SetMinutesTemplateAsync(
                Guid.NewGuid(), "vanished-owner", template.Id, CancellationToken.None));
    }

    [Fact]
    public async Task 更新で他人のテンプレートを指すと保存しない()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        var meeting = await service.CreateAsync("edit-tpl", new Meeting { Title = "会議" }, CancellationToken.None);
        var mine = await templates.GetDefaultAsync("edit-tpl", CancellationToken.None);
        var others = await templates.GetDefaultAsync("edit-tpl-other", CancellationToken.None);

        Assert.True(await service.UpdateAsync(meeting.Id, "edit-tpl", Edit(mine.Id), CancellationToken.None));
        Assert.False(await service.UpdateAsync(meeting.Id, "edit-tpl", Edit(others.Id), CancellationToken.None));

        var loaded = await service.GetAsync(meeting.Id, "edit-tpl", CancellationToken.None);
        Assert.Equal(mine.Id, loaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 更新でテンプレートを省略すると今の選択を保つ()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        var meeting = await service.CreateAsync("keep-tpl", new Meeting { Title = "会議" }, CancellationToken.None);
        var mine = await templates.GetDefaultAsync("keep-tpl", CancellationToken.None);
        await service.UpdateAsync(meeting.Id, "keep-tpl", Edit(mine.Id), CancellationToken.None);

        // 項目を送らない保存（テンプレートを知らない既存の呼び出し）では、選択を消さない
        Assert.True(await service.UpdateAsync(meeting.Id, "keep-tpl", Edit(null), CancellationToken.None));

        var loaded = await service.GetAsync(meeting.Id, "keep-tpl", CancellationToken.None);
        Assert.Equal(mine.Id, loaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task テンプレートの保存で他人のテンプレートは指せない()
    {
        var (service, scope) = await CreateAsync(_factory);
        using var _ = scope;
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        var meeting = await service.CreateAsync("set-tpl", new Meeting { Title = "会議" }, CancellationToken.None);
        var mine = await templates.GetDefaultAsync("set-tpl", CancellationToken.None);
        var others = await templates.GetDefaultAsync("set-tpl-other", CancellationToken.None);

        Assert.Equal(
            SetTemplateResult.Updated,
            await service.SetMinutesTemplateAsync(meeting.Id, "set-tpl", mine.Id, CancellationToken.None));
        // 会議は自分のものなので、断るのはテンプレートのほうだと呼び出し元に伝わる
        Assert.Equal(
            SetTemplateResult.TemplateNotAllowed,
            await service.SetMinutesTemplateAsync(meeting.Id, "set-tpl", others.Id, CancellationToken.None));

        var loaded = await service.GetAsync(meeting.Id, "set-tpl", CancellationToken.None);
        Assert.Equal(mine.Id, loaded!.MinutesTemplateId);
    }

    private static MeetingEdit Edit(Guid? templateId) =>
        new("会議", null, true, false, "ja", "", "", "", templateId);
}
