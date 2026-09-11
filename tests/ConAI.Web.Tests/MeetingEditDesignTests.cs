using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingEditDesignTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingEditDesignTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 編集画面が契約を保ったまま新しい部品クラスになる()
    {
        Meeting meeting;
        Guid fileId;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            meeting = await service.CreateAsync("design-edit", new Meeting
            {
                Title = "編集の見た目",
                LiveMode = true,
                TranslateMode = true
            }, CancellationToken.None);

            var file = await service.AddFileAsync(meeting.Id, "design-edit", new MeetingFile
            {
                Kind = MeetingFileKind.Media,
                OriginalFileName = "a.mp3",
                Extension = ".mp3",
                ContentType = "audio/mpeg",
                SizeBytes = 2048
            }, CancellationToken.None);
            Assert.NotNull(file);
            fileId = file.Id;
        }

        var client = _factory.CreateClientAs("design-edit");
        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        // JavaScript と E2E が掴む契約
        Assert.Contains("id=\"conai-meeting\"", html);
        Assert.Contains($"data-meeting-id=\"{meeting.Id}\"", html);
        Assert.Contains("data-worklet-path=\"/js/pcm-processor.js\"", html);
        // 添付は音声・動画と参考資料の 2 つの欄に分かれる（RV 2）。accept はサーバが AllowedFileTypes から作る
        Assert.Contains("id=\"media-input\"", html);
        Assert.Contains("accept=\".mp3,.m4a,.aac,.flac,.ogg,.wav,.mp4,.mov,.webm\"", html);
        Assert.Contains("id=\"media-error\"", html);
        Assert.Contains("id=\"media-list\"", html);
        Assert.Contains("id=\"reference-input\"", html);
        Assert.Contains("accept=\".pdf,.txt,.md,.docx,.pptx,.xlsx\"", html);
        Assert.Contains("id=\"reference-error\"", html);
        Assert.Contains("id=\"reference-list\"", html);
        Assert.DoesNotContain("id=\"file-input\"", html);
        Assert.DoesNotContain("id=\"file-error\"", html);
        Assert.DoesNotContain("id=\"file-list\"", html);
        Assert.Contains($"data-file-id=\"{fileId}\"", html);
        Assert.Contains("file-delete", html);
        Assert.Contains("id=\"record-button\"", html);
        Assert.Contains("id=\"live-status\"", html);
        // 録音に使うマイクの選択。Windows の既定がステレオ ミキサーだと声が入らないため、利用者が選ぶ
        Assert.Contains("id=\"mic-device\"", html);
        Assert.Contains("id=\"mic-device-note\"", html);
        // live の文字は保存欄（#transcription）へ直接追記する。録音カードの別枠（#live-transcript）は持たない
        Assert.DoesNotContain("id=\"live-transcript\"", html);
        Assert.DoesNotContain("id=\"live-translation\"", html);
        Assert.DoesNotContain("文字起こし（原文）", html);
        Assert.Contains("id=\"transcription\"", html);
        Assert.Contains("id=\"translated-transcription\"", html);
        // 見出しと重なるラベルは出さず、aria-label で読み上げ名だけ残す（2026-08-29 の指摘 RV 1）
        Assert.DoesNotContain("for=\"Transcript_Transcription\"", html);
        Assert.Contains("aria-label=\"文字起こし\"", html);
        Assert.Contains("for=\"Transcript_TranslatedTranscription\"", html);
        // 入力元のモードはラジオ 2 つで切り替える（RV 2）
        Assert.Contains("id=\"Basic_LiveMode_live\"", html);
        Assert.Contains("id=\"Basic_LiveMode_file\"", html);
        Assert.Contains("id=\"target-language-field\"", html);
        // 保存は画面の上部にある 1 つのボタンで行い、欄ごとの保存を持たない（RV 3）
        Assert.Contains("id=\"meeting-form\"", html);
        Assert.Contains("id=\"save-button\"", html);
        Assert.DoesNotContain("id=\"transcript-save\"", html);
        Assert.Contains("id=\"generate-button\"", html);
        Assert.Contains("id=\"generation-status\"", html);
        Assert.Contains("id=\"minutes\"", html);
        Assert.DoesNotContain("id=\"minutes-save\"", html);
        Assert.DoesNotContain("id=\"minutes-save-status\"", html);
        // 議事録のプレビュー / 編集トグル（minutes-editor.js が掴む契約）
        Assert.Contains("id=\"minutes-block\"", html);
        Assert.Contains("id=\"minutes-preview-tab\"", html);
        Assert.Contains("id=\"minutes-edit-tab\"", html);
        Assert.Contains("id=\"minutes-preview\"", html);
        Assert.Contains("id=\"minutes-edit\"", html);
        Assert.DoesNotContain("id=\"minutes-label\"", html);
        Assert.DoesNotContain("for=\"minutes\"", html);
        Assert.Contains("aria-label=\"議事録の表示切替\"", html);
        Assert.Contains("aria-label=\"議事録（Markdown）\"", html);

        // 新しい部品クラス
        Assert.Contains("btn-record", html);
        Assert.Contains("input-field", html);
        Assert.Contains("card-title", html);

        // Bootstrap の残骸
        Assert.DoesNotContain("card-header", html);
        Assert.DoesNotContain("card-body", html);
        Assert.DoesNotContain("form-control", html);
        Assert.DoesNotContain("form-select", html);
        Assert.DoesNotContain("d-none", html);
        Assert.DoesNotContain("d-flex", html);
        Assert.DoesNotContain("btn-outline-", html);
    }

    [Fact]
    public async Task 音声動画の表が種別ではなく渡す順の連番を出す()
    {
        Guid meetingId;
        Guid earlierId;
        Guid laterId;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync("design-media-no", new Meeting
            {
                Title = "分割した音声"
            }, CancellationToken.None);
            meetingId = meeting.Id;

            var later = await service.AddFileAsync(meetingId, "design-media-no", new MeetingFile
            {
                Kind = MeetingFileKind.Media,
                OriginalFileName = "会議-2.mp3",
                Extension = ".mp3",
                ContentType = "audio/mpeg",
                SizeBytes = 2048
            }, CancellationToken.None);
            var earlier = await service.AddFileAsync(meetingId, "design-media-no", new MeetingFile
            {
                Kind = MeetingFileKind.Recording,
                OriginalFileName = "会議-1.webm",
                Extension = ".webm",
                ContentType = "audio/webm",
                SizeBytes = 1024
            }, CancellationToken.None);
            Assert.NotNull(later);
            Assert.NotNull(earlier);
            laterId = later.Id;
            earlierId = earlier.Id;

            // 連番は追加した順ではなく CreatedAt の順で振る。生成が Gemini へ渡す順と同じであることを示すため
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.MeetingFiles.Where(f => f.MeetingId == meetingId).ToListAsync();
            stored.Single(f => f.Id == earlierId).CreatedAt = new DateTime(2026, 8, 31, 1, 0, 0, DateTimeKind.Utc);
            stored.Single(f => f.Id == laterId).CreatedAt = new DateTime(2026, 8, 31, 2, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClientAs("design-media-no");
        var html = await client.GetStringAsync($"/Meetings/Edit/{meetingId}");

        var mediaTable = html[html.IndexOf("id=\"media-list\"", StringComparison.Ordinal)..];
        mediaTable = mediaTable[..mediaTable.IndexOf("</tbody>", StringComparison.Ordinal)];

        Assert.Contains("<th scope=\"col\">No</th>", html);
        Assert.DoesNotContain("<th scope=\"col\">種別</th>", html);
        // 録音か素材かは表に出さない。ファイル名で判別できるため
        Assert.DoesNotContain("素材", mediaTable);
        Assert.DoesNotContain("録音", mediaTable);
        Assert.Matches($"data-file-id=\"{earlierId}\">\\s*<td>1</td>", mediaTable);
        Assert.Matches($"data-file-id=\"{laterId}\">\\s*<td>2</td>", mediaTable);
    }

    [Fact]
    public async Task 閲覧画面が議事録を専用のクラスで包む()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync("design-details", new Meeting { Title = "閲覧の見た目" }, CancellationToken.None);
            await service.MarkSucceededAsync(meeting.Id, "本文です", string.Empty, "## 決定事項\n\n- 次回は来週", CancellationToken.None);
            id = meeting.Id;
        }

        var client = _factory.CreateClientAs("design-details");
        var html = await client.GetStringAsync($"/Meetings/Details/{id}");

        Assert.Contains("conai-prose", html);
        // カードの見出しも <h2> になったため、Markdown 由来の見出しは要素ごと突き合わせる
        Assert.Contains("<h2>決定事項</h2>", html);
        Assert.Contains("conai-live-box", html);
        Assert.DoesNotContain("card-header", html);
        Assert.DoesNotContain("card-body", html);
        Assert.DoesNotContain("d-flex", html);
        Assert.DoesNotContain("btn-outline-", html);
    }
}
