using System.Text;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MinutesGenerationServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MinutesGenerationServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Guid> CreateAsync(
        string ownerId,
        Action<Meeting> configure,
        params (MeetingFileKind Kind, string FileName, string Extension, string ContentType, byte[] Bytes)[] files)
    {
        using var scope = _factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();

        var meeting = new Meeting { Title = "生成テスト" };
        configure(meeting);
        var created = await meetings.CreateAsync(ownerId, meeting, CancellationToken.None);

        foreach (var file in files)
        {
            var entity = new MeetingFile
            {
                Kind = file.Kind,
                OriginalFileName = file.FileName,
                Extension = file.Extension,
                ContentType = file.ContentType,
                SizeBytes = file.Bytes.Length
            };

            var saved = await meetings.AddFileAsync(created.Id, ownerId, entity, CancellationToken.None);
            await using var content = new MemoryStream(file.Bytes);
            await storage.SaveAsync(created.Id, saved!.Id, file.Extension, content, CancellationToken.None);
        }

        return created.Id;
    }

    private FakeGeminiContentClient Fake => _factory.Services.GetRequiredService<FakeGeminiContentClient>();

    [Fact]
    public async Task 音声だけなら文字起こしから作るプロンプトになる()
    {
        var id = await CreateAsync(
            "gen-a",
            _ => { },
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        var result = await service.GenerateAsync(id, CancellationToken.None);

        Assert.Equal(FakeGeminiContentClient.DefaultTranscription, result.Result.Transcription);
        Assert.Contains("添付された音声または動画", Fake.LastRequest!.Prompt);
        Assert.Single(Fake.LastRequest.Files);
        Assert.Equal("audio/mpeg", Fake.LastRequest.Files[0].MimeType);
    }

    [Fact]
    public async Task 文字起こしがあるならファイルを添えずに議事録だけ作る()
    {
        var id = await CreateAsync(
            "gen-b",
            meeting => meeting.Transcription = "話者A: すでに文字起こし済みです。",
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        await service.GenerateAsync(id, CancellationToken.None);

        Assert.Contains("すでに文字起こし済みです。", Fake.LastRequest!.Prompt);
        Assert.Empty(Fake.LastRequest.Files);
    }

    [Fact]
    public async Task 文字起こしが既にある会議の生成はスキーマから文字起こしを外す()
    {
        var id = await CreateAsync(
            "gen-e",
            meeting => meeting.Transcription = "話者A: すでに文字起こし済みです。",
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        await service.GenerateAsync(id, CancellationToken.None);

        Assert.False(Fake.LastRequest!.IncludeTranscription);
    }

    [Fact]
    public async Task テキストの参考資料は本文に差し込まれる()
    {
        var id = await CreateAsync(
            "gen-c",
            _ => { },
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]),
            (MeetingFileKind.Reference, "議題.md", ".md", "text/markdown", Encoding.UTF8.GetBytes("# 議題\n- 予算の確認")));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        await service.GenerateAsync(id, CancellationToken.None);

        Assert.Contains("予算の確認", Fake.LastRequest!.Prompt);
        Assert.Single(Fake.LastRequest.Files);
    }

    [Fact]
    public async Task 翻訳有効なら翻訳付きで依頼する()
    {
        var id = await CreateAsync(
            "gen-d",
            meeting =>
            {
                meeting.TranslateMode = true;
                meeting.TargetLanguage = "en";
            },
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        var result = await service.GenerateAsync(id, CancellationToken.None);

        Assert.True(Fake.LastRequest!.IncludeTranslatedTranscription);
        Assert.Contains("English", Fake.LastRequest.SystemInstruction);
        Assert.Equal(FakeGeminiContentClient.DefaultTranslatedTranscription, result.Result.TranslatedTranscription);
    }

    [Fact]
    public async Task テンプレートを指していない会議は既定の本文で依頼する()
    {
        var id = await CreateAsync(
            "gen-t1",
            _ => { },
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();

        await service.GenerateAsync(id, CancellationToken.None);

        var standard = await templates.GetDefaultAsync("gen-t1", CancellationToken.None);
        Assert.Equal("標準", standard.Name);
        Assert.Contains("## 議題ごとの要点", Fake.LastRequest!.Prompt);
    }

    [Fact]
    public async Task 指しているテンプレートの本文で依頼する()
    {
        Guid templateId;
        using (var setup = _factory.CreateScope())
        {
            var templates = setup.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            var created = await templates.CreateAsync(
                "gen-t2", "要点だけ", "## 要点だけ\n- 要点\n", CancellationToken.None);
            templateId = created!.Id;
        }

        var id = await CreateAsync(
            "gen-t2",
            meeting => meeting.MinutesTemplateId = templateId,
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        await service.GenerateAsync(id, CancellationToken.None);

        Assert.Contains("## 要点だけ", Fake.LastRequest!.Prompt);
        Assert.DoesNotContain("## 議題ごとの要点", Fake.LastRequest.Prompt);
    }

    [Fact]
    public async Task 他人のテンプレートを指していれば既定の本文に落ちる()
    {
        Guid otherId;
        using (var setup = _factory.CreateScope())
        {
            var templates = setup.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            var created = await templates.CreateAsync(
                "gen-t3-other", "他人のもの", "## 他人のもの\n- 行\n", CancellationToken.None);
            otherId = created!.Id;
        }

        var id = await CreateAsync(
            "gen-t3",
            meeting => meeting.MinutesTemplateId = otherId,
            (MeetingFileKind.Media, "会議.mp3", ".mp3", "audio/mpeg", [1, 2, 3, 4]));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMinutesGenerationService>();

        await service.GenerateAsync(id, CancellationToken.None);

        Assert.DoesNotContain("## 他人のもの", Fake.LastRequest!.Prompt);
        Assert.Contains("## 議題ごとの要点", Fake.LastRequest.Prompt);
    }
}
