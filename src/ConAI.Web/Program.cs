using System.Globalization;
using ConAI.Web;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Endpoints;
using ConAI.Web.Gemini;
using ConAI.Web.Infrastructure;
using ConAI.Web.Live;
using ConAI.Web.Middleware;
using ConAI.Web.Notifications;
using ConAI.Web.Search;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

// 管理コマンド（list-users / reset-password）の語は設定の書式ではないため、
// CreateBuilder へ渡す前に取り除き、残りの引数だけをホストへ渡す
var adminCommand = AdminCommands.Parse(args, out var hostArgs);
var builder = WebApplication.CreateBuilder(hostArgs);

if (adminCommand is not null)
{
    // 既定のログのままだと EF の SQL が流れて結果が埋もれる。provider ごと外すと失敗の警告まで消えるため、警告以上だけ残す
    builder.Logging.AddFilter((string? _, LogLevel level) => level >= LogLevel.Warning);
}

// 多重パートの枠分だけ余裕を持たせるため、転送側の上限は 1 MB 上乗せする
var maxUploadBytes = (long)(builder.Configuration.GetValue<int?>("Upload:MaxFileSizeMb") ?? 200) * 1024 * 1024;
var maxRequestBytes = maxUploadBytes + 1024 * 1024;

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxRequestBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = maxRequestBytes;
});
builder.Services.Configure<Microsoft.AspNetCore.Builder.IISServerOptions>(options =>
{
    options.MaxRequestBodySize = maxRequestBytes;
});

var storagePaths = StoragePaths.FromConfiguration(builder.Configuration, builder.Environment.ContentRootPath);
storagePaths.EnsureCreated();
builder.Services.AddSingleton(storagePaths);
builder.Services.AddConAIOptions(builder.Configuration);
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddScoped<IMeetingService, MeetingService>();
builder.Services.AddScoped<IMinutesSelector, MinutesSelector>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
// 辞書を抱えるため使い回す。Tagger の貸し出しはこの中で直列化する
builder.Services.AddSingleton<ISearchTokenizer, NMeCabSearchTokenizer>();
builder.Services.AddScoped<IMeetingSearchIndexer, MeetingSearchIndexer>();
builder.Services.AddScoped<IMeetingSearchService, MeetingSearchService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IPromptService, PromptService>();
builder.Services.AddSingleton<IMinutesTemplateSeeds, MinutesTemplateSeeds>();
builder.Services.AddScoped<IMinutesTemplateService, MinutesTemplateService>();
builder.Services.AddSingleton<IReferenceDocumentConverter, ReferenceDocumentConverter>();
builder.Services.AddGeminiClients();
builder.Services.AddScoped<IMinutesGenerationService, MinutesGenerationService>();
builder.Services.AddSingleton<IGenerationQueue, GenerationQueue>();
builder.Services.AddSingleton<ILiveSessionRegistry, LiveSessionRegistry>();
builder.Services.AddScoped<ILiveSessionService, LiveSessionService>();
builder.Services.AddNotificationServices();
builder.Services.AddHostedService<GenerationWorker>();
builder.Services.AddHostedService<NotificationWorker>();
builder.Services.AddHostedService<MeetingSearchIndexWorker>();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddConAIRateLimiter();

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(storagePaths.KeysRoot));

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite($"Data Source={storagePaths.DatabaseFile}"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddErrorDescriber<JapaneseIdentityErrorDescriber>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Identity 既定の NoOpEmailSender はメールを黙って捨てるため、SMTP で実際に送る実装へ置き換える
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var japanese = new[] { new CultureInfo("ja-JP") };
    options.DefaultRequestCulture = new RequestCulture(japanese[0]);
    options.SupportedCultures = japanese;
    options.SupportedUICultures = japanese;
});

var allowSelfRegistration = builder.Configuration
    .GetSection(AuthOptions.SectionName)
    .Get<AuthOptions>()?.AllowSelfRegistration ?? true;

builder.Services.AddRazorPages(options =>
{
    // 個人データの削除は、会議とファイルの後始末が未実装のため経路ごと塞ぐ。設定では切り替えない
    options.Conventions.AddAreaPageRouteModelConvention(
        "Identity",
        "/Account/Manage/DeletePersonalData",
        model => model.Selectors.Clear());

    if (!allowSelfRegistration)
    {
        options.Conventions.AddAreaPageRouteModelConvention(
            "Identity",
            "/Account/Register",
            model => model.Selectors.Clear());
    }
})
.AddMvcOptions(options =>
{
    // 開催日時はカルチャに依らず yyyy/MM/dd HH:mm:ss で受け取る（DateTimeInput）
    options.ModelBinderProviders.Insert(0, new DateTimeInputModelBinderProvider());

    var provider = options.ModelBindingMessageProvider;
    provider.SetMissingBindRequiredValueAccessor(field => $"{field} の値が送信されていません。");
    provider.SetMissingKeyOrValueAccessor(() => "値を入力してください。");
    provider.SetMissingRequestBodyRequiredValueAccessor(() => "送信された本文が空です。");
    provider.SetValueMustNotBeNullAccessor(_ => "値を入力してください。");
    provider.SetAttemptedValueIsInvalidAccessor((value, field) => $"{field} に指定した「{value}」は正しくありません。");
    provider.SetNonPropertyAttemptedValueIsInvalidAccessor(value => $"指定した「{value}」は正しくありません。");
    provider.SetUnknownValueIsInvalidAccessor(field => $"{field} に指定した値は正しくありません。");
    provider.SetNonPropertyUnknownValueIsInvalidAccessor(() => "指定した値は正しくありません。");
    provider.SetValueIsInvalidAccessor(value => $"「{value}」は正しくありません。");
    provider.SetValueMustBeANumberAccessor(field => $"{field} には数値を入力してください。");
    provider.SetNonPropertyValueMustBeANumberAccessor(() => "数値を入力してください。");
});

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();

    var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
    await meetings.ResetInterruptedJobsAsync(CancellationToken.None);
}

// 管理コマンドはマイグレーションの後で走らせる。スキーマが古いままだと再設定に失敗するため
if (adminCommand is not null)
{
    using var adminScope = app.Services.CreateScope();
    var userManager = adminScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    return await AdminCommands.RunAsync(adminCommand, userManager, new ConsoleAdminConsole());
}

// 閉域網では Security = None も正当な構成のため起動は止めない。ただし再設定リンクのトークンが
// 暗号化なしで流れるため、設定ミスに気付けるよう起動時に 1 回だけ警告する
var smtpOptions = app.Services.GetRequiredService<IOptions<SmtpOptions>>().Value;
if (smtpOptions.IsConfigured
    && string.Equals(smtpOptions.Security, SmtpOptions.NoneSecurity, StringComparison.OrdinalIgnoreCase))
{
    app.Services.GetRequiredService<ILoggerFactory>()
        .CreateLogger<SmtpEmailSender>()
        .LogWarning(
            "Smtp:Security が 'None' のため、パスワード再設定リンク（トークン）が暗号化されずにそのまま流れます。閉域網以外では 'StartTls' か 'SslOnConnect' にしてください。");
}

app.UseConAISecurityHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRequestLocalization();
app.MapStaticAssets().AllowAnonymous();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapRazorPages().WithStaticAssets();

var fileEndpoints = app.MapMeetingFileEndpoints();
var contentEndpoints = app.MapMeetingContentEndpoints();
var generationEndpoints = app.MapGenerationEndpoints();
var chatEndpoints = app.MapChatEndpoints();
var searchEndpoints = app.MapSearchEndpoints();
app.MapLiveEndpoints();
app.MapNotificationEndpoints();
fileEndpoints.AddEndpointFilter<AntiforgeryEndpointFilter>();
contentEndpoints.AddEndpointFilter<AntiforgeryEndpointFilter>();
generationEndpoints.AddEndpointFilter<AntiforgeryEndpointFilter>();
chatEndpoints.AddEndpointFilter<AntiforgeryEndpointFilter>();
searchEndpoints.AddEndpointFilter<AntiforgeryEndpointFilter>();
fileEndpoints.RequireRateLimiting(RateLimitPolicies.Files);
contentEndpoints.RequireRateLimiting(RateLimitPolicies.Files);
chatEndpoints.RequireRateLimiting(RateLimitPolicies.Chat);
searchEndpoints.RequireRateLimiting(RateLimitPolicies.Search);
app.MapGet("/healthz", () => Results.Text("ok")).AllowAnonymous();

app.Run();

// 管理コマンドの return によりエントリーポイントが戻り値を持つため、Web の通常終了も 0 を返す
return 0;

public partial class Program { }
