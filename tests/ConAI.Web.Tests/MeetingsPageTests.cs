using System.Net;
using System.Text.RegularExpressions;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingsPageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingsPageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 匿名では一覧にアクセスできない()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Meetings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 一覧には自分の会議だけが並ぶ()
    {
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await service.CreateAsync("page-a", new Meeting { Title = "自分の会議" }, CancellationToken.None);
            await service.CreateAsync("page-b", new Meeting { Title = "他人の会議" }, CancellationToken.None);
        }

        var client = _factory.CreateClientAs("page-a");
        // 動的出力の日本語は HtmlEncoder 既定で数値文字参照になるため、デコードしてから比較する
        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings"));

        Assert.Contains("自分の会議", html);
        Assert.DoesNotContain("他人の会議", html);
    }

    [Fact]
    public async Task 作成ページのPOSTで会議ができて編集画面へ遷移する()
    {
        var client = _factory.CreateClientAs("page-create");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "新しい会議",
            ["Input.LiveMode"] = "true",
            ["Input.TranslateMode"] = "false",
            ["Input.TargetLanguage"] = "ja",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.StartsWith("/Meetings/Edit/", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task 作成ページはモードのラジオを既定でLiveにする()
    {
        var client = _factory.CreateClientAs("page-create-design");

        var html = await client.GetStringAsync("/Meetings/Create");

        // 入力元のモードはラジオ 2 つで切り替える（RV 2）。既定は Live
        var live = Regex.Match(html, """<input[^>]*id="Input_LiveMode_live"[^>]*>""").Value;
        var file = Regex.Match(html, """<input[^>]*id="Input_LiveMode_file"[^>]*>""").Value;
        Assert.NotEmpty(live);
        Assert.Contains("checked", live);
        Assert.DoesNotContain("checked", file);

        // 翻訳モードを OFF で始まるので、翻訳先言語は隠れた状態で描かれる（translate-toggle.js が切り替える）
        var field = Regex.Match(html, """<div[^>]*id="target-language-field"[^>]*>""").Value;
        Assert.NotEmpty(field);
        Assert.Contains("hidden=\"hidden\"", field);
    }

    [Fact]
    public async Task 会議名が空なら作成ページを再表示する()
    {
        var client = _factory.CreateClientAs("page-invalid");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "",
            ["Input.TargetLanguage"] = "ja",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        // 検証メッセージの日本語も数値文字参照で出るため、デコードしてから比較する
        Assert.Contains("会議名を入力してください。", System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Antiforgeryトークンなしの作成POSTは400になる()
    {
        var client = _factory.CreateClientAs("page-noxsrf");

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "トークンなし",
            ["Input.TargetLanguage"] = "ja"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 作成ページのテンプレートのセレクトボックスは既定を選んだ状態で描かれる()
    {
        var client = _factory.CreateClientAs("page-template");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings/Create"));

        var select = Regex.Match(html, """<select[^>]*id="minutes-template".*?</select>""", RegexOptions.Singleline).Value;
        Assert.NotEmpty(select);
        Assert.Contains("name=\"Input.MinutesTemplateId\"", select);

        // 画面を開いた時点で組み込み 2 件が配られ、既定の「標準」が選ばれる
        Assert.Contains("簡潔", select);
        var standard = Regex.Match(select, """<option[^>]*>標準</option>""").Value;
        Assert.Contains("selected", standard);
    }

    [Fact]
    public async Task 作成ページで他人のテンプレートを指定すると再表示になる()
    {
        Guid otherId;
        using (var scope = _factory.CreateScope())
        {
            var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            otherId = (await templates.GetDefaultAsync("page-template-other", CancellationToken.None)).Id;
        }

        var client = _factory.CreateClientAs("page-template-mine");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "他人のテンプレート",
            ["Input.LiveMode"] = "true",
            ["Input.TranslateMode"] = "false",
            ["Input.TargetLanguage"] = "ja",
            ["Input.MinutesTemplateId"] = otherId.ToString(),
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("選べないテンプレートです。", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }
}
