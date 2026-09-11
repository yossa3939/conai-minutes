using System.Net;
using System.Text;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingMinutesDownloadTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string Minutes = "# 議事録\n\n## 決定事項\n\n- 予算を**承認**\n\n| 項目 | 担当 |\n|---|---|\n| 報告書 | 田中 |";

    private readonly ConAIWebApplicationFactory _factory;

    public MeetingMinutesDownloadTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId, string? minutes = Minutes)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await service.CreateAsync(ownerId, new Meeting { Title = "予算会議" }, CancellationToken.None);
        if (minutes is not null)
        {
            await service.MarkSucceededAsync(meeting.Id, "本文", string.Empty, minutes, CancellationToken.None);
        }

        return meeting;
    }

    [Fact]
    public async Task Markdownをそのままattachmentで返す()
    {
        var meeting = await CreateAsync("minutes-md");
        var client = _factory.CreateClientAs("minutes-md");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=md");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("予算会議.md", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(Minutes, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task 形式を省略するとMarkdownになる()
    {
        var meeting = await CreateAsync("minutes-default");
        var client = _factory.CreateClientAs("minutes-default");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/markdown", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task txtは記号を落とした平文で返す()
    {
        var meeting = await CreateAsync("minutes-txt");
        var client = _factory.CreateClientAs("minutes-txt");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=txt");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("予算会議.txt", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.DoesNotContain("#", body);
        Assert.DoesNotContain("**", body);
        Assert.DoesNotContain("|", body);
        Assert.Contains("議事録\n決定事項\n予算を承認\n", body);
        Assert.Contains("項目\t担当\n報告書\t田中\n", body);
    }

    [Fact]
    public async Task 会議名のファイル名に使えない文字は置き換える()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync("minutes-name", new Meeting { Title = "8/28 定例: 予算" }, CancellationToken.None);
            await service.MarkSucceededAsync(meeting.Id, "本文", string.Empty, Minutes, CancellationToken.None);
            id = meeting.Id;
        }

        var client = _factory.CreateClientAs("minutes-name");

        var response = await client.GetAsync($"/api/meetings/{id}/minutes?format=txt");

        Assert.Equal("8_28 定例_ 予算.txt", response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    /// <summary>その会議名でダウンロードしたときの、Content-Disposition のファイル名。</summary>
    private async Task<string> FileNameAsync(string ownerId, string title)
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync(ownerId, new Meeting { Title = title }, CancellationToken.None);
            await service.MarkSucceededAsync(meeting.Id, "本文", string.Empty, Minutes, CancellationToken.None);
            id = meeting.Id;
        }

        var client = _factory.CreateClientAs(ownerId);
        var response = await client.GetAsync($"/api/meetings/{id}/minutes?format=md");
        return response.Content.Headers.ContentDisposition!.FileNameStar!;
    }

    [Fact]
    public async Task 会議名の末尾の点と空白は落とす()
    {
        // Windows は末尾の点と空白を落として保存するため、そのままだと拡張子の前に点が並ぶ
        Assert.Equal("定例会議.md", await FileNameAsync("minutes-dot", "定例会議. "));
    }

    [Fact]
    public async Task 装置名と同じ会議名は避ける()
    {
        // CON.md や nul.md は Windows が装置として扱い、保存できない
        Assert.Equal("_CON.md", await FileNameAsync("minutes-con", "CON"));
        Assert.Equal("_nul.md", await FileNameAsync("minutes-nul", "nul"));
        Assert.Equal("_aux.部門.md", await FileNameAsync("minutes-aux", "aux.部門"));

        // 装置名のうしろの空白は Windows が読み飛ばすので、「CON .部門」も装置のまま
        Assert.Equal("_CON .部門.md", await FileNameAsync("minutes-con-space", "CON .部門"));

        // コンソール入出力の別名。CON と同じく保存できない
        Assert.Equal("_CONIN$.md", await FileNameAsync("minutes-conin", "CONIN$"));
        Assert.Equal("_CONOUT$.md", await FileNameAsync("minutes-conout", "CONOUT$"));
    }

    [Fact]
    public async Task 長い会議名はUTF8で200バイトに収める()
    {
        var fileName = await FileNameAsync("minutes-long", new string('あ', 200));

        // 会議名の上限は 200 文字。日本語なら 600 バイトになり、そのままでは保存できない環境がある
        Assert.True(Encoding.UTF8.GetByteCount(fileName) <= 200, $"{Encoding.UTF8.GetByteCount(fileName)} バイト");
        Assert.EndsWith(".md", fileName);
        Assert.StartsWith(new string('あ', 65), fileName);
    }

    [Fact]
    public async Task 切り詰めても文字が壊れない()
    {
        // 65 文字目までで 195 バイト。次の絵文字は 4 バイトで入らないので、上位サロゲートだけが残りうる
        var fileName = await FileNameAsync("minutes-cut", new string('あ', 65) + "🙂" + new string('い', 100));

        Assert.Equal(new string('あ', 65) + ".md", fileName);
        Assert.DoesNotContain(fileName, char.IsSurrogate);
    }

    [Fact]
    public async Task 未知の形式は400になる()
    {
        var meeting = await CreateAsync("minutes-bad");
        var client = _factory.CreateClientAs("minutes-bad");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 議事録が無ければ404になる()
    {
        var meeting = await CreateAsync("minutes-none", minutes: null);
        var client = _factory.CreateClientAs("minutes-none");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=md");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 他人の議事録は404になる()
    {
        var meeting = await CreateAsync("minutes-owner");
        var client = _factory.CreateClientAs("minutes-other");

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=md");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 匿名では取得できない()
    {
        var meeting = await CreateAsync("minutes-anon");
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/minutes?format=md");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 閲覧画面と編集画面にダウンロード導線が出る()
    {
        var meeting = await CreateAsync("minutes-links");
        var client = _factory.CreateClientAs("minutes-links");

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Details/{meeting.Id}"));
        var edit = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        foreach (var html in new[] { details, edit })
        {
            Assert.Contains($"/api/meetings/{meeting.Id}/minutes?format=md", html);
            Assert.Contains($"/api/meetings/{meeting.Id}/minutes?format=txt", html);
        }
    }

    [Fact]
    public async Task 議事録が無い間はダウンロード導線を出さない()
    {
        var meeting = await CreateAsync("minutes-nolinks", minutes: null);
        var client = _factory.CreateClientAs("minutes-nolinks");

        var details = await client.GetStringAsync($"/Meetings/Details/{meeting.Id}");
        var edit = await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}");

        Assert.DoesNotContain("/minutes?format=", details);
        Assert.DoesNotContain("/minutes?format=", edit);
    }
}
