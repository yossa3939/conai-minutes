using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConAI.Web.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class LiveEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string Origin = "http://localhost";
    private const int UtteranceBytes = 32000;

    private readonly ConAIWebApplicationFactory _factory;

    public LiveEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task WebSocket_要求でなければ_400_になる()
    {
        var meetingId = await CreateMeetingAsync("live-a");
        using var client = _factory.CreateClientAs("live-a");

        var response = await client.GetAsync($"/api/live/transcribe?meetingId={meetingId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 未認証なら_401_になる()
    {
        var meetingId = await CreateMeetingAsync("live-b");
        using var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/live/transcribe?meetingId={meetingId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Origin_が無ければ_403_になる()
    {
        var meetingId = await CreateMeetingAsync("live-c");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectAsync("live-c", meetingId, origin: null));

        Assert.Equal(403, StatusCodeOf(error));
    }

    [Fact]
    public async Task Origin_が一致しなければ_403_になる()
    {
        var meetingId = await CreateMeetingAsync("live-d");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectAsync("live-d", meetingId, origin: "http://evil.example"));

        Assert.Equal(403, StatusCodeOf(error));
    }

    [Fact]
    public async Task 他人の会議なら_404_になる()
    {
        var meetingId = await CreateMeetingAsync("live-e-owner");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectAsync("live-e-other", meetingId, Origin));

        Assert.Equal(404, StatusCodeOf(error));
    }

    [Fact]
    public async Task 同じ利用者が二重に接続すると_409_になる()
    {
        var meetingId = await CreateMeetingAsync("live-f");
        using var first = await ConnectAsync("live-f", meetingId, Origin);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ConnectAsync("live-f", meetingId, Origin));

        Assert.Equal(409, StatusCodeOf(error));

        await first.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
    }

    [Fact]
    public async Task 音声を送ると文字起こしが返り_切断で会議に保存される()
    {
        var meetingId = await CreateMeetingAsync("live-g");
        using var socket = await ConnectAsync("live-g", meetingId, Origin);

        await SendSilenceAsync(socket, UtteranceBytes);
        var transcript = await ReadUntilAsync(socket, "Transcript");

        Assert.Equal("テスト文字起こし1。", transcript);

        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);

        var saved = await WaitForTranscriptionAsync(meetingId);
        Assert.Contains("テスト文字起こし1。", saved);
    }

    private async Task<WebSocket> ConnectAsync(string userId, Guid meetingId, string? origin)
    {
        var client = _factory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request =>
        {
            request.Headers.Append(TestAuthHandler.UserHeader, userId);
            if (origin is not null)
            {
                request.Headers.Append("Origin", origin);
            }
        };

        var uri = new Uri(_factory.Server.BaseAddress, $"/api/live/transcribe?meetingId={meetingId}");
        return await client.ConnectAsync(uri, CancellationToken.None);
    }

    private static int StatusCodeOf(InvalidOperationException exception)
    {
        var match = Regex.Match(exception.Message, @"status code:\s*(\d+)");
        Assert.True(match.Success, exception.Message);
        return int.Parse(match.Groups[1].Value);
    }

    private static async Task SendSilenceAsync(WebSocket socket, int totalBytes)
    {
        var chunk = new byte[1024];
        for (var sent = 0; sent < totalBytes; sent += chunk.Length)
        {
            await socket.SendAsync(chunk, WebSocketMessageType.Binary, endOfMessage: true, CancellationToken.None);
        }
    }

    private static async Task<string> ReadUntilAsync(WebSocket socket, string type)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[8 * 1024];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token);
            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var element = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, result.Count)).RootElement;
            if (element.GetProperty("type").GetString() == type)
            {
                return element.GetProperty("text").GetString()!;
            }
        }
    }

    private async Task<Guid> CreateMeetingAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = "Live 検証",
            LiveMode = true
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();
        return meeting.Id;
    }

    private async Task<string> WaitForTranscriptionAsync(Guid meetingId)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var scope = _factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var meeting = await db.Meetings.FindAsync(meetingId);
            if (!string.IsNullOrEmpty(meeting?.Transcription))
            {
                return meeting.Transcription;
            }

            await Task.Delay(100);
        }

        Assert.Fail("文字起こしが保存されませんでした。");
        return string.Empty;
    }
}
