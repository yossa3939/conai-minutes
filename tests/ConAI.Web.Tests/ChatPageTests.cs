using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class ChatPageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public ChatPageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task AddTurnAsync(
        string ownerId,
        string question,
        string answer,
        DateTime createdAt,
        params (Guid? MeetingId, string Title)[] sources)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var turn = new ChatTurn
        {
            OwnerId = ownerId,
            Question = question,
            Answer = answer,
            CreatedAt = createdAt
        };

        for (var i = 0; i < sources.Length; i++)
        {
            turn.Sources.Add(new ChatTurnSource
            {
                MeetingId = sources[i].MeetingId,
                MeetingTitle = sources[i].Title,
                Order = i
            });
        }

        db.ChatTurns.Add(turn);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> AddMeetingAsync(string ownerId, string title)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting { OwnerId = ownerId, Title = title, Minutes = "## 決定事項\n- 何か" };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        return meeting.Id;
    }

    [Fact]
    public async Task 未認証では開けない()
    {
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync("/Chat");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ナビに議事録を探すが出る()
    {
        using var client = _factory.CreateClientAs("chat-page-nav");

        // 導線はレイアウトに置く。検索の画面ではなく一覧で見て、ナビに出ていることを確かめる。
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings"));

        Assert.Contains("議事録を探す", html);
        Assert.Contains("href=\"/Chat\"", html);
    }

    [Fact]
    public async Task 往復が1件も無いときも画面が開く()
    {
        using var client = _factory.CreateClientAs("chat-page-empty");

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Chat"));

        Assert.Contains("まだ会話はありません。", html);
        Assert.Contains("id=\"chat-input\"", html);
        Assert.Contains("id=\"chat-send\"", html);
        Assert.Contains("id=\"chat-clear\"", html);
        Assert.Contains("id=\"search-results\"", html);
        Assert.Contains("id=\"search-answer\"", html);
        Assert.Contains("会議名、議事録、文字起こしから探します。", html);
        // 結果が増えたことは aria-live で読み上げに伝える。属性が落ちると、成功したときだけ黙る。
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains("aria-busy=\"false\"", html);
    }

    [Fact]
    public async Task 保存済みの往復が古い順に描かれる()
    {
        await AddTurnAsync(
            "chat-page-order", "先に聞いたこと", "先の答え",
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc));
        await AddTurnAsync(
            "chat-page-order", "後で聞いたこと", "後の答え",
            new DateTime(2026, 9, 3, 2, 0, 0, DateTimeKind.Utc));

        using var client = _factory.CreateClientAs("chat-page-order");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Chat"));

        Assert.True(html.IndexOf("先に聞いたこと", StringComparison.Ordinal)
            < html.IndexOf("後で聞いたこと", StringComparison.Ordinal));
    }

    [Fact]
    public async Task 答えがMarkdownとしてHTMLになる()
    {
        await AddTurnAsync(
            "chat-page-markdown", "質問", "## 決定事項\n\n<script>alert(1)</script>",
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc));

        using var client = _factory.CreateClientAs("chat-page-markdown");
        var raw = await client.GetStringAsync("/Chat");
        var html = WebUtility.HtmlDecode(raw);

        Assert.Contains("<h2", html);
        Assert.Contains("決定事項", html);
        Assert.DoesNotContain("<script>alert(1)</script>", raw);
    }

    [Fact]
    public async Task 質問はそのまま出力せず_HTML_として解釈させない()
    {
        await AddTurnAsync(
            "chat-page-escape", "<script>alert(1)</script>", "答え",
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc));

        using var client = _factory.CreateClientAs("chat-page-escape");
        var raw = await client.GetStringAsync("/Chat");

        Assert.DoesNotContain("<script>alert(1)</script>", raw);
        Assert.Contains("&lt;script&gt;", raw);
    }

    [Fact]
    public async Task 根拠に会議名とリンクが出る()
    {
        var meetingId = await AddMeetingAsync("chat-page-link", "予算会議");
        await AddTurnAsync(
            "chat-page-link", "質問", "答え",
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc),
            (meetingId, "予算会議"));

        using var client = _factory.CreateClientAs("chat-page-link");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Chat"));

        Assert.Contains($"/Meetings/Details/{meetingId}", html);
        Assert.Contains("予算会議", html);
    }

    [Fact]
    public async Task 削除済みの会議はリンクにならない()
    {
        await AddTurnAsync(
            "chat-page-dead", "質問", "答え",
            new DateTime(2026, 9, 3, 1, 0, 0, DateTimeKind.Utc),
            (null, "消えた会議"));

        using var client = _factory.CreateClientAs("chat-page-dead");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Chat"));

        Assert.Contains("消えた会議", html);
        Assert.DoesNotContain("/Meetings/Details/", html);
    }
}
