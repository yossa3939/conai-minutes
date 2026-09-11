using System.Net;

namespace ConAI.Web.Tests;

public class IdentityRegisterPageDesignTests
{
    private const string MailNotice =
        "この環境ではメール送信が設定されていないため、この操作は完了しません。管理者に連絡してください。";

    [Fact]
    public async Task 登録の画面が日本語の部品クラスで組まれている()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Identity/Account/Register");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains("id=\"Input_Email\"", html);
        Assert.Contains("id=\"Input_Password\"", html);
        Assert.Contains("id=\"Input_ConfirmPassword\"", html);
        Assert.Contains("id=\"registerSubmit\"", html);
        Assert.Contains("メールアドレス", html);
        Assert.Contains("パスワード（確認）", html);
        Assert.Contains("input-field", html);
        Assert.Contains("btn-primary", html);

        Assert.DoesNotContain("Create a new account", html);
        Assert.DoesNotContain("Confirm password", html);
        Assert.DoesNotContain("form-floating", html);
        Assert.DoesNotContain("form-control", html);
        Assert.DoesNotContain("text-danger", html);
    }

    [Fact]
    public async Task メール送信に依存する画面が案内を出す()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "mail-notice");
        using var client = factory.CreateClient();

        var registered = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/RegisterConfirmation?email=mail-notice@example.test"));
        Assert.Contains(MailNotice, registered);
        Assert.DoesNotContain("Please check your email", registered);

        // 再設定は SMTP を設定すれば送れるため、確認画面は送信済みの案内を出す
        var forgotten = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/ForgotPasswordConfirmation"));
        Assert.Contains("再設定用のリンクを送りました", forgotten);
        Assert.DoesNotContain("Please check your email", forgotten);

        // このファクトリは Smtp を設定しないため、入力フォームではなく未設定の案内が出る
        var forgot = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/ForgotPassword"));
        Assert.Contains("パスワードの再設定", forgot);
        Assert.Contains("この環境ではメールでの再設定が設定されていません", forgot);
        Assert.DoesNotContain("Forgot your password", forgot);
        Assert.DoesNotContain("form-control", forgot);
    }
}
