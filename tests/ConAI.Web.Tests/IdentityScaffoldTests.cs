using System.Net;

namespace ConAI.Web.Tests;

public class IdentityScaffoldTests
{
    private static readonly string[] ExpectedPages =
    [
        "Account/AccessDenied.cshtml",
        "Account/ConfirmEmail.cshtml",
        "Account/ConfirmEmailChange.cshtml",
        "Account/ExternalLogin.cshtml",
        "Account/ForgotPassword.cshtml",
        "Account/ForgotPasswordConfirmation.cshtml",
        "Account/Lockout.cshtml",
        "Account/Login.cshtml",
        "Account/LoginWith2fa.cshtml",
        "Account/LoginWithRecoveryCode.cshtml",
        "Account/Logout.cshtml",
        "Account/Register.cshtml",
        "Account/RegisterConfirmation.cshtml",
        "Account/ResendEmailConfirmation.cshtml",
        "Account/ResetPassword.cshtml",
        "Account/ResetPasswordConfirmation.cshtml",
        "Account/_StatusMessage.cshtml",
        "Account/Manage/ChangePassword.cshtml",
        "Account/Manage/DeletePersonalData.cshtml",
        "Account/Manage/Disable2fa.cshtml",
        "Account/Manage/DownloadPersonalData.cshtml",
        "Account/Manage/Email.cshtml",
        "Account/Manage/EnableAuthenticator.cshtml",
        "Account/Manage/ExternalLogins.cshtml",
        "Account/Manage/GenerateRecoveryCodes.cshtml",
        "Account/Manage/Index.cshtml",
        "Account/Manage/PersonalData.cshtml",
        "Account/Manage/ResetAuthenticator.cshtml",
        "Account/Manage/SetPassword.cshtml",
        "Account/Manage/ShowRecoveryCodes.cshtml",
        "Account/Manage/TwoFactorAuthentication.cshtml",
        "Account/Manage/ManageNavPages.cs",
        "Account/Manage/_Layout.cshtml",
        "Account/Manage/_ManageNav.cshtml",
        "Account/Manage/_StatusMessage.cshtml",
        "Account/Manage/_ViewStart.cshtml"
    ];

    [Fact]
    public void Identity_の画面がすべてプロジェクト内に生成されている()
    {
        var pagesRoot = Path.Combine(
            RepositoryRoot(), "src", "ConAI.Web", "Areas", "Identity", "Pages");

        var missing = ExpectedPages
            .Where(relative => !File.Exists(
                Path.Combine(pagesRoot, relative.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        Assert.Empty(missing);
    }

    [Fact]
    public async Task アカウント管理の画面が自分のアカウントで開ける()
    {
        using var factory = new ConAIWebApplicationFactory();
        await IdentityTestUsers.EnsureAsync(factory, "manage-a");
        using var client = factory.CreateClientAs("manage-a");

        var response = await client.GetAsync("/Identity/Account/Manage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ConAI.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
