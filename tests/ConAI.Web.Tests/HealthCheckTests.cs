namespace ConAI.Web.Tests;

public class HealthCheckTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public HealthCheckTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Healthz_匿名でも200を返す()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TestAuthHandler_ヘッダ付きのクライアントは認証済みになる()
    {
        var client = _factory.CreateClientAs("user-1");

        var response = await client.GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}
