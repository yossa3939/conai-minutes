using System.Net;

namespace ConAI.Web.Tests;

public class IdentityTwoFactorPageDesignTests
{
    [Fact]
    public async Task 二要素認証の画面が日本語で組まれている()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "twofactor-top");
        using var client = factory.CreateClientAs("twofactor-top");

        var top = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/TwoFactorAuthentication"));

        Assert.Contains("二要素認証", top);
        Assert.Contains("認証アプリ", top);
        Assert.Contains("id=\"enable-authenticator\"", top);
        Assert.DoesNotContain("Two-factor authentication", top);
        Assert.DoesNotContain("Add authenticator app", top);
        Assert.DoesNotContain("display: inline-block", top);
        Assert.DoesNotContain("btn btn-", top);

        var reset = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/ResetAuthenticator"));

        Assert.Contains("キーを作り直す", reset);
        Assert.Contains("btn-destructive", reset);
        Assert.DoesNotContain("Reset authenticator key", reset);
        Assert.DoesNotContain("alert-warning", reset);
    }

    [Fact]
    public async Task 認証アプリの設定画面がQRを使わずキーを文字で示す()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "twofactor-key");
        using var client = factory.CreateClientAs("twofactor-key");

        var html = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/EnableAuthenticator"));

        Assert.Contains("認証アプリに次のキーを入力してください", html);
        Assert.Contains("id=\"shared-key\"", html);
        Assert.Contains("id=\"Input_Code\"", html);
        Assert.Contains("認証コード", html);

        Assert.DoesNotContain("qrCode", html);
        Assert.DoesNotContain("qrcode.js", html);
        Assert.DoesNotContain("Scan the QR Code", html);
        Assert.DoesNotContain("form-floating", html);
        Assert.DoesNotContain("text-danger", html);
    }
}
