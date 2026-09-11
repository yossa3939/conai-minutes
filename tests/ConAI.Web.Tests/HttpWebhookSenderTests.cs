using System.Net;
using System.Text;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ConAI.Web.Tests;

public class HttpWebhookSenderTests
{
    private const string Url = "https://hooks.slack.com/services/T/B/pQ7x";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return _respond(request);
        }
    }

    /// <summary>いくらでも読める本文を装い、実際に読まれた量を数える。</summary>
    private sealed class CountingStream : Stream
    {
        private readonly long _length;
        private long _position;

        public CountingStream(long length) => _length = length;

        public long BytesRead => _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;
            if (remaining <= 0)
            {
                return 0;
            }

            var read = (int)Math.Min(count, remaining);
            Array.Fill(buffer, (byte)'a', offset, read);
            _position += read;
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private static HttpWebhookSender Create(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(handler),
            new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero)));

    private static HttpResponseMessage Respond(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task 成功はSucceededになりJSONをそのまま送る()
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.OK, "ok"));
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Slack, Url, """{"text":"やあ"}""", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Succeeded, result.Outcome);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("""{"text":"やあ"}""", handler.LastBody);
        Assert.Equal("application/json", handler.LastRequest.Content!.Headers.ContentType!.MediaType);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task 一時的な失敗はRetryableになる(HttpStatusCode status)
    {
        var sender = Create(new StubHandler(_ => Respond(status)));

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "拒否されました")]
    [InlineData(HttpStatusCode.Forbidden, "拒否されました")]
    [InlineData(HttpStatusCode.NotFound, "宛先が見つかりません")]
    [InlineData(HttpStatusCode.Gone, "宛先が見つかりません")]
    public async Task 失効はRevokedになり直し方を伝える(HttpStatusCode status, string expected)
    {
        var sender = Create(new StubHandler(_ => Respond(status)));

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Revoked, result.Outcome);
        Assert.Contains(expected, result.Message);
        // URL そのものは資格情報なので、記録に残る文言へ混ぜない
        Assert.DoesNotContain("pQ7x", result.Message);
    }

    [Fact]
    public async Task その他の4xxは恒久失敗として無効化しない()
    {
        var sender = Create(new StubHandler(_ => Respond(HttpStatusCode.BadRequest)));

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Permanent, result.Outcome);
        Assert.Equal(400, result.StatusCode);
    }

    [Fact]
    public async Task 転送は追いかけず恒久失敗にする()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Respond(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://internal.example.local/admin");
            return response;
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Permanent, result.Outcome);
        Assert.Equal(302, result.StatusCode);
    }

    [Fact]
    public async Task 接続できないときはRetryableになる()
    {
        var sender = Create(new StubHandler(_ => throw new HttpRequestException("dns")));

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
        Assert.Null(result.StatusCode);
    }

    [Fact]
    public async Task 応答待ちで打ち切られたときはRetryableになる()
    {
        var sender = Create(new StubHandler(_ => throw new TaskCanceledException("timeout")));

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
    }

    [Fact]
    public async Task 呼び出し側の中止はそのまま伝える()
    {
        var sender = Create(new StubHandler(_ => throw new TaskCanceledException("stopping")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(
            () => sender.SendAsync(WebhookKind.Slack, Url, "{}", cts.Token));
    }

    [Fact]
    public async Task HTTP429はヘッダーの待ち時間を読む()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(5), result.RetryAfter);
    }

    [Fact]
    public async Task HTTP429はヘッダーと本文の長いほうを採る()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests, """{"retry_after":12.5}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
            return response;
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Discord, Url, "{}", CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(12.5), result.RetryAfter);
    }

    [Fact]
    public async Task 待ち時間は60秒で頭打ちにする()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(600));
            return response;
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Slack, Url, "{}", CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(60), result.RetryAfter);
    }

    [Fact]
    public void 日付形式の待ち時間も読む()
    {
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(30));

        var wait = RetryAfterReader.Read(response, body: null, new FakeTimeProvider(now));

        Assert.Equal(TimeSpan.FromSeconds(30), wait);
    }

    [Fact]
    public void 過ぎた日付は待ち時間として扱わない()
    {
        var now = new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero);
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(now.AddSeconds(-30));

        Assert.Null(RetryAfterReader.Read(response, body: null, new FakeTimeProvider(now)));
    }

    [Theory]
    [InlineData("""{"retry_after":"5"}""")]
    [InlineData("""{"retry_after":null}""")]
    [InlineData("""{"retry_after":true}""")]
    [InlineData("""{"retry_after":{"seconds":5}}""")]
    public async Task 本文の待ち時間が数値でなくても落ちない(string body)
    {
        var handler = new StubHandler(_ => Respond(HttpStatusCode.TooManyRequests, body));
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Discord, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
        Assert.Null(result.RetryAfter);
    }

    [Fact]
    public async Task 本文が数値でなくてもヘッダーの待ち時間は生きる()
    {
        var handler = new StubHandler(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests, """{"retry_after":"5"}""");
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
            return response;
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.Discord, Url, "{}", CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(7), result.RetryAfter);
    }

    [Fact]
    public async Task 大きな本文は最後まで読み込まない()
    {
        // 許可リストは Teams のワークフロー（*.logic.azure.com）を通す。
        // 利用者が自分で用意した宛先が巨大な本文を返しても、ワーカーがそれを抱え込まないことを見る
        var stream = new CountingStream(64 * 1024 * 1024);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StreamContent(stream)
        });
        var sender = Create(handler);

        var result = await sender.SendAsync(WebhookKind.MicrosoftTeams, Url, "{}", CancellationToken.None);

        Assert.Equal(WebhookSendOutcome.Retryable, result.Outcome);
        Assert.Equal(500, result.StatusCode);
        Assert.True(stream.BytesRead < 1024 * 1024, $"読み込んだのは {stream.BytesRead} バイトだった");
    }

    [Fact]
    public async Task 偽の送信部品はURLを記録しない()
    {
        var sender = new FakeWebhookSender();

        await sender.SendAsync(WebhookKind.Slack, Url, """{"text":"やあ"}""", CancellationToken.None);

        var sent = Assert.Single(sender.Sent);
        Assert.Equal(WebhookKind.Slack, sent.Kind);
        Assert.Equal("""{"text":"やあ"}""", sent.Json);
    }
}
