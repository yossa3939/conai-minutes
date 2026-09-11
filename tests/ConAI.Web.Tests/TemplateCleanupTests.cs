using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class TemplateCleanupTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public TemplateCleanupTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task プライバシーページが消えている()
    {
        var client = _factory.CreateClientAs("cleanup-privacy");

        var response = await client.GetAsync("/Privacy");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task レイアウトが日本語のブランドと著作権表示になる()
    {
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("<html lang=\"ja\">", html);
        Assert.Contains("<title>議事録 - 議事録</title>", html);
        Assert.Contains("© 2026 ConAI Project", html);
        Assert.DoesNotContain("ConAI.Web", html);
        Assert.DoesNotContain("/Privacy", html);
        Assert.DoesNotContain("site.js", html);
    }

    [Fact]
    public async Task エラーページが日本語になる()
    {
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Error"));

        Assert.Contains("エラーが発生しました", html);
        Assert.DoesNotContain("Development Mode", html);
        Assert.DoesNotContain("Request ID", html);
    }

    [Fact]
    public async Task 会議名の長さ制限が日本語で出力される()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var created = await service.CreateAsync(
                "cleanup-length",
                new Meeting { Title = "長さ制限の確認" },
                CancellationToken.None);
            id = created.Id;
        }

        var client = _factory.CreateClientAs("cleanup-length");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Edit/{id}"));

        // クライアント側の検証はこの属性の文言をそのまま出す。
        // 必須と長さ超過で、サーバ側と同じ日本語が出ることをここで固定する。
        Assert.Contains("data-val-required=\"会議名を入力してください。\"", html);
        Assert.Contains("data-val-length=\"会議名は 200 文字以内で入力してください。\"", html);
    }

    [Fact]
    public async Task 翻訳先言語の長さ制限が日本語で出る()
    {
        var client = _factory.CreateClientAs("cleanup-lang");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync("/Meetings/Create"));

        var response = await client.PostAsync("/Meetings/Create", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "言語コードが長い会議",
            ["Input.TargetLanguage"] = "abcdefghijk",
            ["__RequestVerificationToken"] = token
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("翻訳先言語のコードは 10 文字以内で入力してください。", html);
    }
}
