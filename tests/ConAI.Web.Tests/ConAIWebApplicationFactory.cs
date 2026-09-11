using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConAI.Web.Tests;

public class ConAIWebApplicationFactory : WebApplicationFactory<Program>
{
    public ConAIWebApplicationFactory()
    {
        DataRoot = Path.Combine(Path.GetTempPath(), "conai-tests", Guid.NewGuid().ToString("N"));
    }

    public string DataRoot { get; }

    public bool UseTestAuth { get; init; } = true;

    public string Provider { get; init; } = "Fake";

    public IReadOnlyDictionary<string, string?> ExtraSettings { get; init; } = new Dictionary<string, string?>();

    /// <summary>本物のサービスを差し替えたいテストのための入れ替え口。既定の登録より後に適用される。</summary>
    public Action<IServiceCollection>? ConfigureServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);

        // Program.cs は Storage:DataRoot を builder.Build() より前（StoragePaths.FromConfiguration）で読む。
        // 最小ホスティングでは ConfigureAppConfiguration の上書きは Build() 時にしか適用されないため、
        // ホスト構成（UseSetting → WebApplication.CreateBuilder の args）経由でないと既定の App_Data に落ちる。
        // ExtraSettings も Program.cs が Build() 前に読む（Auth:AllowSelfRegistration 等）ため同じ経由に乗せる。
        builder.UseSetting("Storage:DataRoot", DataRoot);

        foreach (var pair in ExtraSettings)
        {
            builder.UseSetting(pair.Key, pair.Value);
        }

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Storage:DataRoot"] = DataRoot,
                ["Gemini:Provider"] = Provider,
                ["Gemini:ApiKey"] = "test-key",
                // モデル名は既定値を持たないので、テストでも明示する。
                // 値そのものは Fake プロバイダが使わないため何でもよい。
                ["Gemini:LiveModel"] = "fake-live",
                ["Gemini:LiveTranslateModel"] = "fake-translate",
                ["Gemini:GenerateModel"] = "fake-generate",
                ["Gemini:SelectModel"] = "fake-select",
                ["Live:MaxSessionsPerUser"] = "1",
                // 既定で本物の送信を止め、テストがネットワークへ出ないようにする
                ["Notifications:Provider"] = "Fake"
            };

            foreach (var pair in ExtraSettings)
            {
                settings[pair.Key] = pair.Value;
            }

            config.AddInMemoryCollection(settings);
        });

        builder.ConfigureTestServices(services =>
        {
            if (UseTestAuth)
            {
                services.AddAuthentication(TestAuthHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

                services.AddAuthorization(options =>
                {
                    options.DefaultPolicy = new AuthorizationPolicyBuilder(TestAuthHandler.SchemeName)
                        .RequireAuthenticatedUser()
                        .Build();
                });
            }

            ConfigureServices?.Invoke(services);
        });
    }

    public HttpClient CreateClientAs(string userId)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.UserHeader, userId);
        return client;
    }

    public IServiceScope CreateScope() => Services.CreateScope();

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
        {
            return;
        }

        try
        {
            // SQLite の接続プールが conai.db を開いたままにするため、プールを空にしてから消す
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(DataRoot))
            {
                Directory.Delete(DataRoot, recursive: true);
            }
        }
        catch (IOException)
        {
            // 一時ディレクトリの後始末が失敗してもテスト結果には影響させない
        }
    }
}
