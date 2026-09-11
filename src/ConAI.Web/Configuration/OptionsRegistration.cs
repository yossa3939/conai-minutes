using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddConAIOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var readEnvironment = getEnvironmentVariable ?? (name => Environment.GetEnvironmentVariable(name));

        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .PostConfigure(options =>
            {
                // Gemini:ApiKey（user-secrets / Gemini__ApiKey）が空のときだけ GEMINI_API_KEY で補う
                if (string.IsNullOrWhiteSpace(options.ApiKey))
                {
                    options.ApiKey = readEnvironment(GeminiOptions.ApiKeyEnvironmentVariable)?.Trim() ?? string.Empty;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Smtp:Password は環境変数 Smtp__Password が Bind で拾う標準経路だけを正とする。
        // SMTP_PASSWORD のような独自の環境変数名は広く使われる標準が無く、補う価値がないため廃止した
        services.AddOptions<SmtpOptions>()
            .Bind(configuration.GetSection(SmtpOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<LiveOptions>()
            .Bind(configuration.GetSection(LiveOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<UploadOptions>()
            .Bind(configuration.GetSection(UploadOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageOptions>()
            .Bind(configuration.GetSection(StorageOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
