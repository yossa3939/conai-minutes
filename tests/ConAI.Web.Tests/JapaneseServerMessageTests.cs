using System.Net;
using System.Reflection;
using ConAI.Web.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class JapaneseServerMessageTests
{
    [Fact]
    public void エラー文言のすべてが日本語に差し替わっている()
    {
        var japaneseDescriber = new JapaneseIdentityErrorDescriber();
        var defaultDescriber = new IdentityErrorDescriber();

        var methods = typeof(IdentityErrorDescriber)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.ReturnType == typeof(IdentityError))
            .ToArray();

        Assert.NotEmpty(methods);

        foreach (var method in methods)
        {
            var arguments = method.GetParameters()
                .Select(p => p.ParameterType == typeof(int) ? (object)8 : "テスト値")
                .ToArray();

            var japanese = (IdentityError)method.Invoke(japaneseDescriber, arguments)!;
            var original = (IdentityError)method.Invoke(defaultDescriber, arguments)!;

            Assert.Equal(original.Code, japanese.Code);
            Assert.NotEqual(original.Description, japanese.Description);
            Assert.DoesNotMatch("[A-Za-z]{2,}", japanese.Description);
        }
    }

    [Fact]
    public void 日本語のエラー文言が_DI_に登録されている()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var scope = factory.Services.CreateScope();

        var describer = scope.ServiceProvider.GetRequiredService<IdentityErrorDescriber>();

        Assert.IsType<JapaneseIdentityErrorDescriber>(describer);
    }

    [Fact]
    public void カルチャが_ja_JP_に固定されている()
    {
        using var factory = new ConAIWebApplicationFactory();

        var options = factory.Services.GetRequiredService<IOptions<RequestLocalizationOptions>>().Value;

        Assert.Equal("ja-JP", options.DefaultRequestCulture.Culture.Name);
        Assert.Equal("ja-JP", options.DefaultRequestCulture.UICulture.Name);
        Assert.Equal(new[] { "ja-JP" }, options.SupportedCultures!.Select(c => c.Name));
        Assert.Equal(new[] { "ja-JP" }, options.SupportedUICultures!.Select(c => c.Name));
    }

    [Fact]
    public void モデルバインディングの文言がすべて日本語になっている()
    {
        using var factory = new ConAIWebApplicationFactory();

        var provider = factory.Services
            .GetRequiredService<IOptions<MvcOptions>>()
            .Value.ModelBindingMessageProvider;

        var messages = new[]
        {
            provider.MissingBindRequiredValueAccessor("項目"),
            provider.MissingKeyOrValueAccessor(),
            provider.MissingRequestBodyRequiredValueAccessor(),
            provider.ValueMustNotBeNullAccessor("値"),
            provider.AttemptedValueIsInvalidAccessor("値", "項目"),
            provider.NonPropertyAttemptedValueIsInvalidAccessor("値"),
            provider.UnknownValueIsInvalidAccessor("項目"),
            provider.NonPropertyUnknownValueIsInvalidAccessor(),
            provider.ValueIsInvalidAccessor("値"),
            provider.ValueMustBeANumberAccessor("項目"),
            provider.NonPropertyValueMustBeANumberAccessor()
        };

        Assert.All(messages, message => Assert.DoesNotMatch("[A-Za-z]{2,}", message));
    }

    [Fact]
    public async Task 弱いパスワードでの登録が日本語で拒否される()
    {
        using var factory = new ConAIWebApplicationFactory();
        using var client = factory.CreateClient();
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync("/Identity/Account/Register"));

        var response = await client.PostAsync("/Identity/Account/Register", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["Input.Email"] = "weak@example.test",
                ["Input.Password"] = "abcdefgh",
                ["Input.ConfirmPassword"] = "abcdefgh",
                ["__RequestVerificationToken"] = token
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("パスワードには数字を 1 文字以上含めてください。", html);
    }
}
