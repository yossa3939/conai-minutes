using System.Net;
using System.Text.RegularExpressions;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class TemplatePagesTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public TemplatePagesTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<MinutesTemplate> TemplateAsync(string ownerId, string name)
    {
        using var scope = _factory.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
        var list = await templates.ListAsync(ownerId, CancellationToken.None);
        return list.First(t => t.Name == name);
    }

    private async Task<IReadOnlyList<MinutesTemplate>> ListAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
        return await templates.ListAsync(ownerId, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        string token,
        Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [Fact]
    public async Task 匿名では一覧にアクセスできない()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Templates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 一覧には自分のテンプレートだけが並び既定に印が付く()
    {
        var others = await TemplateAsync("tpl-page-other", "標準");
        var client = _factory.CreateClientAs("tpl-page-a");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Templates"));

        var standard = await TemplateAsync("tpl-page-a", "標準");
        Assert.Contains($"data-template-id=\"{standard.Id}\"", html);
        Assert.DoesNotContain($"data-template-id=\"{others.Id}\"", html);

        // 既定の行にだけ印が付き、既定の行には「既定にする」と「削除」を出さない
        var row = Regex.Match(html, $"""<tr data-template-id="{standard.Id}".*?</tr>""", RegexOptions.Singleline).Value;
        Assert.Contains("既定", row);
        Assert.DoesNotContain("既定にする", row);
        Assert.DoesNotContain("削除", row);
    }

    [Fact]
    public async Task ヘッダーにテンプレートへのリンクが出る()
    {
        var client = _factory.CreateClientAs("tpl-page-nav");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings"));

        Assert.Contains("href=\"/Templates\"", html);
        Assert.Contains("テンプレート", html);
    }

    [Fact]
    public async Task 作成で自分のテンプレートが増える()
    {
        var client = _factory.CreateClientAs("tpl-page-create");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Templates/Create"));

        var response = await PostAsync(client, "/Templates/Create", token, new Dictionary<string, string>
        {
            ["Input.Name"] = "議事メモ",
            ["Input.Body"] = "## 決まったこと\n- 決めた内容"
        });

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Templates", response.Headers.Location!.ToString());

        var list = await ListAsync("tpl-page-create");
        var created = Assert.Single(list, t => t.Name == "議事メモ");
        // 作ったものは既定にしない（切り替えは「既定にする」で行う）
        Assert.False(created.IsDefault);
    }

    [Fact]
    public async Task 名前が空なら作成ページを再表示する()
    {
        var client = _factory.CreateClientAs("tpl-page-invalid");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Templates/Create"));

        var response = await PostAsync(client, "/Templates/Create", token, new Dictionary<string, string>
        {
            ["Input.Name"] = "",
            ["Input.Body"] = "## 決まったこと"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("名前を入力してください。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(2, (await ListAsync("tpl-page-invalid")).Count);
    }

    [Fact]
    public async Task 他人のテンプレートの編集ページは404になる()
    {
        var others = await TemplateAsync("tpl-page-edit-other", "標準");
        var client = _factory.CreateClientAs("tpl-page-edit-mine");

        var response = await client.GetAsync($"/Templates/Edit/{others.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 編集で名前と本文を変えられる()
    {
        var concise = await TemplateAsync("tpl-page-edit", "簡潔");
        var client = _factory.CreateClientAs("tpl-page-edit");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Templates/Edit/{concise.Id}"));

        var response = await PostAsync(client, $"/Templates/Edit/{concise.Id}", token, new Dictionary<string, string>
        {
            ["Input.Name"] = "短くまとめる",
            ["Input.Body"] = "## 要点\n- 要点"
        });

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var updated = await TemplateAsync("tpl-page-edit", "短くまとめる");
        Assert.Contains("## 要点", updated.Body);
    }

    [Fact]
    public async Task 他人のテンプレートへの保存は404になる()
    {
        var others = await TemplateAsync("tpl-page-post-other", "標準");
        var client = _factory.CreateClientAs("tpl-page-post-mine");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Templates/Create"));

        var response = await PostAsync(client, $"/Templates/Edit/{others.Id}", token, new Dictionary<string, string>
        {
            ["Input.Name"] = "乗っ取り",
            ["Input.Body"] = "## 本文"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain(await ListAsync("tpl-page-post-other"), t => t.Name == "乗っ取り");
    }

    [Fact]
    public async Task 複製するとコピーの行が増える()
    {
        var standard = await TemplateAsync("tpl-page-copy", "標準");
        var client = _factory.CreateClientAs("tpl-page-copy");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Templates"));

        var response = await PostAsync(
            client,
            $"/Templates?handler=Duplicate&id={standard.Id}",
            token,
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var list = await ListAsync("tpl-page-copy");
        var copy = Assert.Single(list, t => t.Name == "標準（コピー）");
        Assert.Equal(standard.Body, copy.Body);
        Assert.False(copy.IsDefault);
    }

    [Fact]
    public async Task 既定にするを押すと印が移る()
    {
        var concise = await TemplateAsync("tpl-page-default", "簡潔");
        var client = _factory.CreateClientAs("tpl-page-default");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Templates"));

        var response = await PostAsync(
            client,
            $"/Templates?handler=SetDefault&id={concise.Id}",
            token,
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var list = await ListAsync("tpl-page-default");
        Assert.True(list.Single(t => t.Name == "簡潔").IsDefault);
        Assert.False(list.Single(t => t.Name == "標準").IsDefault);
    }

    [Fact]
    public async Task 削除の確認画面は使っている会議の件数を出す()
    {
        var concise = await TemplateAsync("tpl-page-count", "簡潔");
        using (var scope = _factory.CreateScope())
        {
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await meetings.CreateAsync(
                "tpl-page-count",
                new Meeting { Title = "使っている会議", MinutesTemplateId = concise.Id },
                CancellationToken.None);
        }

        var client = _factory.CreateClientAs("tpl-page-count");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Templates/Delete/{concise.Id}"));

        Assert.Contains("このテンプレートを使う会議が 1 件あります。削除すると、それらの会議は「標準」に戻ります。", html);
    }

    [Fact]
    public async Task 付け替え先が無い削除では会議が空になると知らせる()
    {
        // 既定が欠けた状態で残り 1 件まで減らす。付け替え先が無いので、確認画面は
        // GetDefaultAsync が返す「これから消すテンプレート自身」の名前を出してはいけない。
        var standard = await TemplateAsync("tpl-page-empty", "標準");
        var concise = await TemplateAsync("tpl-page-empty", "簡潔");
        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var template in db.MinutesTemplates.Where(t => t.OwnerId == "tpl-page-empty").ToList())
            {
                template.IsDefault = false;
            }

            await db.SaveChangesAsync();

            var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            Assert.Equal(
                DeleteTemplateResult.Deleted,
                await templates.DeleteAsync(concise.Id, "tpl-page-empty", CancellationToken.None));

            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await meetings.CreateAsync(
                "tpl-page-empty",
                new Meeting { Title = "使っている会議", MinutesTemplateId = standard.Id },
                CancellationToken.None);
        }

        var client = _factory.CreateClientAs("tpl-page-empty");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Templates/Delete/{standard.Id}"));

        Assert.Contains(
            "このテンプレートを使う会議が 1 件あります。削除すると、それらの会議のテンプレートは空になり、次の生成では新しく配られる既定を使います。",
            html);
    }

    [Fact]
    public async Task 既定のテンプレートは削除できない()
    {
        var standard = await TemplateAsync("tpl-page-default-del", "標準");
        var client = _factory.CreateClientAs("tpl-page-default-del");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Templates/Delete/{standard.Id}"));

        var confirm = WebUtility.HtmlDecode(await client.GetStringAsync($"/Templates/Delete/{standard.Id}"));
        Assert.Contains("既定のテンプレートは削除できません。先に別のテンプレートを既定にしてください。", confirm);

        // 画面のボタンは無効だが、直接 POST しても消えない
        var response = await PostAsync(
            client,
            $"/Templates/Delete/{standard.Id}",
            token,
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await ListAsync("tpl-page-default-del")).Count);
    }

    [Fact]
    public async Task 削除すると使っていた会議が既定に戻る()
    {
        var concise = await TemplateAsync("tpl-page-del", "簡潔");
        Guid meetingId;
        using (var scope = _factory.CreateScope())
        {
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await meetings.CreateAsync(
                "tpl-page-del",
                new Meeting { Title = "付け替えられる会議", MinutesTemplateId = concise.Id },
                CancellationToken.None);
            meetingId = meeting.Id;
        }

        var client = _factory.CreateClientAs("tpl-page-del");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Templates/Delete/{concise.Id}"));

        var response = await PostAsync(
            client,
            $"/Templates/Delete/{concise.Id}",
            token,
            new Dictionary<string, string>());

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        var standard = await TemplateAsync("tpl-page-del", "標準");
        using var check = _factory.CreateScope();
        var service = check.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meetingId, "tpl-page-del", CancellationToken.None);
        Assert.Equal(standard.Id, loaded!.MinutesTemplateId);
    }
}
