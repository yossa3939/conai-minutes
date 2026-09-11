using System.Net;

namespace ConAI.Web.Tests;

public class IdentityAuthPageDesignTests
{
    [Fact]
    public async Task ログインの画面が日本語の部品クラスで組まれている()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Identity/Account/Login");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains("メールアドレス", html);
        Assert.Contains("ログイン状態を保持する", html);
        Assert.Contains("id=\"Input_Email\"", html);
        Assert.Contains("id=\"Input_Password\"", html);
        Assert.Contains("id=\"login-submit\"", html);
        Assert.Contains("input-field", html);
        Assert.Contains("form-label", html);
        Assert.Contains("btn-primary", html);

        Assert.DoesNotContain("Log in", html);
        Assert.DoesNotContain("Remember me", html);
        Assert.DoesNotContain("Forgot your password", html);
        Assert.DoesNotContain("Use another service", html);
        Assert.DoesNotContain("form-floating", html);
        Assert.DoesNotContain("form-control", html);
        Assert.DoesNotContain("text-danger", html);
    }

    [Fact]
    public async Task ログアウトとロックアウトとアクセス拒否の画面が日本語になっている()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var logout = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/Logout"));
        Assert.Contains("ログアウトしました。", logout);
        Assert.DoesNotContain("successfully logged out", logout);

        var lockout = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/Lockout"));
        Assert.Contains("このアカウントはロックされています。", lockout);
        Assert.Contains("alert-destructive", lockout);
        Assert.DoesNotContain("locked out", lockout);
        Assert.DoesNotContain("text-danger", lockout);

        var denied = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/AccessDenied"));
        Assert.Contains("このページを開く権限がありません。", denied);
        Assert.DoesNotContain("do not have access", denied);
        Assert.DoesNotContain("text-danger", denied);
    }
}
