using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class ChatServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public ChatServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private FakeGeminiContentClient Fake => _factory.Services.GetRequiredService<FakeGeminiContentClient>();

    private async Task<Guid> AddMeetingAsync(
        string ownerId, string title, string minutes, DateTime? updatedAt = null)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = title,
            Minutes = minutes,
            UpdatedAt = updatedAt ?? DateTime.UtcNow
        };
        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        return meeting.Id;
    }

    private async Task AddTurnsAsync(string ownerId, int count, string answer = "答え")
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var baseTime = new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < count; i++)
        {
            db.ChatTurns.Add(new ChatTurn
            {
                OwnerId = ownerId,
                Question = $"質問 {i}",
                Answer = answer,
                CreatedAt = baseTime.AddSeconds(i)
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<ChatAskOutcome> AskAsync(string ownerId, string question)
    {
        using var scope = _factory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        return await chat.AskAsync(ownerId, question, [], CancellationToken.None);
    }

    private async Task<IReadOnlyList<ChatTurn>> ListAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        return await chat.ListAsync(ownerId, CancellationToken.None);
    }

    [Fact]
    public async Task 議事録のある会議が1件も無ければ選抜も回答も呼ばない()
    {
        var before = Fake.SelectCallCount;
        var beforeAsk = Fake.AskCallCount;

        var outcome = await AskAsync("chat-svc-none", "何かありましたか");

        Assert.Equal(ChatAskStatus.NoMeetings, outcome.Status);
        Assert.Null(outcome.Turn);
        Assert.Empty(outcome.Notes);
        Assert.Equal(before, Fake.SelectCallCount);
        Assert.Equal(beforeAsk, Fake.AskCallCount);
    }

    [Fact]
    public async Task 往復が上限に達していたら質問できない()
    {
        await AddMeetingAsync("chat-svc-limit", "予算会議", "## 決定事項\n- 増額を承認した");
        await AddTurnsAsync("chat-svc-limit", ChatLimits.MaxTurns);

        var outcome = await AskAsync("chat-svc-limit", "予算会議 はどうなりましたか");

        Assert.Equal(ChatAskStatus.LimitReached, outcome.Status);
        Assert.Null(outcome.Turn);
    }

    [Fact]
    public async Task 選抜が空なら答えを作らず保存もしない()
    {
        await AddMeetingAsync("chat-svc-nomatch", "予算会議", "## 決定事項\n- 増額を承認した");
        var beforeAsk = Fake.AskCallCount;

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([]));
        ChatAskOutcome outcome;
        try
        {
            outcome = await AskAsync("chat-svc-nomatch", "昼食の話はどうなりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        Assert.Equal(ChatAskStatus.NoMatch, outcome.Status);
        Assert.Null(outcome.Turn);
        Assert.Equal(beforeAsk, Fake.AskCallCount);
        Assert.Empty(await ListAsync("chat-svc-nomatch"));
    }

    [Fact]
    public async Task 答えは往復として保存される()
    {
        await AddMeetingAsync("chat-svc-save", "予算会議", "## 決定事項\n- 増額を承認した");

        var outcome = await AskAsync("chat-svc-save", "予算会議 で何が決まりましたか");

        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
        Assert.Contains("予算会議 で何が決まりましたか", outcome.Turn?.Answer);

        var turns = await ListAsync("chat-svc-save");

        Assert.Single(turns);
        Assert.Equal("予算会議 で何が決まりましたか", turns[0].Question);
    }

    [Fact]
    public async Task 根拠には会議名が写し取られる()
    {
        var meetingId = await AddMeetingAsync("chat-svc-source", "予算会議", "## 決定事項\n- 増額を承認した");

        await AskAsync("chat-svc-source", "予算会議 で何が決まりましたか");
        var turns = await ListAsync("chat-svc-source");

        var source = Assert.Single(turns[0].Sources);
        Assert.Equal(meetingId, source.MeetingId);
        Assert.Equal("予算会議", source.MeetingTitle);
        Assert.Equal(0, source.Order);
    }

    [Fact]
    public async Task 根拠は答えに使われた議事録だけに絞られる()
    {
        // 選抜の並びは更新の新しい順。UpdatedAt を決め打ちして、
        // 1 番＝部門定例、2 番＝予算会議になることを固定する。
        var budgetMeetingId = await AddMeetingAsync(
            "chat-svc-used", "予算会議", "## 決定事項\n- 増額を承認した",
            updatedAt: new DateTime(2026, 9, 4, 0, 0, 0, DateTimeKind.Utc));
        await AddMeetingAsync(
            "chat-svc-used", "部門定例", "## 決定事項\n- 様子を見る",
            updatedAt: new DateTime(2026, 9, 4, 1, 0, 0, DateTimeKind.Utc));

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1, 2]));
        // 2 段目は 2 番（予算会議）を使ったと答える。
        Fake.AskHandler = (_, _) => Task.FromResult(new ChatResult("答え", [2]));
        try
        {
            await AskAsync("chat-svc-used", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
            Fake.AskHandler = null;
        }

        var turns = await ListAsync("chat-svc-used");
        var source = Assert.Single(turns[0].Sources);

        Assert.Equal(budgetMeetingId, source.MeetingId);
        Assert.Equal("予算会議", source.MeetingTitle);
        Assert.Equal(0, source.Order);
    }

    [Fact]
    public async Task usedMeetingNumbersが空なら選抜の全件が根拠になる()
    {
        await AddMeetingAsync("chat-svc-empty-used", "予算会議", "## 決定事項\n- 増額を承認した");
        await AddMeetingAsync("chat-svc-empty-used", "部門定例", "## 決定事項\n- 様子を見る");

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1, 2]));
        Fake.AskHandler = (_, _) => Task.FromResult(new ChatResult("答え", []));
        try
        {
            await AskAsync("chat-svc-empty-used", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
            Fake.AskHandler = null;
        }

        var turns = await ListAsync("chat-svc-empty-used");

        Assert.Equal(2, turns[0].Sources.Count);
        Assert.Equal([0, 1], turns[0].Sources.Select(s => s.Order).ToArray());
    }

    [Fact]
    public async Task 範囲外のusedMeetingNumbersは捨てられる()
    {
        await AddMeetingAsync("chat-svc-out-of-range", "予算会議", "## 決定事項\n- 増額を承認した");

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        Fake.AskHandler = (_, _) => Task.FromResult(new ChatResult("答え", [0, 9, 1]));
        try
        {
            await AskAsync("chat-svc-out-of-range", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
            Fake.AskHandler = null;
        }

        var turns = await ListAsync("chat-svc-out-of-range");

        Assert.Single(turns[0].Sources);
    }

    [Fact]
    public async Task 候補を打ち切ったときは注記が付く()
    {
        await AddMeetingAsync("chat-svc-note", "予算会議", "## 決定事項\n- 増額を承認した");

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        ChatAskOutcome outcome;
        try
        {
            // 501 件目を足して候補の件数の上限を踏ませる。
            for (var i = 0; i < ChatLimits.MaxCandidateMeetings; i++)
            {
                await AddMeetingAsync("chat-svc-note", $"埋め草 {i}", "## 決定事項\n- なし");
            }

            outcome = await AskAsync("chat-svc-note", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
        Assert.Contains(ChatNotes.CandidatesTruncated, outcome.Notes);
    }

    [Fact]
    public async Task 根拠を積み切れないときは注記が付き議事録も落ちる()
    {
        var big = new string('あ', ChatLimits.MaxAnswerContextChars - 10);
        await AddMeetingAsync("chat-svc-drop", "大きい会議", big);
        await AddMeetingAsync("chat-svc-drop", "落ちる会議", new string('い', 100));

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1, 2]));
        Fake.AskHandler = (_, _) => Task.FromResult(new ChatResult("答え", []));
        ChatAskOutcome outcome;
        try
        {
            outcome = await AskAsync("chat-svc-drop", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
            Fake.AskHandler = null;
        }

        Assert.Contains(ChatNotes.SourcesDropped, outcome.Notes);
        Assert.Single(outcome.Turn!.Sources);
    }

    [Fact]
    public async Task 先頭の議事録は上限を超えていても必ず入る()
    {
        var huge = new string('あ', ChatLimits.MaxAnswerContextChars + 1_000);
        await AddMeetingAsync("chat-svc-huge", "巨大な会議", huge);

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        ChatAskOutcome outcome;
        try
        {
            outcome = await AskAsync("chat-svc-huge", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        // 1 件目まで落とすと、根拠ゼロで答えを作らせることになる。
        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
        Assert.Single(outcome.Turn!.Sources);
        Assert.DoesNotContain(ChatNotes.SourcesDropped, outcome.Notes);
    }

    [Fact]
    public async Task 選抜へ渡す直近の質問は3件だけになる()
    {
        await AddMeetingAsync("chat-svc-recent", "予算会議", "## 決定事項\n- 増額を承認した");
        await AddTurnsAsync("chat-svc-recent", 5);

        string? selectionPrompt = null;
        Fake.SelectHandler = (request, _) =>
        {
            selectionPrompt = request.Prompt;
            return Task.FromResult(new SelectionResult([1]));
        };
        try
        {
            await AskAsync("chat-svc-recent", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        Assert.NotNull(selectionPrompt);

        // 直近 3 件（質問 2〜4）だけが載り、それより古い質問は載らない。
        Assert.Contains("- 質問 2", selectionPrompt);
        Assert.Contains("- 質問 4", selectionPrompt);
        Assert.DoesNotContain("- 質問 1", selectionPrompt);
        Assert.DoesNotContain("- 質問 0", selectionPrompt);
    }

    [Fact]
    public async Task 履歴は直近10往復までしか渡らない()
    {
        await AddMeetingAsync("chat-svc-history", "予算会議", "## 決定事項\n- 増額を承認した");
        await AddTurnsAsync("chat-svc-history", 12);

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        try
        {
            await AskAsync("chat-svc-history", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        var prompt = Fake.LastChatRequest!.Prompt;

        Assert.DoesNotContain("質問 0", prompt);
        // 履歴は Environment.NewLine で連結される。LF だけの比較は Windows では絶対に現れず空振りする。
        Assert.DoesNotContain($"質問 1{Environment.NewLine}", prompt);
        Assert.Contains("質問 11", prompt);
    }

    [Fact]
    public async Task 履歴は上限文字数を超えると古いほうから落ちる()
    {
        await AddMeetingAsync("chat-svc-history-chars", "予算会議", "## 決定事項\n- 増額を承認した");
        // 1 往復あたり 8,000 文字。10 往復ぶんは 20,000 文字の枠に入らない。
        await AddTurnsAsync("chat-svc-history-chars", 10, answer: new string('答', 8_000));

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        try
        {
            await AskAsync("chat-svc-history-chars", "何が決まりましたか");
        }
        finally
        {
            Fake.SelectHandler = null;
        }

        var prompt = Fake.LastChatRequest!.Prompt;

        Assert.Contains("質問 9", prompt);
        Assert.DoesNotContain("質問 0", prompt);
    }

    [Fact]
    public async Task 空の答えは例外になり保存されない()
    {
        await AddMeetingAsync("chat-svc-blank", "予算会議", "## 決定事項\n- 増額を承認した");

        Fake.SelectHandler = (_, _) => Task.FromResult(new SelectionResult([1]));
        Fake.AskHandler = (_, _) => Task.FromResult(new ChatResult("   ", []));
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => AskAsync("chat-svc-blank", "何が決まりましたか"));
        }
        finally
        {
            Fake.SelectHandler = null;
            Fake.AskHandler = null;
        }

        Assert.Empty(await ListAsync("chat-svc-blank"));
    }

    [Fact]
    public async Task 会話を消すと自分の往復だけが消える()
    {
        await AddTurnsAsync("chat-svc-clear-a", 2);
        await AddTurnsAsync("chat-svc-clear-b", 1);

        using (var scope = _factory.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            await chat.ClearAsync("chat-svc-clear-a", CancellationToken.None);
        }

        // 消えたことは戻り値ではなく ListAsync で確かめる。ClearAsync は何も返さない。
        Assert.Empty(await ListAsync("chat-svc-clear-a"));
        Assert.Single(await ListAsync("chat-svc-clear-b"));
    }

    [Fact]
    public async Task 一覧は古い順に並ぶ()
    {
        await AddTurnsAsync("chat-svc-order", 3);

        var turns = await ListAsync("chat-svc-order");

        Assert.Equal(["質問 0", "質問 1", "質問 2"], turns.Select(t => t.Question).ToArray());
    }

    [Fact]
    public async Task 会議IDを渡すと選抜を呼ばずにその会議を根拠にする()
    {
        var owner = $"chat-ids-{Guid.NewGuid():N}";
        var meetingId = await AddMeetingAsync(owner, "予算検討会", "予算を 10% 減らすと決めた");

        using var scope = _factory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        var outcome = await chat.AskAsync(owner, "何が決まった？", [meetingId], CancellationToken.None);

        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
        Assert.Equal("予算検討会", outcome.Turn!.Sources[0].MeetingTitle);
    }

    [Fact]
    public async Task 他人の会議IDを混ぜても根拠にならない()
    {
        // 要求の本文は利用者が自由に組み立てられる。検索の応答を経由せず直接届く経路である。
        var mine = $"chat-ids-mine-{Guid.NewGuid():N}";
        var yours = $"chat-ids-yours-{Guid.NewGuid():N}";

        var ours = await AddMeetingAsync(mine, "自分の会議", "自分の議事録");
        var theirs = await AddMeetingAsync(yours, "他人の会議", "他人の議事録");

        using var scope = _factory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        var outcome = await chat.AskAsync(
            mine, "何が決まった？", [ours, theirs], CancellationToken.None);

        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
        Assert.All(outcome.Turn!.Sources, source => Assert.NotEqual(theirs, source.MeetingId));
    }

    [Fact]
    public async Task 会議IDが空なら選抜の経路を通る()
    {
        var owner = $"chat-ids-empty-{Guid.NewGuid():N}";
        await AddMeetingAsync(owner, "予算検討会", "予算を 10% 減らすと決めた");

        using var scope = _factory.CreateScope();
        var chat = scope.ServiceProvider.GetRequiredService<IChatService>();

        var outcome = await chat.AskAsync(owner, "予算検討会 はどうなった？", [], CancellationToken.None);

        Assert.Equal(ChatAskStatus.Answered, outcome.Status);
    }

    [Fact]
    public async Task 質問に当たる文字起こしの窓が根拠に積まれる()
    {
        var owner = $"chat-window-{Guid.NewGuid():N}";
        var meetingId = await AddMeetingAsync(owner, "定例会", "議事録には書かれていない");

        using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Meetings.FindAsync(meetingId);
            stored!.Transcription =
                new string('あ', 2_000) + "撤退に反対したのは島田さんだった" + new string('い', 2_000);
            await db.SaveChangesAsync();
        }

        using var ask = _factory.CreateScope();
        var prompts = ask.ServiceProvider.GetRequiredService<IPromptService>();

        var prompt = prompts.BuildChatPrompt(new ChatPromptContext(
            Sources:
            [
                new ChatSourceContext(1, "定例会", null, "議事録には書かれていない", "撤退に反対したのは島田さん")
            ],
            History: [],
            Question: "誰が反対した？"));

        Assert.Contains("撤退に反対したのは島田さん", prompt);
        Assert.Contains("文字起こし抜粋", prompt);
    }
}
