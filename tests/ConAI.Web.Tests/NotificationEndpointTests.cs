using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class NotificationEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string SlackUrl = "https://hooks.slack.com/services/T00000000/B00000000/pQ7x";

    private readonly ConAIWebApplicationFactory _factory;

    public NotificationEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Guid> AddMeetingAsync(string ownerId, string minutes)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var meeting = new Meeting { OwnerId = ownerId, Title = "通知", Minutes = minutes };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        return meeting.Id;
    }

    private async Task<Guid> AddEndpointAsync(string ownerId, string name = "Slack")
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var (result, _) = await service.CreateAsync(
            ownerId,
            new WebhookEndpointEdit(name, WebhookKind.Slack, SlackUrl, true, true, true),
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);

        var endpoints = await service.ListAsync(ownerId, CancellationToken.None);
        return endpoints.Single(e => e.Name == name).Id;
    }

    private async Task<HttpClient> ClientAsync(string ownerId) =>
        await _factory.CreateClientAs(ownerId).WithCsrfTokenAsync();

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("message").GetString() ?? string.Empty;
    }

    [Fact]
    public async Task 手動送信は受け付けて202を返す()
    {
        const string owner = "notify-api-a";
        await AddEndpointAsync(owner);
        var meetingId = await AddMeetingAsync(owner, "## 決定事項\n- やる");
        var client = await ClientAsync(owner);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/notify", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("送信を受け付けました。結果は通知設定の画面で確認できます。", await MessageAsync(response));
    }

    [Fact]
    public async Task 議事録が空なら手動送信を断る()
    {
        const string owner = "notify-api-b";
        await AddEndpointAsync(owner);
        var meetingId = await AddMeetingAsync(owner, string.Empty);
        var client = await ClientAsync(owner);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/notify", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("議事録がまだありません。生成してから送ってください。", await MessageAsync(response));
    }

    [Fact]
    public async Task 使える宛先が無ければ手動送信を断る()
    {
        const string owner = "notify-api-c";
        var meetingId = await AddMeetingAsync(owner, "## 決定事項");
        var client = await ClientAsync(owner);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/notify", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("送り先がありません。通知の設定で宛先を登録してください。", await MessageAsync(response));
    }

    [Fact]
    public async Task 他人の会議は手動送信できない()
    {
        const string owner = "notify-api-d";
        await AddEndpointAsync("notify-api-e");
        var meetingId = await AddMeetingAsync(owner, "## 決定事項");
        var client = await ClientAsync("notify-api-e");

        var response = await client.PostAsync($"/api/meetings/{meetingId}/notify", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task トークンなしの手動送信は400になる()
    {
        const string owner = "notify-api-f";
        await AddEndpointAsync(owner);
        var meetingId = await AddMeetingAsync(owner, "## 決定事項");
        var client = _factory.CreateClientAs(owner);

        var response = await client.PostAsync($"/api/meetings/{meetingId}/notify", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task テスト送信は結果を返す()
    {
        const string owner = "notify-api-g";
        var endpointId = await AddEndpointAsync(owner);
        var client = await ClientAsync(owner);

        var response = await client.PostAsync($"/api/notifications/{endpointId}/test", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal(
            "テスト通知を送りました。チャット側で届いているか確認してください。",
            document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task 他人の宛先へはテスト送信できない()
    {
        var endpointId = await AddEndpointAsync("notify-api-h");
        var client = await ClientAsync("notify-api-i");

        var response = await client.PostAsync($"/api/notifications/{endpointId}/test", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 通知APIは1分あたり5回を超えると429になる()
    {
        const string owner = "notify-api-j";
        await AddEndpointAsync(owner);
        var meetingId = await AddMeetingAsync(owner, "## 決定事項");
        var client = await ClientAsync(owner);
        var url = $"/api/meetings/{meetingId}/notify";

        for (var i = 0; i < 5; i++)
        {
            var ok = await client.PostAsync(url, null);
            Assert.Equal(HttpStatusCode.Accepted, ok.StatusCode);
        }

        var limited = await client.PostAsync(url, null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }
}
