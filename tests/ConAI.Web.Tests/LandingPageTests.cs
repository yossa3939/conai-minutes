using System.Net;

namespace ConAI.Web.Tests;

public class LandingPageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public LandingPageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 未ログインならログイン導線だけを見せる()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("/Identity/Account/Login", html);
    }

    [Fact]
    public async Task ナビに会議タブを出さない()
    {
        // ロゴ（/）がログイン済みなら会議一覧へ送るため、ヘッダーの会議タブは冗長として廃止した。
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("href=\"/Meetings\"", html);
    }

    [Fact]
    public async Task ログイン済みなら会議一覧へ送る()
    {
        using var client = _factory.CreateClientAs("landing-user");

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/Meetings", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task エラーページは未ログインでも表示できる()
    {
        // UseExceptionHandler の再実行は未認証でも届く。リダイレクトを追うと偽陽性になるため追わない。
        using var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Error");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
