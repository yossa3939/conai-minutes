using System.Net;

namespace ConAI.Web.Tests;

public class SecurityHeaderTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string ExpectedCsp =
        "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
        "connect-src 'self' wss:; object-src 'none'; frame-ancestors 'none'; base-uri 'self'";

    private readonly ConAIWebApplicationFactory _factory;

    public SecurityHeaderTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 全応答にセキュリティヘッダが付く()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(ExpectedCsp, response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal(
            "microphone=(self), camera=(), display-capture=(self)",
            response.Headers.GetValues("Permissions-Policy").Single());
    }

    [Fact]
    public async Task インラインscriptはレイアウトに残っていない()
    {
        var client = _factory.CreateClientAs("csp-a");

        var html = await client.GetStringAsync("/Meetings");

        Assert.DoesNotContain("<script type=\"importmap\"", html);
        Assert.DoesNotContain("<script>", html);
    }

    [Fact]
    public async Task 静的ファイルは匿名で取得できる()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/css/app.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task 経路に一致しないリクエストも匿名では開けない()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // フォールバックの認可ポリシーは、エンドポイントが見つからないリクエストにも適用される。
        // 404 ではなく 401 が返ることが、既定で閉じている証拠になる。
        var response = await client.GetAsync("/Privacy");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
