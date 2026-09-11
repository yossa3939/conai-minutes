using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingDetailsPageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingDetailsPageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task 議事録がMarkdownとして描画される()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync("details-a", new Meeting { Title = "閲覧" }, CancellationToken.None);
            await service.MarkSucceededAsync(meeting.Id, "本文です", string.Empty, "## 決定事項\n\n<script>alert(1)</script>", CancellationToken.None);
            id = meeting.Id;
        }

        var client = _factory.CreateClientAs("details-a");
        // 動的出力の日本語は数値文字参照で出るため、文言のアサーションはデコードしてから比較する。
        // エスケープ結果の検証はデコードすると元に戻るため、ブラウザが受け取る生のレスポンスに対して行う。
        var raw = await client.GetStringAsync($"/Meetings/Details/{id}");
        var html = WebUtility.HtmlDecode(raw);

        Assert.Contains("<h2", html);
        Assert.Contains("決定事項", html);
        Assert.Contains("本文です", html);
        Assert.DoesNotContain("<script>alert(1)</script>", raw);
    }

    [Fact]
    public async Task 他人の閲覧ページは404になる()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            id = (await service.CreateAsync("details-a", new Meeting { Title = "閲覧" }, CancellationToken.None)).Id;
        }

        var client = _factory.CreateClientAs("details-b");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Meetings/Details/{id}")).StatusCode);
    }

    private const string SlackUrl =
        "https://hooks.slack.com/services/T00000000/B00000000/detailstest0000000000aa";

    private async Task<Guid> MeetingAsync(string ownerId, string minutes)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await service.CreateAsync(ownerId, new Meeting { Title = "通知" }, CancellationToken.None);

        if (minutes.Length > 0)
        {
            await service.MarkSucceededAsync(
                meeting.Id, "本文です", string.Empty, minutes, CancellationToken.None);
        }

        return meeting.Id;
    }

    private async Task EndpointAsync(string ownerId, bool enabled)
    {
        using var scope = _factory.CreateScope();
        var endpoints = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var (result, _) = await endpoints.CreateAsync(
            ownerId,
            new WebhookEndpointEdit("通知先", WebhookKind.Slack, SlackUrl, true, true, enabled),
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);
    }

    [Fact]
    public async Task 議事録と有効な宛先があると通知を送るボタンが出る()
    {
        const string owner = "details-notify-on";
        var id = await MeetingAsync(owner, "## 決定事項\n\n続ける");
        await EndpointAsync(owner, enabled: true);

        var client = _factory.CreateClientAs(owner);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Details/{id}"));

        Assert.Contains("通知を送る", html);
        Assert.Contains($"/api/meetings/{id}/notify", html);
    }

    [Fact]
    public async Task 議事録が空なら通知を送るボタンは出ない()
    {
        const string owner = "details-notify-nominutes";
        var id = await MeetingAsync(owner, string.Empty);
        await EndpointAsync(owner, enabled: true);

        var client = _factory.CreateClientAs(owner);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Details/{id}"));

        Assert.DoesNotContain("通知を送る", html);
    }

    [Fact]
    public async Task 宛先が無効なら通知を送るボタンは出ない()
    {
        const string owner = "details-notify-disabled";
        var id = await MeetingAsync(owner, "## 決定事項\n\n続ける");
        await EndpointAsync(owner, enabled: false);

        var client = _factory.CreateClientAs(owner);
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Details/{id}"));

        Assert.DoesNotContain("通知を送る", html);
    }
}
