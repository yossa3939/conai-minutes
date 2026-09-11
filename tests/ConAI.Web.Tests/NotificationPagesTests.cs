using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class NotificationPagesTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string SlackUrl =
        "https://hooks.slack.com/services/T00000000/B00000000/pagetest00000000000000aa";
    private const string DiscordUrl =
        "https://discord.com/api/webhooks/100000000000000000/pagetest00000000000000bb";

    private readonly ConAIWebApplicationFactory _factory;

    public NotificationPagesTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<WebhookEndpoint> CreateAsync(string ownerId, string name, WebhookKind kind, string url)
    {
        using var scope = _factory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        var (result, error) = await endpoints.CreateAsync(
            ownerId,
            new WebhookEndpointEdit(name, kind, url, true, true, true),
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);
        Assert.Null(error);

        var list = await endpoints.ListAsync(ownerId, CancellationToken.None);
        return list.Single(e => e.Name == name);
    }

    private async Task<WebhookEndpoint?> ReloadAsync(Guid id, string ownerId)
    {
        using var scope = _factory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        return await endpoints.GetAsync(id, ownerId, CancellationToken.None);
    }

    private async Task<IReadOnlyList<WebhookEndpoint>> ListAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        return await endpoints.ListAsync(ownerId, CancellationToken.None);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        string token,
        Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    private static Dictionary<string, string> Form(string name, WebhookKind kind, string url) => new()
    {
        ["Input.Name"] = name,
        ["Input.Kind"] = kind.ToString(),
        ["Input.Url"] = url,
        ["Input.NotifyOnSuccess"] = "true",
        ["Input.NotifyOnFailure"] = "true",
        ["Input.IsEnabled"] = "true"
    };

    [Fact]
    public async Task 匿名では一覧にアクセスできない()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task 一覧と作成と編集と削除が200を返す()
    {
        var endpoint = await CreateAsync("notify-page-ok", "開発チャンネル", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-ok");

        foreach (var path in new[]
        {
            "/Notifications",
            "/Notifications/Create",
            $"/Notifications/Edit/{endpoint.Id}",
            $"/Notifications/Delete/{endpoint.Id}"
        })
        {
            var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task 一覧には自分の宛先だけが並びマスクした断片が出る()
    {
        var others = await CreateAsync("notify-page-other", "他人の部屋", WebhookKind.Discord, DiscordUrl);
        var mine = await CreateAsync("notify-page-list", "うちの部屋", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-list");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Notifications"));

        Assert.Contains($"data-endpoint-id=\"{mine.Id}\"", html);
        Assert.DoesNotContain($"data-endpoint-id=\"{others.Id}\"", html);
        Assert.Contains("Slack", html);
        Assert.Contains(mine.UrlHint, html);
        // 平文の URL は画面に出さない
        Assert.DoesNotContain(SlackUrl, html);
    }

    [Fact]
    public async Task 失敗した宛先には理由と無効の表示が並ぶ()
    {
        var endpoint = await CreateAsync("notify-page-failed", "壊れた部屋", WebhookKind.Slack, SlackUrl);

        using (var scope = _factory.CreateScope())
        {
            var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
            await endpoints.MarkFailedAsync(
                endpoint.Id, "宛先が見つかりません。作り直してください。", disable: true, CancellationToken.None);
        }

        var client = _factory.CreateClientAs("notify-page-failed");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Notifications"));

        Assert.Contains("宛先が見つかりません。作り直してください。", html);
        Assert.Contains("無効", html);
    }

    [Fact]
    public async Task ヘッダーに通知へのリンクが出る()
    {
        var client = _factory.CreateClientAs("notify-page-nav");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings"));

        Assert.Contains("/Notifications", html);
        Assert.Contains(">通知</a>", html);
    }

    [Fact]
    public async Task 他人の宛先は編集も削除もできない()
    {
        var endpoint = await CreateAsync("notify-page-owner", "他人の部屋", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-intruder");

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/Notifications/Edit/{endpoint.Id}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.GetAsync($"/Notifications/Delete/{endpoint.Id}")).StatusCode);

        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync("/Notifications/Create"));
        var posted = await PostAsync(
            client, $"/Notifications/Delete/{endpoint.Id}", token, []);

        Assert.Equal(HttpStatusCode.NotFound, posted.StatusCode);
        Assert.NotNull(await ReloadAsync(endpoint.Id, "notify-page-owner"));
    }

    [Fact]
    public async Task 上限を超えて作ろうとすると検証エラーになる()
    {
        const string owner = "notify-page-limit";
        for (var i = 0; i < 10; i++)
        {
            await CreateAsync(
                owner,
                $"部屋 {i}",
                WebhookKind.Slack,
                $"https://hooks.slack.com/services/T00000000/B00000000/limit0000000000000000{i:D2}");
        }

        var client = _factory.CreateClientAs(owner);
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync("/Notifications/Create"));

        var response = await PostAsync(
            client,
            "/Notifications/Create",
            token,
            Form("あふれる部屋", WebhookKind.Slack,
                "https://hooks.slack.com/services/T00000000/B00000000/limit000000000000000099"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("10 件までです", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        Assert.Equal(10, (await ListAsync(owner)).Count);
    }

    [Fact]
    public async Task 種別に合わないURLの検証エラーには期待する形の例が出る()
    {
        var client = _factory.CreateClientAs("notify-page-invalid");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync("/Notifications/Create"));

        var response = await PostAsync(
            client, "/Notifications/Create", token, Form("違う宛先", WebhookKind.Slack, DiscordUrl));

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(WebhookKinds.UrlExample(WebhookKind.Slack), html);
        Assert.Empty(await ListAsync("notify-page-invalid"));
    }

    [Fact]
    public async Task 検証エラーで戻った画面に入力したURLを書き戻さない()
    {
        // URL は投稿権限そのもの。送り返した HTML に残ると、履歴や自動補完へ広がる
        const string url = "https://discord.com/api/webhooks/100000000000000000/echoback0000000000000cc";
        var client = _factory.CreateClientAs("notify-page-echo");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync("/Notifications/Create"));

        var response = await PostAsync(
            client, "/Notifications/Create", token, Form("書き戻し", WebhookKind.Slack, url));

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("echoback0000000000000cc", html);
    }

    [Fact]
    public async Task 同じ名前の宛先を2件登録できる()
    {
        const string owner = "notify-page-samename";
        var client = _factory.CreateClientAs(owner);

        foreach (var url in new[]
        {
            "https://hooks.slack.com/services/T00000000/B00000000/samename0000000000000001",
            "https://hooks.slack.com/services/T00000000/B00000000/samename0000000000000002"
        })
        {
            var token = HtmlTestHelpers.ExtractAntiforgeryToken(
                await client.GetStringAsync("/Notifications/Create"));
            var response = await PostAsync(
                client, "/Notifications/Create", token, Form("同じ札", WebhookKind.Slack, url));

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        Assert.Equal(2, (await ListAsync(owner)).Count(e => e.Name == "同じ札"));
    }

    [Fact]
    public async Task 編集でURL欄が空なら既存のURLが残る()
    {
        var endpoint = await CreateAsync("notify-page-keepurl", "据え置き", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-keepurl");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync($"/Notifications/Edit/{endpoint.Id}"));

        var response = await PostAsync(
            client,
            $"/Notifications/Edit/{endpoint.Id}",
            token,
            Form("名前だけ変えた", WebhookKind.Slack, string.Empty));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var saved = await ReloadAsync(endpoint.Id, "notify-page-keepurl");
        Assert.NotNull(saved);
        Assert.Equal("名前だけ変えた", saved.Name);
        Assert.Equal(endpoint.ProtectedUrl, saved.ProtectedUrl);
        Assert.Equal(endpoint.UrlHint, saved.UrlHint);
    }

    [Fact]
    public async Task 編集で種別を変えても保存されない()
    {
        var endpoint = await CreateAsync("notify-page-kind", "種別据え置き", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-kind");
        var html = await client.GetStringAsync($"/Notifications/Edit/{endpoint.Id}");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(html);

        // 編集画面に種別の入力欄は無い。それでも直接送れば束縛はされるので、無視されることを確かめる
        Assert.DoesNotContain("id=\"Input_Kind\"", html);

        var response = await PostAsync(
            client,
            $"/Notifications/Edit/{endpoint.Id}",
            token,
            Form("種別据え置き", WebhookKind.Discord, string.Empty));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var saved = await ReloadAsync(endpoint.Id, "notify-page-kind");
        Assert.NotNull(saved);
        Assert.Equal(WebhookKind.Slack, saved.Kind);
    }

    [Fact]
    public async Task 削除すると一覧から消える()
    {
        var endpoint = await CreateAsync("notify-page-delete", "消す部屋", WebhookKind.Slack, SlackUrl);
        var client = _factory.CreateClientAs("notify-page-delete");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(
            await client.GetStringAsync($"/Notifications/Delete/{endpoint.Id}"));

        var response = await PostAsync(client, $"/Notifications/Delete/{endpoint.Id}", token, []);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Empty(await ListAsync("notify-page-delete"));
    }

    [Fact]
    public async Task Antiforgeryなしの登録は400になる()
    {
        var client = _factory.CreateClientAs("notify-page-csrf");

        var response = await client.PostAsync(
            "/Notifications/Create",
            new FormUrlEncodedContent(Form("トークン無し", WebhookKind.Slack, SlackUrl)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await ListAsync("notify-page-csrf"));
    }
}
