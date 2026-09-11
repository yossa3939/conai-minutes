using System.Net;
using System.Net.Http.Json;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class RateLimitTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public RateLimitTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "制限" }, CancellationToken.None);
    }

    [Fact]
    public async Task ファイルAPIは1分あたり30回を超えると429になる()
    {
        var meeting = await CreateAsync("rate-a");
        var client = _factory.CreateClientAs("rate-a");
        var url = $"/api/meetings/{meeting.Id}/files/{Guid.NewGuid()}";

        for (var i = 0; i < 30; i++)
        {
            var ok = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.NotFound, ok.StatusCode);
        }

        var limited = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task 別の利用者は別枠で数える()
    {
        var meeting = await CreateAsync("rate-b");
        var busy = _factory.CreateClientAs("rate-b");
        var url = $"/api/meetings/{meeting.Id}/files/{Guid.NewGuid()}";

        for (var i = 0; i < 31; i++)
        {
            await busy.GetAsync(url);
        }

        var otherMeeting = await CreateAsync("rate-c");
        var other = _factory.CreateClientAs("rate-c");

        var response = await other.GetAsync($"/api/meetings/{otherMeeting.Id}/files/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 生成状況のポーリングは生成のレート制限を消費しない()
    {
        var meeting = await CreateAsync("rate-d");
        var client = _factory.CreateClientAs("rate-d");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 20; i++)
        {
            var response = await client.GetAsync($"/api/meetings/{meeting.Id}/generation");
            statuses.Add(response.StatusCode);
        }

        // ポーリング（3 秒間隔・20 回/分）が生成開始の枠（5 回/分）を食い潰さないことの保証。
        Assert.All(statuses, status => Assert.NotEqual(HttpStatusCode.TooManyRequests, status));
    }

    [Fact]
    public async Task 生成の開始は5回を超えると429になる()
    {
        var meeting = await CreateAsync("rate-e");
        var client = await _factory.CreateClientAs("rate-e").WithCsrfTokenAsync();

        for (var i = 0; i < 5; i++)
        {
            // 2 回目以降の応答は 202/409 が混ざり得るが、レート制限は結果に関わらず数える。
            await client.PostAsync($"/api/meetings/{meeting.Id}/generation", null);
        }

        var limited = await client.PostAsync($"/api/meetings/{meeting.Id}/generation", null);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task Live接続は10回を超えると429になる()
    {
        var meeting = await CreateAsync("rate-live");
        var client = _factory.CreateClientAs("rate-live");
        var url = $"/api/live/transcribe?meetingId={meeting.Id}";

        // WebSocket ハンドシェイクでない普通の GET は 400 になるが、接続試行として数える
        for (var i = 0; i < 10; i++)
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        var limited = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task チャットは10回を超えると429になる()
    {
        var client = await _factory.CreateClientAs("rate-chat").WithCsrfTokenAsync();

        // 議事録のある会議が無いので Gemini は呼ばれない。
        // 応答は 200（no-meetings）になるが、レート制限は結果に関わらず数える。
        for (var i = 0; i < 10; i++)
        {
            var response = await client.PostAsJsonAsync("/api/chat", new { question = "質問" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var limited = await client.PostAsJsonAsync("/api/chat", new { question = "質問" });

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    }

    [Fact]
    public async Task チャットの枠を使い切ってもLiveの枠は残る()
    {
        var meeting = await CreateAsync("rate-chat-partition");
        var client = await _factory.CreateClientAs("rate-chat-partition").WithCsrfTokenAsync();

        for (var i = 0; i < 11; i++)
        {
            await client.PostAsJsonAsync("/api/chat", new { question = "質問" });
        }

        // Live は WebSocket 要求でなければ 400 を返す。429 でなければ枠が分かれている。
        var live = await client.GetAsync($"/api/live/transcribe?meetingId={meeting.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, live.StatusCode);
    }
}
