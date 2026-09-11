using System.Text.RegularExpressions;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MinutesSelectorTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MinutesSelectorTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task AddMeetingsAsync(string ownerId, params (string Title, string Minutes, int MinutesAgo)[] rows)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var baseTime = new DateTime(2026, 9, 4, 12, 0, 0, DateTimeKind.Utc);

        foreach (var row in rows)
        {
            db.Meetings.Add(new Meeting
            {
                OwnerId = ownerId,
                Title = row.Title,
                Minutes = row.Minutes,
                UpdatedAt = baseTime.AddMinutes(-row.MinutesAgo)
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task<MinutesSelection> SelectAsync(
        string ownerId,
        string question,
        Func<SelectionRequest, CancellationToken, Task<SelectionResult>> handler,
        string[]? recentQuestions = null)
    {
        var fake = _factory.Services.GetRequiredService<FakeGeminiContentClient>();
        fake.SelectHandler = handler;

        try
        {
            using var scope = _factory.CreateScope();
            var selector = scope.ServiceProvider.GetRequiredService<IMinutesSelector>();

            return await selector.SelectAsync(
                ownerId, question, recentQuestions ?? [], CancellationToken.None);
        }
        finally
        {
            fake.SelectHandler = null;
        }
    }

    private string LastPrompt() =>
        _factory.Services.GetRequiredService<FakeGeminiContentClient>().LastSelectionRequest!.Prompt;

    /// <summary>テンプレート本体にも「## 会議の一覧」等の見出しがあるので、数えるのは DIGESTS 区画の中だけにする。</summary>
    private static string DigestsOf(string prompt)
    {
        var start = prompt.IndexOf("<<<DIGESTS", StringComparison.Ordinal);
        var end = prompt.IndexOf("DIGESTS>>>", StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "DIGESTS 区画がプロンプトにありません");

        return prompt[(start + "<<<DIGESTS".Length)..end].Trim();
    }

    // 見出し行は行頭の「N. 会議名」。抜粋は字下げされているので、行頭の数字は見出しにしか出ない。
    private static readonly Regex NumberedLinePattern =
        new(@"^(?<number>\d+)\.\s", RegexOptions.Multiline | RegexOptions.Compiled);

    [Fact]
    public async Task 議事録が空の会議は候補に入らない()
    {
        await AddMeetingsAsync(
            "selector-empty-minutes",
            ("ある会議", "## 決定事項\n- 増額を承認した", 10),
            ("空の会議", string.Empty, 5));

        await SelectAsync(
            "selector-empty-minutes", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());

        Assert.Contains("ある会議", digests);
        Assert.DoesNotContain("空の会議", digests);
    }

    [Fact]
    public async Task 他人の会議は候補に入らない()
    {
        await AddMeetingsAsync("selector-own", ("自分の会議", "## 決定事項\n- 増額を承認した", 10));
        await AddMeetingsAsync("selector-stranger", ("他人の会議", "## 決定事項\n- 別人の決定", 5));

        await SelectAsync(
            "selector-own", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());

        Assert.Contains("自分の会議", digests);
        Assert.DoesNotContain("他人の会議", digests);
    }

    [Fact]
    public async Task 候補は更新の新しい順に番号が振られる()
    {
        await AddMeetingsAsync(
            "selector-order",
            ("古い", "## 決定事項\n- 内容", 3),
            ("中", "## 決定事項\n- 内容", 2),
            ("新しい", "## 決定事項\n- 内容", 1));

        await SelectAsync(
            "selector-order", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());
        var newest = digests.IndexOf("1. 新しい", StringComparison.Ordinal);
        var middle = digests.IndexOf("2. 中", StringComparison.Ordinal);
        var oldest = digests.IndexOf("3. 古い", StringComparison.Ordinal);

        Assert.True(newest >= 0 && middle >= 0 && oldest >= 0, $"行が見つかりません: {digests}");
        Assert.True(newest < middle, "「1. 新しい」が「2. 中」より前であること");
        Assert.True(middle < oldest, "「2. 中」が「3. 古い」より前であること");
    }

    [Fact]
    public async Task ダイジェストの見出しは15行までで本文より前に来る()
    {
        // 見出し 20 行 + 決定事項。決定事項の見出しも 16 番目として切り捨てられ、本文は決定事項以降になる。
        var headings = string.Join("\n", Enumerable.Range(0, 20).Select(i => $"# 見出し {i}"));
        await AddMeetingsAsync(
            "selector-heading-lines",
            ("見出しの会議", $"{headings}\n## 決定事項\n本文の一行です。", 10));

        await SelectAsync(
            "selector-heading-lines", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());
        var hashCount = digests.Count(c => c == '#');
        var bodyAt = digests.IndexOf("本文の一行です。", StringComparison.Ordinal);
        var lastHashAt = digests.LastIndexOf('#');

        Assert.Equal(ChatLimits.MaxDigestHeadingLines, hashCount);
        Assert.True(lastHashAt < bodyAt, "見出しの連結が本文より前に来ること");
    }

    [Fact]
    public async Task 決定事項があれば本文はそこから始まる()
    {
        await AddMeetingsAsync(
            "selector-decisions",
            ("本文の起点", "## 前置き\n無関係な前置き\n## 決定事項\n拾うべき本文", 10));

        await SelectAsync(
            "selector-decisions", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());

        Assert.Contains("拾うべき本文", digests);
        Assert.DoesNotContain("無関係な前置き", digests);
    }

    [Fact]
    public async Task ダイジェストは1件600文字に収まる()
    {
        // 見出し 15 行（" / " 連結で 582 文字）と本文 300 文字で、抜粋を 880 文字超にする。
        // 見出しの無い入力だと本文の切り詰め（DigestExcerptChars）で 300 文字そこで終わり、
        // 600 文字の上限そのものが働いたことを確かめられない。
        var headings = string.Join("\n", Enumerable.Range(0, 15).Select(_ => $"# {new string('あ', 34)}"));
        var minutes = $"{headings}\n## 決定事項\n{new string('う', ChatLimits.DigestExcerptChars)}";

        await AddMeetingsAsync(
            "selector-digest-size",
            (new string('題', 30), minutes, 10));

        await SelectAsync(
            "selector-digest-size", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var digests = DigestsOf(LastPrompt());

        // 上限は見出し行込みの 600 文字。区画には行末の改行と抜粋の字下げ 3 文字が上乗せされるため、
        // そのぶんを見込んだ長さに収まることを確かめる。
        var maxWithIndent = ChatLimits.MaxDigestChars + Environment.NewLine.Length + 3;

        Assert.True(
            digests.Length <= maxWithIndent,
            $"ダイジェストが {digests.Length} 文字になりました");

        // 切り詰めは見出しの連結の途中で止まる。末尾に置いた本文は載らない。
        Assert.DoesNotContain("う", digests);
    }

    [Fact]
    public async Task 候補の合計が上限を超えると打ち切って印を立てる()
    {
        // 見出しを 15 行並べ、1 会議ぶんのダイジェストが 600 文字ぎりぎりになる議事録にする。
        var minutes = string.Join("\n", Enumerable.Range(0, 15).Select(_ => $"# {new string('あ', 34)}"));
        var rows = Enumerable.Range(0, 400)
            .Select(i => ($"埋め草 {i}", minutes, i))
            .ToArray();

        await AddMeetingsAsync("selector-context-cap", rows);

        var selection = await SelectAsync(
            "selector-context-cap", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var listed = NumberedLinePattern.Matches(DigestsOf(LastPrompt())).Count;

        Assert.True(selection.CandidatesTruncated);
        Assert.True(listed < 400, $"候補が {listed} 件、打ち切られていません");
    }

    [Fact]
    public async Task 候補が500件を超えると打ち切って印を立てる()
    {
        var rows = Enumerable.Range(0, 501)
            .Select(i => ($"埋め草 {i}", "- 短い", i))
            .ToArray();

        await AddMeetingsAsync("selector-count-cap", rows);

        var selection = await SelectAsync(
            "selector-count-cap", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1])));

        var max = NumberedLinePattern.Matches(DigestsOf(LastPrompt()))
            .Select(m => int.Parse(m.Groups["number"].Value))
            .Max();

        Assert.True(selection.CandidatesTruncated);
        Assert.Equal(ChatLimits.MaxCandidateMeetings, max);
    }

    [Fact]
    public async Task 範囲外の連番は捨てられる()
    {
        await AddMeetingsAsync(
            "selector-out-of-range",
            ("新しい会議", "## 決定事項\n- 内容", 1),
            ("古い会議", "## 決定事項\n- 内容", 2));

        var selection = await SelectAsync(
            "selector-out-of-range", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([0, 3, 1])));

        var meeting = Assert.Single(selection.Meetings);

        Assert.Equal("新しい会議", meeting.Title);
    }

    [Fact]
    public async Task 重複した連番は畳まれる()
    {
        await AddMeetingsAsync(
            "selector-duplicate",
            ("1番の会議", "## 決定事項\n- 内容", 1),
            ("2番の会議", "## 決定事項\n- 内容", 2));

        var selection = await SelectAsync(
            "selector-duplicate", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([1, 1, 2])));

        Assert.Equal(2, selection.Meetings.Count);
        Assert.Equal("1番の会議", selection.Meetings[0].Title);
        Assert.Equal("2番の会議", selection.Meetings[1].Title);
    }

    [Fact]
    public async Task 選抜は5件までで6件目以降の連番は捨てられる()
    {
        var rows = Enumerable.Range(0, 7)
            .Select(i => ($"候補 {i}", "## 決定事項\n- 内容", i + 1))
            .ToArray();

        await AddMeetingsAsync("selector-selected-cap", rows);

        var selection = await SelectAsync(
            "selector-selected-cap",
            "何が決まりましたか",
            (_, _) => Task.FromResult(new SelectionResult([.. Enumerable.Range(1, 7)])));

        Assert.Equal(ChatLimits.MaxSelectedMeetings, selection.Meetings.Count);
    }

    [Fact]
    public async Task 空の配列は該当なしになる()
    {
        await AddMeetingsAsync("selector-empty-answer", ("ある会議", "## 決定事項\n- 内容", 10));

        var selection = await SelectAsync(
            "selector-empty-answer", "何が決まりましたか", (_, _) => Task.FromResult(new SelectionResult([])));

        Assert.Empty(selection.Meetings);
        Assert.False(selection.CandidatesTruncated);
    }
}
