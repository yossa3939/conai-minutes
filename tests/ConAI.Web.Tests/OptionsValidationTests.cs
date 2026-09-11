using ConAI.Web.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class OptionsValidationTests
{
    private static IServiceProvider Build(
        Dictionary<string, string?> values,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        // モデル名は既定値を持たない。設定漏れを起動時の失敗にするためで、
        // モデル名を見ないテストのほうは土台として埋めてから各テストの値で上書きする。
        var settings = ModelSettings();

        foreach (var pair in values)
        {
            settings[pair.Key] = pair.Value;
        }

        return BuildFrom(settings, getEnvironmentVariable);
    }

    private static Dictionary<string, string?> ModelSettings() => new()
    {
        ["Gemini:LiveModel"] = "fake-live",
        ["Gemini:LiveTranslateModel"] = "fake-translate",
        ["Gemini:GenerateModel"] = "fake-generate",
        ["Gemini:SelectModel"] = "fake-select"
    };

    private static IServiceProvider BuildFrom(
        Dictionary<string, string?> settings,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        // 既定では環境変数を読まない。実環境の GEMINI_API_KEY がテスト結果に影響しないようにする
        services.AddConAIOptions(configuration, getEnvironmentVariable ?? (_ => null));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Googleプロバイダでキーが空なら検証に失敗する()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Google",
            ["Gemini:ApiKey"] = ""
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GeminiOptions>>().Value);
        Assert.Contains("Gemini:ApiKey", ex.Message);
    }

    [Fact]
    public void Googleプロバイダでキーが空でも環境変数GEMINI_API_KEYがあれば通る()
    {
        var provider = Build(
            new Dictionary<string, string?>
            {
                ["Gemini:Provider"] = "Google",
                ["Gemini:ApiKey"] = ""
            },
            name => name == GeminiOptions.ApiKeyEnvironmentVariable ? "env-key" : null);

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal("env-key", options.ApiKey);
    }

    [Fact]
    public void 設定のキーは環境変数GEMINI_API_KEYより優先される()
    {
        var provider = Build(
            new Dictionary<string, string?>
            {
                ["Gemini:Provider"] = "Google",
                ["Gemini:ApiKey"] = "config-key"
            },
            _ => "env-key");

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal("config-key", options.ApiKey);
    }

    [Fact]
    public void Fakeプロバイダならキーが空でも検証を通る()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Gemini:ApiKey"] = ""
        });

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal("Fake", options.Provider);
    }

    [Fact]
    public void 未知のプロバイダ名は検証に失敗する()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "OpenAI",
            ["Gemini:ApiKey"] = "x"
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GeminiOptions>>().Value);
    }

    [Theory]
    [InlineData("Gemini:LiveModel", "LiveModel")]
    [InlineData("Gemini:LiveTranslateModel", "LiveTranslateModel")]
    [InlineData("Gemini:GenerateModel", "GenerateModel")]
    [InlineData("Gemini:SelectModel", "SelectModel")]
    public void モデル名の設定が無ければ検証に失敗する(string key, string propertyName)
    {
        var settings = ModelSettings();
        settings["Gemini:Provider"] = "Fake";
        settings.Remove(key);

        var provider = BuildFrom(settings);

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GeminiOptions>>().Value);
        Assert.Contains(propertyName, ex.Message);
    }

    [Fact]
    public void インライン上限はメガバイトからバイトへ換算される()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Gemini:InlineLimitMb"] = "20"
        });

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal(20L * 1024 * 1024, options.InlineLimitBytes);
    }

    [Fact]
    public void Notificationsのプロバイダが未知なら検証に失敗する()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Notifications:Provider"] = "Webhook"
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
        Assert.Contains("Notifications:Provider", ex.Message);
    }

    [Fact]
    public void NotificationsのPublicBaseUrlが相対URLなら検証に失敗する()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Notifications:PublicBaseUrl"] = "/conai"
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
        Assert.Contains("Notifications:PublicBaseUrl", ex.Message);
    }

    [Fact]
    public void NotificationsのPublicBaseUrlは空でも通る()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake"
        });

        var options = provider.GetRequiredService<IOptions<NotificationOptions>>().Value;

        Assert.Equal(string.Empty, options.PublicBaseUrl);
        Assert.Equal(1500, options.ExcerptChars);
        Assert.Equal(10, options.MaxEndpointsPerUser);
    }

    [Fact]
    public void Notificationsの抜粋文字数が範囲外なら検証に失敗する()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Notifications:ExcerptChars"] = "4001"
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<NotificationOptions>>().Value);
    }

    [Theory]
    [InlineData("Gemini:GenerateThinkingLevel")]
    [InlineData("Gemini:SelectThinkingLevel")]
    [InlineData("Gemini:ChatThinkingLevel")]
    public void ThinkingLevelに許可値以外を書くと検証に失敗する(string key)
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            [key] = "HIGHEST"
        });

        var ex = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<GeminiOptions>>().Value);
        Assert.Contains(key, ex.Message);
    }

    [Fact]
    public void ThinkingLevelに正しい値を書くと検証を通り値はそのまま入る()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Gemini:GenerateThinkingLevel"] = "low",
            ["Gemini:SelectThinkingLevel"] = "MEDIUM",
            ["Gemini:ChatThinkingLevel"] = "High"
        });

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        // 許可値の確認は起動時検証で済ませる。プロパティには正規化せず書かれた値をそのまま残す。
        Assert.Equal("low", options.GenerateThinkingLevel);
        Assert.Equal("MEDIUM", options.SelectThinkingLevel);
        Assert.Equal("High", options.ChatThinkingLevel);
    }

    [Fact]
    public void ThinkingLevelを設定しなければ既定は空文字で検証を通る()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake"
        });

        var options = provider.GetRequiredService<IOptions<GeminiOptions>>().Value;

        Assert.Equal(string.Empty, options.GenerateThinkingLevel);
        Assert.Equal(string.Empty, options.SelectThinkingLevel);
        Assert.Equal(string.Empty, options.ChatThinkingLevel);
    }

    // 環境変数 Smtp__Password はこの設定キーに入るため、標準の Bind 経路だけで渡ることの確認になる
    [Fact]
    public void Smtpのパスワードは設定キーからバインドされる()
    {
        var provider = Build(new Dictionary<string, string?>
        {
            ["Gemini:Provider"] = "Fake",
            ["Smtp:Host"] = "smtp.example.com",
            ["Smtp:FromAddress"] = "from@example.com",
            ["Smtp:Password"] = "config-password"
        });

        var options = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal("config-password", options.Password);
    }

    // 独自の環境変数名（SMTP_PASSWORD）で補う経路は廃止した。標準の Smtp__Password は設定キー側に
    // 入るため、どんな名前の環境変数があってもここで補われないことを固定する
    [Fact]
    public void Smtpのパスワードは環境変数からは補われない()
    {
        var provider = Build(
            new Dictionary<string, string?>
            {
                ["Gemini:Provider"] = "Fake",
                ["Smtp:Host"] = "smtp.example.com",
                ["Smtp:FromAddress"] = "from@example.com"
            },
            _ => "env-password");

        var options = provider.GetRequiredService<IOptions<SmtpOptions>>().Value;

        Assert.Equal(string.Empty, options.Password);
    }
}
