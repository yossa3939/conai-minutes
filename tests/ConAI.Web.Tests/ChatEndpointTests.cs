using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Search;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class ChatEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public ChatEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private sealed record SourceBody(
        [property: JsonPropertyName("meetingId")] Guid? MeetingId,
        [property: JsonPropertyName("title")] string Title);

    private sealed record TurnBody(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("question")] string Question,
        [property: JsonPropertyName("answerHtml")] string AnswerHtml,
        [property: JsonPropertyName("createdAtText")] string CreatedAtText,
        [property: JsonPropertyName("sources")] IReadOnlyList<SourceBody> Sources);

    private sealed record AskBody(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("turn")] TurnBody? Turn,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes);

    private FakeGeminiContentClient Fake => _factory.Services.GetRequiredService<FakeGeminiContentClient>();

    private static async Task<string?> MessageOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        return body?["message"];
    }

    private async Task<Guid> AddMeetingAsync(string ownerId, string title, string minutes)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = title,
            Minutes = minutes,
            UpdatedAt = DateTime.UtcNow
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        return meeting.Id;
    }

    [Fact]
    public async Task 未認証は401になる()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "質問" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CSRFトークンが無いと400になる()
    {
        using var client = _factory.CreateClientAs("chat-api-csrf");

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "質問" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task form形式のPOSTでも415にならず400になる()
    {
        // 最小 API の本文束縛はエンドポイントフィルタより先に動く。
        // [FromBody] で束縛すると、ここが 415 になって CSRF の 400 に届かない。
        using var client = await _factory.CreateClientAs("chat-api-form").WithCsrfTokenAsync();

        var response = await client.PostAsync(
            "/api/chat", new FormUrlEncodedContent(new Dictionary<string, string> { ["question"] = "質問" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("リクエストの本文を読み取れませんでした。", await MessageOf(response));
    }

    [Fact]
    public async Task 壊れたJSONは400になる()
    {
        using var client = await _factory.CreateClientAs("chat-api-broken").WithCsrfTokenAsync();

        var response = await client.PostAsync(
            "/api/chat",
            new StringContent("{ \"question\": \"切りかけ", Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("リクエストの本文を読み取れませんでした。", await MessageOf(response));
    }

    [Fact]
    public async Task 空白だけの質問は400になる()
    {
        using var client = await _factory.CreateClientAs("chat-api-empty").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("質問を入力してください。", await MessageOf(response));
    }

    [Fact]
    public async Task 長すぎる質問は400になる()
    {
        using var client = await _factory.CreateClientAs("chat-api-long").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            "/api/chat", new { question = new string('あ', ChatLimits.MaxQuestionChars + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("2,000", await MessageOf(response));
    }

    [Fact]
    public async Task 上限に達していたら400になる()
    {
        await AddMeetingAsync("chat-api-limit", "予算会議", "## 決定事項\n- 増額を承認した");

        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var baseTime = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);

            for (var i = 0; i < ChatLimits.MaxTurns; i++)
            {
                db.ChatTurns.Add(new ChatTurn
                {
                    OwnerId = "chat-api-limit",
                    Question = $"質問 {i}",
                    Answer = "答え",
                    CreatedAt = baseTime.AddSeconds(i)
                });
            }

            await db.SaveChangesAsync();
        }

        using var client = await _factory.CreateClientAs("chat-api-limit").WithCsrfTokenAsync();
        var response = await client.PostAsJsonAsync("/api/chat", new { question = "予算会議 はどうですか" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("200 往復", await MessageOf(response));
    }

    [Fact]
    public async Task 議事録が1件も無ければ200でturnはnullになる()
    {
        using var client = await _factory.CreateClientAs("chat-api-none").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "何かありましたか" });
        var body = await response.Content.ReadFromJsonAsync<AskBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-meetings", body?.Status);
        Assert.Null(body?.Turn);
    }

    [Fact]
    public async Task 該当が無ければ200でturnはnullになる()
    {
        await AddMeetingAsync("chat-api-nomatch", "予算会議", "## 決定事項\n- 増額を承認した");
        using var client = await _factory.CreateClientAs("chat-api-nomatch").WithCsrfTokenAsync();

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([]));
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("/api/chat", new { question = "昼食の話はどうなりましたか" });
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        var body = await response.Content.ReadFromJsonAsync<AskBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-match", body?.Status);
        Assert.Null(body?.Turn);
    }

    [Fact]
    public async Task 答えが返ると根拠つきの往復が返る()
    {
        var meetingId = await AddMeetingAsync("chat-api-ok", "予算会議", "## 決定事項\n- 増額を承認した");
        using var client = await _factory.CreateClientAs("chat-api-ok").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/chat", new { question = "予算会議 で何が決まりましたか" });
        var body = await response.Content.ReadFromJsonAsync<AskBody>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("answered", body?.Status);
        Assert.Equal("予算会議 で何が決まりましたか", body?.Turn?.Question);
        Assert.Contains("<p>", body?.Turn?.AnswerHtml);
        Assert.Matches(@"^\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}$", body?.Turn?.CreatedAtText);
        var source = Assert.Single(body!.Turn!.Sources);
        Assert.Equal(meetingId, source.MeetingId);
        Assert.Equal("予算会議", source.Title);
    }

    [Fact]
    public async Task 打ち切りの注記が返る()
    {
        await AddMeetingAsync("chat-api-note", "予算会議", "## 決定事項\n- 増額を承認した");
        for (var i = 0; i < ChatLimits.MaxCandidateMeetings; i++)
        {
            await AddMeetingAsync("chat-api-note", $"埋め草 {i}", "## 決定事項\n- なし");
        }

        using var client = await _factory.CreateClientAs("chat-api-note").WithCsrfTokenAsync();

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("/api/chat", new { question = "何が決まりましたか" });
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        var body = await response.Content.ReadFromJsonAsync<AskBody>();

        Assert.Contains("会議が多いため、新しいものから一部だけを対象に探しました。", body!.Notes);
    }

    [Fact]
    public async Task Geminiが落ちると502になる()
    {
        await AddMeetingAsync("chat-api-fail", "予算会議", "## 決定事項\n- 増額を承認した");
        using var client = await _factory.CreateClientAs("chat-api-fail").WithCsrfTokenAsync();

        Fake.SelectHandler = (_, _) => throw new InvalidOperationException("模擬の失敗");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("/api/chat", new { question = "何が決まりましたか" });
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal("答えを作れませんでした。しばらくしてからもう一度お試しください。", await MessageOf(response));
    }

    [Fact]
    public async Task 打ち切りのキャンセルは502のタイムアウトになる()
    {
        await AddMeetingAsync("chat-api-timeout", "予算会議", "## 決定事項\n- 増額を承認した");
        using var client = await _factory.CreateClientAs("chat-api-timeout").WithCsrfTokenAsync();

        // 60 秒の打ち切りが起きたときと同じ形の例外を投げる。
        // 呼び出し元のトークンは生きているので、502 のタイムアウトに落ちる。
        Fake.SelectHandler = (_, _) => throw new OperationCanceledException(new CancellationToken(true));
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("/api/chat", new { question = "何が決まりましたか" });
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(
            "答えが返るまでに時間がかかりすぎました。しばらくしてからもう一度お試しください。",
            await MessageOf(response));
    }

    [Fact]
    public async Task 往復が無くても会話を消すと204になる()
    {
        using var client = await _factory.CreateClientAs("chat-api-clear").WithCsrfTokenAsync();

        var response = await client.DeleteAsync("/api/chat");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task 会議IDを載せた質問が受け付けられる()
    {
        const string owner = "chat-api-ids";
        var meetingId = await AddMeetingAsync(owner, "予算検討会", "予算を 10% 減らすと決めた");

        using var client = await _factory.CreateClientAs(owner).WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            "/api/chat", new { question = "何が決まった？", meetingIds = new[] { meetingId } });

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task 会議IDが多すぎる質問は400を返す()
    {
        // 画面が送るのは上位 10 件までで、サーバもそこまでしか根拠にしない。
        // それを超える配列は、読み捨てる前に本文ごと受け取ってしまう。
        using var client = await _factory.CreateClientAs("chat-api-too-many-ids").WithCsrfTokenAsync();

        var meetingIds = Enumerable
            .Range(0, SearchLimits.MaxAnswerMeetings + 1)
            .Select(_ => Guid.NewGuid())
            .ToArray();

        var response = await client.PostAsJsonAsync(
            "/api/chat", new { question = "何が決まった？", meetingIds });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(
            SearchLimits.MaxAnswerMeetings.ToString(CultureInfo.InvariantCulture), await MessageOf(response));
    }
}
