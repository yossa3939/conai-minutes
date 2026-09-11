using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

/// <summary>
/// TestAuthHandler は X-Test-User の値からクレームを作るだけで、Identity のユーザー行は作らない。
/// UserManager.GetUserAsync を引く画面を開くテストは、先にこのヘルパーで同じ Id の行を作る。
/// </summary>
public static class IdentityTestUsers
{
    public const string DefaultPassword = "Conai-Test-1!";

    public static async Task<IdentityUser> EnsureAsync(
        ConAIWebApplicationFactory factory,
        string userId,
        string? email = null)
    {
        using var scope = factory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        var existing = await userManager.FindByIdAsync(userId);
        if (existing is not null)
        {
            return existing;
        }

        var address = email ?? $"{userId}@example.test";
        var user = new IdentityUser
        {
            Id = userId,
            UserName = address,
            Email = address,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, DefaultPassword);
        if (!result.Succeeded)
        {
            var reason = string.Join(" / ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"テスト用アカウントを作成できなかった: {reason}");
        }

        return user;
    }
}
