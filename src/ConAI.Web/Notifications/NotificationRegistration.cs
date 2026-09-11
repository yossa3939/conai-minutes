using ConAI.Web.Configuration;
using ConAI.Web.Services;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Notifications;

public static class NotificationRegistration
{
    public static IServiceCollection AddNotificationServices(this IServiceCollection services)
    {
        services.AddSingleton<IWebhookUrlProtector, WebhookUrlProtector>();
        services.AddScoped<IWebhookEndpointService, WebhookEndpointService>();
        services.AddSingleton<IWebhookPayloadBuilder, SlackPayloadBuilder>();
        services.AddSingleton<IWebhookPayloadBuilder, GoogleChatPayloadBuilder>();
        services.AddSingleton<IWebhookPayloadBuilder, DiscordPayloadBuilder>();
        services.AddSingleton<IWebhookPayloadBuilder, TeamsPayloadBuilder>();

        services.AddHttpClient(HttpWebhookSender.ClientName, (provider, client) =>
            {
                var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            })
            // 転送を追いかけると、許可リストを通した URL から外の宛先へ送ってしまう
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

        services.AddSingleton<FakeWebhookSender>();
        services.AddSingleton<HttpWebhookSender>();
        services.AddSingleton<IWebhookSender>(provider =>
            provider.GetRequiredService<IOptions<NotificationOptions>>().Value.IsFake
                ? provider.GetRequiredService<FakeWebhookSender>()
                : provider.GetRequiredService<HttpWebhookSender>());

        services.AddSingleton<INotificationDelay, NotificationDelay>();
        services.AddSingleton<IWebhookPacer, WebhookPacer>();
        services.AddSingleton<INotificationQueue, NotificationQueue>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();

        return services;
    }
}
