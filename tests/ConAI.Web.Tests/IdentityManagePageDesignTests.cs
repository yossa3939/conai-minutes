using System.Net;

namespace ConAI.Web.Tests;

public class IdentityManagePageDesignTests
{
    private const string MailNotice =
        "この環境ではメール送信が設定されていないため、この操作は完了しません。管理者に連絡してください。";

    [Fact]
    public async Task アカウント管理の左メニューが日本語の4項目だけになっている()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "manage-nav");
        using var client = factory.CreateClientAs("manage-nav");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/Manage"));

        Assert.Contains("アカウント管理", html);
        Assert.Contains("プロフィール", html);
        Assert.Contains("パスワード", html);
        Assert.Contains("二要素認証", html);
        Assert.Contains("個人データ", html);
        Assert.Contains("class=\"manage-nav-link\"", html);
        Assert.Contains("manage-nav-link-active", html);

        Assert.DoesNotContain("/Identity/Account/Manage/Email", html);
        Assert.DoesNotContain("/Identity/Account/Manage/ExternalLogins", html);
        Assert.DoesNotContain("Manage your account", html);
        Assert.DoesNotContain("Change your account settings", html);
        Assert.DoesNotContain("nav-pills", html);
        Assert.DoesNotContain("col-md-3", html);
    }

    [Fact]
    public async Task プロフィールの画面が読み取り専用になっている()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "manage-profile");
        using var client = factory.CreateClientAs("manage-profile");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Identity/Account/Manage/Index"));

        Assert.Contains("メールアドレス", html);
        Assert.Contains("manage-profile@example.test", html);
        Assert.Contains("id=\"Username\"", html);
        Assert.Contains("readonly", html);

        Assert.DoesNotContain("電話番号", html);
        Assert.DoesNotContain("Input_PhoneNumber", html);
        Assert.DoesNotContain("update-profile-button", html);
        Assert.DoesNotContain("Phone number", html);
        Assert.DoesNotContain("form-floating", html);
    }

    [Fact]
    public async Task パスワードの画面が日本語の部品クラスで組まれている()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "manage-password");
        using var client = factory.CreateClientAs("manage-password");

        var html = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/ChangePassword"));

        Assert.Contains("現在のパスワード", html);
        Assert.Contains("新しいパスワード（確認）", html);
        Assert.Contains("id=\"Input_OldPassword\"", html);
        Assert.Contains("id=\"Input_NewPassword\"", html);
        Assert.Contains("id=\"Input_ConfirmPassword\"", html);
        Assert.Contains("input-field", html);
        Assert.Contains("btn-primary", html);

        Assert.DoesNotContain("Current password", html);
        Assert.DoesNotContain("form-control", html);
        Assert.DoesNotContain("text-danger", html);
    }

    [Fact]
    public async Task 到達できない操作が塞がれて固定の案内が出る()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "manage-blocked");
        using var client = factory.CreateClientAs("manage-blocked");

        var personal = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/PersonalData"));
        Assert.Contains("ダウンロード", personal);
        Assert.Contains("id=\"download-data-button\"", personal);
        Assert.DoesNotContain("id=\"delete\"", personal);
        Assert.DoesNotContain("/Identity/Account/Manage/DeletePersonalData", personal);

        var deleted = await client.GetAsync("/Identity/Account/Manage/DeletePersonalData");
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);

        var email = WebUtility.HtmlDecode(
            await client.GetStringAsync("/Identity/Account/Manage/Email"));
        Assert.Contains(MailNotice, email);
        Assert.DoesNotContain("Manage email", email);
    }
}
