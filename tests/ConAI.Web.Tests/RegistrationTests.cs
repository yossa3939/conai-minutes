using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class RegistrationTests
{
    [Fact]
    public async Task 自己登録が無効なら登録ページに到達できない()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ExtraSettings = new Dictionary<string, string?> { ["Auth:AllowSelfRegistration"] = "false" }
        };
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Identity/Account/Register");

        // 未認証リクエストはエンドポイント不一致でも FallbackPolicy に弾かれて 401 になる。
        // 200 が返らないことこそが「登録できない」ことの証明で、拒否の番号は問わない。
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task 自己登録が無効なら登録の_POST_も拒否される()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ExtraSettings = new Dictionary<string, string?> { ["Auth:AllowSelfRegistration"] = "false" }
        };
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.Email"] = "intruder@example.com",
                ["Input.Password"] = "Passw0rd!",
                ["Input.ConfirmPassword"] = "Passw0rd!"
            }));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed, HttpStatusCode.Unauthorized });
    }

    [Fact]
    public async Task 自己登録が無効なら画面に登録リンクが出ない()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ExtraSettings = new Dictionary<string, string?> { ["Auth:AllowSelfRegistration"] = "false" }
        };
        using var client = factory.CreateClient();

        var html = await client.GetStringAsync("/");

        Assert.DoesNotContain("/Identity/Account/Register", html);
    }

    [Fact]
    public async Task 自己登録が有効なら登録ページが開く()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/Identity/Account/Register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void 認証_Cookie_の寿命と_SameSite_が設定どおりになる()
    {
        using var factory = new ConAIWebApplicationFactory();

        var options = factory.Services
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        Assert.Equal(TimeSpan.FromHours(8), options.ExpireTimeSpan);
        Assert.True(options.SlidingExpiration);
        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
    }
}
