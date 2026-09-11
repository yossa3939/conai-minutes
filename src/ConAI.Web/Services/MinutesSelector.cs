using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Services;

public sealed class MinutesSelector : IMinutesSelector
{
    private const string DecisionsHeading = "## 決定事項";

    private readonly ApplicationDbContext _db;
    private readonly IPromptService _prompts;
    private readonly IGeminiContentClient _gemini;
    private readonly GeminiOptions _options;
    private readonly ILogger<MinutesSelector> _logger;

    public MinutesSelector(
        ApplicationDbContext db,
        IPromptService prompts,
        IGeminiContentClient gemini,
        IOptions<GeminiOptions> options,
        ILogger<MinutesSelector> logger)
    {
        _db = db;
        _prompts = prompts;
        _gemini = gemini;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<MinutesSelection> SelectAsync(
        string ownerId,
        string question,
        IReadOnlyList<string> recentQuestions,
        CancellationToken cancellationToken)
    {
        // 上限より 1 件多く読む。件数で切ったかどうかを、別に COUNT を投げずに判定するためである。
        // 選抜も回答も文字起こしを読まないので、SQL の段階で要る列だけに絞る。
        var candidates = await _db.Meetings
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId && m.Minutes != string.Empty)
            .OrderByDescending(m => m.UpdatedAt)
            .Take(ChatLimits.MaxCandidateMeetings + 1)
            .Select(m => new MeetingCandidate(m.Id, m.Title, m.HeldAt, m.Minutes))
            .ToListAsync(cancellationToken);

        var truncated = candidates.Count > ChatLimits.MaxCandidateMeetings;
        var digests = new List<MeetingDigest>();
        var listed = new List<MeetingCandidate>();
        var total = 0;

        foreach (var meeting in candidates.Take(ChatLimits.MaxCandidateMeetings))
        {
            var number = listed.Count + 1;
            var header = DigestFormats.Header(number, meeting.Title, meeting.HeldAt);
            var excerpt = BuildExcerpt(meeting, header.Length);

            if (total + header.Length + excerpt.Length > ChatLimits.MaxSelectionContextChars)
            {
                // 新しいものから詰めているので、ここから先は古い会議だけが残る。
                truncated = true;
                break;
            }

            total += header.Length + excerpt.Length;
            digests.Add(new MeetingDigest(number, meeting.Title, meeting.HeldAt, excerpt));
            listed.Add(meeting);
        }

        if (digests.Count == 0)
        {
            // 候補が無ければ選びようがない。Gemini を呼ばずに返す。
            return new MinutesSelection([], truncated);
        }

        var request = new SelectionRequest(
            _options.SelectModel,
            _prompts.BuildSelectionSystemInstruction(),
            _prompts.BuildSelectionPrompt(new SelectionPromptContext(digests, recentQuestions, question)));

        using var timeout = new CancellationTokenSource(ChatLimits.SelectTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        var result = await _gemini.SelectAsync(request, linked.Token);
        var selected = Resolve(result.MeetingNumbers, listed);

        // 選んだ会議 ID だけを残す。質問文と議事録はログに出さない。
        _logger.LogDebug(
            "議事録を選抜しました。meetingIds={MeetingIds}",
            selected.Select(m => m.Id).ToArray());

        return new MinutesSelection(selected, truncated);
    }

    /// <summary>
    /// 返ってきた連番を会議へ戻す。範囲外は捨て、重複は畳み、6 件目以降は捨てる。
    /// 生成モデルの出力なので、正しい番号が返る前提を置かない。
    /// </summary>
    private static IReadOnlyList<MeetingCandidate> Resolve(
        IReadOnlyList<int> numbers, IReadOnlyList<MeetingCandidate> listed)
    {
        var selected = new List<MeetingCandidate>();
        var seen = new HashSet<int>();

        foreach (var number in numbers)
        {
            if (number < 1 || number > listed.Count || !seen.Add(number))
            {
                continue;
            }

            selected.Add(listed[number - 1]);

            if (selected.Count == ChatLimits.MaxSelectedMeetings)
            {
                break;
            }
        }

        return selected;
    }

    /// <summary>
    /// 見出し行を最大 15 行、続けて本文の冒頭を最大 300 文字取る。
    /// 見出しの並びだけで会議の主題はおおむね分かるので、本文より先に置く。
    /// </summary>
    private static string BuildExcerpt(MeetingCandidate meeting, int headerLength)
    {
        var lines = meeting.Minutes.Replace("\r\n", "\n").Split('\n');

        var headings = lines
            .Where(IsHeading)
            .Select(line => line.Trim())
            .Take(ChatLimits.MaxDigestHeadingLines)
            .ToList();

        var body = string.Join(
            " ",
            BodyLines(lines).Select(line => line.Trim()).Where(line => line.Length > 0));

        if (body.Length > ChatLimits.DigestExcerptChars)
        {
            body = body[..ChatLimits.DigestExcerptChars];
        }

        var excerpt = (headings.Count, body.Length) switch
        {
            (0, _) => body,
            (_, 0) => string.Join(" / ", headings),
            _ => $"{string.Join(" / ", headings)}{Environment.NewLine}{body}"
        };

        // 会議名と開催日時を含めて 600 文字に収める。見出しが長い会議でも枠を食い切らせない。
        var budget = Math.Max(0, ChatLimits.MaxDigestChars - headerLength);

        return excerpt.Length <= budget ? excerpt : excerpt[..budget];
    }

    /// <summary>本文は「## 決定事項」以降を優先する。無ければ見出しを除いた先頭から取る。</summary>
    private static IEnumerable<string> BodyLines(string[] lines)
    {
        var start = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim().StartsWith(DecisionsHeading, StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        return lines.Skip(start).Where(line => !IsHeading(line));
    }

    private static bool IsHeading(string line) => line.TrimStart().StartsWith('#');
}
