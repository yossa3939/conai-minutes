using System.Net;

namespace ConAI.Web.Tests;

public class TailwindAssetTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public TailwindAssetTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task レイアウトが読み込むスタイルシートは生成した_1_本だけになる()
    {
        var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Matches(@"/css/app(\.[a-z0-9]+)?\.css", html);
        Assert.DoesNotContain("bootstrap", html);
        Assert.DoesNotContain("/css/site.css", html);
        Assert.DoesNotContain("/css/conai.css", html);
        Assert.DoesNotContain("data-bs-", html);
        Assert.DoesNotContain("navbar-toggler", html);
    }

    [Fact]
    public async Task 生成した_CSS_が配信される()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/css/app.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);

        var css = await response.Content.ReadAsStringAsync();
        Assert.Contains("--color-primary", css);
        Assert.Contains(".nav-link", css);
        Assert.Contains(".conai-recording", css);
        Assert.Contains(".field-validation-error", css);
    }
}
