using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class PasswordResetPageTests
{
    /// <summary>IEmailSender の偽物。送信先・件名・本文を記録するだけで、実際には送らない。</summary>
    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string Email, string Subject, string HtmlMessage)> Sent { get; } = [];

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Sent.Add((email, subject, htmlMessage));
            return Task.CompletedTask;
        }
    }

    /// <summary>IEmailSender の偽物。常に例外を投げて、SMTP 送信の失敗を演じる。</summary>
    private sealed class ThrowingEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage) =>
            throw new InvalidOperationException("テスト用の送信失敗");
    }

    // IdentityTestUsers.EnsureAsync は EmailConfirmed = true で作る。
    // このアプリでは確認済みユーザーが存在しない場面を試すため、UserManager で直に作る
    private static async Task CreateUnconfirmedUserAsync(
        ConAIWebApplicationFactory factory, string userId, string email)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser
        {
            Id = userId,
            UserName = email,
            Email = email,
            EmailConfirmed = false
        };

        var result = await userManager.CreateAsync(user, IdentityTestUsers.DefaultPassword);
        if (!result.Succeeded)
        {
            var reason = string.Join(" / ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"テスト用アカウントを作成できなかった: {reason}");
        }
    }

    private static ConAIWebApplicationFactory CreateConfiguredFactory(IEmailSender sender) => new()
    {
        ExtraSettings = new Dictionary<string, string?>
        {
            ["Smtp:Host"] = "smtp.example.test",
            ["Smtp:FromAddress"] = "noreply@example.test"
        },
        // AddSingleton で差し替え、テストから Sent を読めるようにする
        ConfigureServices = services => services.AddSingleton<IEmailSender>(sender)
    };

    // POST 先のリダイレクトをそのまま検査したいので、自動追跡を切ったクライアントを作る
    private static HttpClient CreateNoRedirectClient(ConAIWebApplicationFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task<HttpResponseMessage> PostForgotPasswordAsync(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Identity/Account/ForgotPassword");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(html);
        return await client.PostAsync("/Identity/Account/ForgotPassword",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["__RequestVerificationToken"] = token
            }));
    }

    [Fact]
    public async Task ログイン画面に再設定へのリンクがある()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Identity/Account/Login");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("id=\"forgot-password\"", html);
        Assert.Contains("パスワードを忘れた場合", html);
    }

    [Fact]
    public async Task メール未確認のユーザーにも再設定メールを送る()
    {
        // RequireConfirmedAccount = false で登録するため、誰もメール確認を通っていない。
        // 従来の IsEmailConfirmedAsync の検査は全ユーザーを黙って弾いていた
        var sender = new RecordingEmailSender();
        using var factory = CreateConfiguredFactory(sender);
        const string email = "unconfirmed@example.test";
        await CreateUnconfirmedUserAsync(factory, "forgot-unconfirmed", email);

        using var client = CreateNoRedirectClient(factory);
        var response = await PostForgotPasswordAsync(client, email);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var sent = Assert.Single(sender.Sent);
        Assert.Equal(email, sent.Email);
    }

    [Fact]
    public async Task 登録されていないアドレスは同じ確認画面へ行きメールを送らない()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateConfiguredFactory(sender);
        using var client = CreateNoRedirectClient(factory);

        var response = await PostForgotPasswordAsync(client, "missing@example.test");

        // アカウントの有無を画面から読み取れないよう、存在する場合と同じ確認画面へ返す
        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", response.Headers.Location?.ToString());
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task 送信が失敗しても登録されていない場合と同じ確認画面へ行く()
    {
        // 未登録アドレスは必ず確認画面へ 302 を返す。その裏で送信が失敗した登録済み
        // アドレスだけ 200 に戻ると、応答の差からアカウントの有無が読み取れてしまう。
        // 失敗時も同じ 302 を返すことで、この違いを出さない
        var sender = new ThrowingEmailSender();
        using var factory = CreateConfiguredFactory(sender);
        const string email = "send-failure@example.test";
        await CreateUnconfirmedUserAsync(factory, "forgot-send-failure", email);

        using var client = CreateNoRedirectClient(factory);
        var response = await PostForgotPasswordAsync(client, email);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Identity/Account/ForgotPasswordConfirmation", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task 送るメールの件名と本文は日本語で再設定へのリンクを含む()
    {
        var sender = new RecordingEmailSender();
        using var factory = CreateConfiguredFactory(sender);
        const string email = "ja-body@example.test";
        await CreateUnconfirmedUserAsync(factory, "forgot-ja-body", email);
        using var client = CreateNoRedirectClient(factory);

        await PostForgotPasswordAsync(client, email);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal("パスワード再設定のご案内", sent.Subject);
        Assert.Contains("パスワードの再設定が要求されました。", sent.HtmlMessage);
        Assert.Contains("パスワードを再設定する", sent.HtmlMessage);
        Assert.Contains("心当たりが無い場合は、このメールを破棄してください。", sent.HtmlMessage);
        Assert.Contains("/Identity/Account/ResetPassword", sent.HtmlMessage);
    }

    [Fact]
    public async Task メール未設定の再設定画面は案内を出してフォームを出さない()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/ForgotPassword"));

        Assert.Contains("この環境ではメールでの再設定が設定されていません", html);
        Assert.DoesNotContain("id=\"Input_Email\"", html);
    }

    [Fact]
    public async Task メール未設定では投稿されてもメールを送らない()
    {
        var sender = new RecordingEmailSender();
        using var factory = new ConAIWebApplicationFactory
        {
            ConfigureServices = services => services.AddSingleton<IEmailSender>(sender)
        };
        using var client = CreateNoRedirectClient(factory);

        // 未設定の再設定画面にはフォーム（= トークン）が無いので、ログイン画面から譲り受ける
        var loginHtml = await client.GetStringAsync("/Identity/Account/Login");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(loginHtml);
        var response = await client.PostAsync("/Identity/Account/ForgotPassword",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = "anyone@example.test",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task 確認画面は条件付きの送信済み案内を出す()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/ForgotPasswordConfirmation"));

        // 存在しないアドレスでもこの画面へ来るため、送信を断定する文言にしない
        Assert.Contains("再設定用のリンクを送りました", html);
        Assert.DoesNotContain("この環境ではメール送信が設定されていないため", html);
    }
}
