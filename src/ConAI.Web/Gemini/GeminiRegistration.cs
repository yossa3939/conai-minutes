using ConAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Gemini;

public static class GeminiRegistration
{
    public static IServiceCollection AddGeminiClients(this IServiceCollection services)
    {
        services.AddSingleton<FakeGeminiLiveClient>();
        services.AddSingleton<FakeGeminiContentClient>();
        services.AddSingleton<GoogleGeminiLiveClient>();
        services.AddSingleton<GoogleGeminiContentClient>();

        services.AddSingleton<IGeminiLiveClient>(provider => IsFake(provider)
            ? provider.GetRequiredService<FakeGeminiLiveClient>()
            : provider.GetRequiredService<GoogleGeminiLiveClient>());

        services.AddSingleton<IGeminiContentClient>(provider => IsFake(provider)
            ? provider.GetRequiredService<FakeGeminiContentClient>()
            : provider.GetRequiredService<GoogleGeminiContentClient>());

        return services;
    }

    private static bool IsFake(IServiceProvider provider) =>
        provider.GetRequiredService<IOptions<GeminiOptions>>().Value.IsFake;
}
