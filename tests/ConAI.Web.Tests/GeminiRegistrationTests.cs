using ConAI.Web.Gemini;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class GeminiRegistrationTests
{
    [Fact]
    public void Googleプロバイダではgoogle実装が解決される()
    {
        using var factory = new ConAIWebApplicationFactory { Provider = "Google" };
        using var scope = factory.CreateScope();

        Assert.IsType<GoogleGeminiLiveClient>(scope.ServiceProvider.GetRequiredService<IGeminiLiveClient>());
        Assert.IsType<GoogleGeminiContentClient>(scope.ServiceProvider.GetRequiredService<IGeminiContentClient>());
    }

    [Fact]
    public void Fakeプロバイダではgoogle実装を作らない()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var scope = factory.CreateScope();

        Assert.IsType<FakeGeminiContentClient>(scope.ServiceProvider.GetRequiredService<IGeminiContentClient>());
    }
}
