using System.Net;
using System.Text.RegularExpressions;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingEditPageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingEditPageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateMeetingAsync(string ownerId, Action<Meeting>? configure = null)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = new Meeting { Title = "編集対象" };
        configure?.Invoke(meeting);
        return await service.CreateAsync(ownerId, meeting, CancellationToken.None);
    }

    [Fact]
    public async Task 所有者は編集ページを開ける()
    {
        var meeting = await CreateMeetingAsync("edit-a");
        var client = _factory.CreateClientAs("edit-a");

        // 動的出力の日本語は数値文字参照で出るため、デコードしてから比較する
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        Assert.Contains("編集対象", html);
        Assert.Contains($"data-meeting-id=\"{meeting.Id}\"", html);
        Assert.Contains("<meta name=\"csrf\"", html);
        // 保存は画面の上部にある 1 つのボタンで行う（RV 3）
        Assert.Contains("id=\"meeting-form\"", html);
        Assert.Contains("id=\"save-button\"", html);
    }

    [Fact]
    public async Task 他人の編集ページは404になる()
    {
        var meeting = await CreateMeetingAsync("edit-a");
        var client = _factory.CreateClientAs("edit-b");

        var response = await client.GetAsync($"/Meetings/Edit/{meeting.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 保存で基本情報と文字起こしと議事録をまとめて更新できる()
    {
        var meeting = await CreateMeetingAsync("edit-save");
        var client = _factory.CreateClientAs("edit-save");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        // handler 無しの POST 1 本で、画面の全欄を保存する（RV 3）
        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "名前を変えた",
                ["Basic.HeldAt"] = "2026/08/28 10:11:12",
                ["Basic.LiveMode"] = "false",
                ["Basic.TranslateMode"] = "true",
                ["Basic.TargetLanguage"] = "en",
                ["Transcript.Transcription"] = "直した本文",
                ["Transcript.TranslatedTranscription"] = "fixed body",
                ["Minutes"] = "# 決定事項",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save", CancellationToken.None);
        Assert.Equal("名前を変えた", loaded!.Title);
        Assert.Equal(new DateTime(2026, 8, 28, 10, 11, 12), loaded.HeldAt);
        Assert.False(loaded.LiveMode);
        Assert.True(loaded.TranslateMode);
        Assert.Equal("en", loaded.TargetLanguage);
        Assert.Equal("直した本文", loaded.Transcription);
        Assert.Equal("fixed body", loaded.TranslatedTranscription);
        Assert.Equal("# 決定事項", loaded.Minutes);

        // 保存後の再表示では「保存しました。」を 1 回だけ出す
        var shown = await client.GetAsync(response.Headers.Location);
        Assert.Contains("保存しました。", WebUtility.HtmlDecode(await shown.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task 文字起こしと議事録が空でも保存できる()
    {
        var meeting = await CreateMeetingAsync("edit-save-empty");
        var client = _factory.CreateClientAs("edit-save-empty");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        // 会議を作った直後は文字起こしも議事録も空。空の textarea は空文字として送られる
        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "空のまま保存",
                ["Basic.LiveMode"] = "true",
                ["Basic.TranslateMode"] = "false",
                ["Basic.TargetLanguage"] = SupportedLanguages.Default,
                ["Transcript.Transcription"] = "",
                ["Transcript.TranslatedTranscription"] = "",
                ["Minutes"] = "",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-empty", CancellationToken.None);
        Assert.Equal("空のまま保存", loaded!.Title);
        Assert.True(loaded.LiveMode);
        Assert.Equal(string.Empty, loaded.Transcription);
        Assert.Equal(string.Empty, loaded.TranslatedTranscription);
        Assert.Equal(string.Empty, loaded.Minutes);
    }

    [Fact]
    public async Task 他人の会議は保存できない()
    {
        var meeting = await CreateMeetingAsync("edit-save-a");
        var client = _factory.CreateClientAs("edit-save-b");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "乗っ取り",
                ["Basic.TargetLanguage"] = "ja",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-a", CancellationToken.None);
        Assert.Equal("編集対象", loaded!.Title);
    }

    [Fact]
    public async Task 長すぎる文字起こしは保存しない()
    {
        var meeting = await CreateMeetingAsync("edit-save-long");
        var client = _factory.CreateClientAs("edit-save-long");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.TargetLanguage"] = "ja",
                ["Transcript.Transcription"] = new string('あ', MeetingLimits.MaxTextChars + 1),
                ["__RequestVerificationToken"] = token
            }));

        // 上限値は日本語 1 文字がフォーム値の上限（既定 4 MiB）に先に当たらない値にしてあるため、
        // 多バイト文字のまま StringLength の境界（ちょうど 1 文字だけ超える）を直接試せる。
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("文字起こしが長すぎます。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-long", CancellationToken.None);
        Assert.Equal(string.Empty, loaded!.Transcription);
    }

    [Fact]
    public async Task 長すぎる議事録は保存しない()
    {
        var meeting = await CreateMeetingAsync("edit-save-long-minutes");
        var client = _factory.CreateClientAs("edit-save-long-minutes");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.TargetLanguage"] = "ja",
                ["Minutes"] = new string('あ', MeetingLimits.MaxTextChars + 1),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("議事録が長すぎます。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-long-minutes", CancellationToken.None);
        Assert.Equal(string.Empty, loaded!.Minutes);
    }

    [Fact]
    public async Task 上限ちょうどの文字起こしは保存できる()
    {
        var meeting = await CreateMeetingAsync("edit-save-exact");
        var client = _factory.CreateClientAs("edit-save-exact");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.TargetLanguage"] = "ja",
                ["Transcript.Transcription"] = new string('あ', MeetingLimits.MaxTextChars),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-exact", CancellationToken.None);
        Assert.Equal(new string('あ', MeetingLimits.MaxTextChars), loaded!.Transcription);
    }

    [Fact]
    public async Task 保存済みの長すぎる文字起こしは変えないなら保存できる()
    {
        // 録音の追記には上限が無いため、保存済みの文字起こしが上限を超えることがある。
        // その会議で会議名だけ直せなくなると、画面から何も直せない会議ができてしまう。
        var stored = new string('あ', MeetingLimits.MaxTextChars + 1);
        var meeting = await CreateMeetingAsync("edit-save-stored-long", m => m.Transcription = stored);
        var client = _factory.CreateClientAs("edit-save-stored-long");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "会議名だけ直す",
                ["Basic.TargetLanguage"] = "ja",
                ["Transcript.Transcription"] = stored,
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-stored-long", CancellationToken.None);
        Assert.Equal("会議名だけ直す", loaded!.Title);
        Assert.Equal(stored, loaded.Transcription);
    }

    [Fact]
    public async Task 保存済みが長すぎても書き足した文字起こしは保存しない()
    {
        var stored = new string('あ', MeetingLimits.MaxTextChars + 1);
        var meeting = await CreateMeetingAsync("edit-save-stored-long-b", m => m.Transcription = stored);
        var client = _factory.CreateClientAs("edit-save-stored-long-b");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.TargetLanguage"] = "ja",
                ["Transcript.Transcription"] = stored + "い",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("文字起こしが長すぎます。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-stored-long-b", CancellationToken.None);
        Assert.Equal(stored, loaded!.Transcription);
    }

    [Fact]
    public async Task 対応していない言語では保存しない()
    {
        var meeting = await CreateMeetingAsync("edit-save-lang");
        var client = _factory.CreateClientAs("edit-save-lang");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.TargetLanguage"] = "xx",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("対応していない言語です。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-save-lang", CancellationToken.None);
        Assert.Equal(SupportedLanguages.Default, loaded!.TargetLanguage);
    }

    [Fact]
    public async Task Liveモードの会議はLive側のラジオにチェックが付く()
    {
        var meeting = await CreateMeetingAsync("edit-radio-live", m => m.LiveMode = true);
        var client = _factory.CreateClientAs("edit-radio-live");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        // InputTagHelper は値の一致する側の input に checked を付ける（RV 2）
        var live = Regex.Match(html, """<input[^>]*id="Basic_LiveMode_live"[^>]*>""").Value;
        var file = Regex.Match(html, """<input[^>]*id="Basic_LiveMode_file"[^>]*>""").Value;
        Assert.NotEmpty(live);
        Assert.Contains("checked", live);
        Assert.DoesNotContain("checked", file);
    }

    [Fact]
    public async Task ファイルモードの会議はファイル側のラジオにチェックが付く()
    {
        var meeting = await CreateMeetingAsync("edit-radio-file", m => m.LiveMode = false);
        var client = _factory.CreateClientAs("edit-radio-file");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        var live = Regex.Match(html, """<input[^>]*id="Basic_LiveMode_live"[^>]*>""").Value;
        var file = Regex.Match(html, """<input[^>]*id="Basic_LiveMode_file"[^>]*>""").Value;
        Assert.NotEmpty(file);
        Assert.Contains("checked", file);
        Assert.DoesNotContain("checked", live);
    }

    [Fact]
    public async Task 翻訳モードが無い会議は翻訳先言語の欄を隠す()
    {
        var meeting = await CreateMeetingAsync("edit-lang-off", m => m.TranslateMode = false);
        var client = _factory.CreateClientAs("edit-lang-off");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        var field = Regex.Match(html, """<div[^>]*id="target-language-field"[^>]*>""").Value;
        Assert.NotEmpty(field);
        Assert.Contains("hidden=\"hidden\"", field);
    }

    [Fact]
    public async Task 翻訳モードの会議は翻訳先言語の欄を隠さない()
    {
        var meeting = await CreateMeetingAsync("edit-lang-on", m => m.TranslateMode = true);
        var client = _factory.CreateClientAs("edit-lang-on");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        var field = Regex.Match(html, """<div[^>]*id="target-language-field"[^>]*>""").Value;
        Assert.NotEmpty(field);
        Assert.DoesNotContain("hidden", field);
    }

    [Fact]
    public async Task Liveモードで素材が無ければ生成ボタンを非活性にする()
    {
        var meeting = await CreateMeetingAsync("edit-gen1", m => m.LiveMode = true);
        var client = _factory.CreateClientAs("edit-gen1");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        Assert.Contains("録音するか、音声・動画を追加してください", html);
    }

    [Fact]
    public async Task Liveオフで素材が無ければ添付を促す()
    {
        var meeting = await CreateMeetingAsync("edit-gen2", m => m.LiveMode = false);
        var client = _factory.CreateClientAs("edit-gen2");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        Assert.Contains("音声・動画を追加してください", html);
    }

    [Fact]
    public async Task 編集画面が_meeting_js_を読み込む()
    {
        var meeting = await CreateMeetingAsync("edit-js");
        using var client = _factory.CreateClientAs("edit-js");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        // asp-append-version により /js/meeting.<hash>.js の形で出力される
        Assert.Matches("""src="/js/meeting\.[a-z0-9]+\.js""", html);
        Assert.Contains("type=\"module\"", html);
    }

    [Fact]
    public async Task 編集画面がワークレットのパスと生成状態を渡す()
    {
        var meeting = await CreateMeetingAsync("edit-worklet");
        using var client = _factory.CreateClientAs("edit-worklet");

        var html = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        Assert.Contains("data-worklet-path=\"/js/pcm-processor.js\"", html);
        Assert.Contains("data-generation-status=\"None\"", html);
    }

    [Fact]
    public async Task メディア添付があれば生成ボタンが活性になる()
    {
        var meeting = await CreateMeetingAsync("edit-gen3", m => m.LiveMode = false);
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await service.AddFileAsync(meeting.Id, "edit-gen3", new MeetingFile
            {
                Kind = MeetingFileKind.Media,
                OriginalFileName = "a.mp3",
                Extension = ".mp3",
                ContentType = "audio/mpeg",
                SizeBytes = 100
            }, CancellationToken.None);
        }

        var client = _factory.CreateClientAs("edit-gen3");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        Assert.Contains("id=\"generate-button\"", html);
        Assert.DoesNotContain("音声・動画を追加してください", html);
    }

    private async Task<Guid> TemplateIdAsync(string ownerId, string name)
    {
        using var scope = _factory.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
        var list = await templates.ListAsync(ownerId, CancellationToken.None);
        return list.First(t => t.Name == name).Id;
    }

    [Fact]
    public async Task 編集ページのテンプレートのセレクトボックスは会議のテンプレートを選ぶ()
    {
        var conciseId = await TemplateIdAsync("edit-tpl-show", "簡潔");
        var meeting = await CreateMeetingAsync("edit-tpl-show", m => m.MinutesTemplateId = conciseId);
        var client = _factory.CreateClientAs("edit-tpl-show");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var select = Regex.Match(html, """<select[^>]*id="minutes-template".*?</select>""", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.Contains("name=\"Basic.MinutesTemplateId\"", select);
        Assert.Contains($"""<option value="{conciseId}" selected="selected">簡潔</option>""", select);
    }

    [Fact]
    public async Task テンプレートを指していない会議は既定が選ばれた状態で開く()
    {
        // 移行前に作った会議と、選んだテンプレートが消された会議がこの状態になる
        var meeting = await CreateMeetingAsync("edit-tpl-none");
        Assert.Null(meeting.MinutesTemplateId);
        var standardId = await TemplateIdAsync("edit-tpl-none", "標準");
        var client = _factory.CreateClientAs("edit-tpl-none");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var select = Regex.Match(html, """<select[^>]*id="minutes-template".*?</select>""", RegexOptions.Singleline).Value;
        Assert.Contains($"""<option value="{standardId}" selected="selected">標準</option>""", select);
    }

    [Fact]
    public async Task 保存でテンプレートを変えられる()
    {
        var meeting = await CreateMeetingAsync("edit-tpl-save");
        var conciseId = await TemplateIdAsync("edit-tpl-save", "簡潔");
        var client = _factory.CreateClientAs("edit-tpl-save");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.LiveMode"] = "true",
                ["Basic.TranslateMode"] = "false",
                ["Basic.TargetLanguage"] = "ja",
                ["Basic.MinutesTemplateId"] = conciseId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-tpl-save", CancellationToken.None);
        Assert.Equal(conciseId, loaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 他人のテンプレートを指す保存は再表示になる()
    {
        var meeting = await CreateMeetingAsync("edit-tpl-deny");
        var otherId = await TemplateIdAsync("edit-tpl-deny-other", "標準");
        var client = _factory.CreateClientAs("edit-tpl-deny");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Edit/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Basic.Title"] = "編集対象",
                ["Basic.LiveMode"] = "true",
                ["Basic.TranslateMode"] = "false",
                ["Basic.TargetLanguage"] = "ja",
                ["Basic.MinutesTemplateId"] = otherId.ToString(),
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("選べないテンプレートです。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "edit-tpl-deny", CancellationToken.None);
        Assert.Null(loaded!.MinutesTemplateId);
    }
}
