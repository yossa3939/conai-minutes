using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Services;

public sealed class ChatService : IChatService
{
    private readonly ApplicationDbContext _db;
    private readonly IPromptService _prompts;
    private readonly IGeminiContentClient _gemini;
    private readonly IMinutesSelector _selector;
    private readonly ISearchTokenizer _tokenizer;
    private readonly GeminiOptions _options;
    private readonly TimeProvider _timeProvider;

    public ChatService(
        ApplicationDbContext db,
        IPromptService prompts,
        IGeminiContentClient gemini,
        IMinutesSelector selector,
        ISearchTokenizer tokenizer,
        IOptions<GeminiOptions> options,
        TimeProvider timeProvider)
    {
        _db = db;
        _prompts = prompts;
        _gemini = gemini;
        _selector = selector;
        _tokenizer = tokenizer;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<ChatTurn>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        await _db.ChatTurns
            .Where(t => t.OwnerId == ownerId)
            .Include(t => t.Sources.OrderBy(s => s.Order))
            .OrderBy(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    public async Task<ChatAskOutcome> AskAsync(
        string ownerId, string question, IReadOnlyList<Guid> meetingIds, CancellationToken cancellationToken)
    {
        // 長さの検証はエンドポイントにもあるが、サービスを直接呼ぶ呼び出し元が
        // 増えても契約が自分を守れるように、ここでもう一度確かめる。
        if (question.Length > ChatLimits.MaxQuestionChars)
        {
            throw new ArgumentOutOfRangeException(nameof(question), "質問が文字数の上限を超えています。");
        }

        var history = await ListAsync(ownerId, cancellationToken);

        // この判定は読み出した時点の件数に対して行う。判定から保存までの間に同じ利用者の
        // 同時リクエストが入り、201 往復目が保存され得る競合が残っている。画面の二重送信防止と
        // レート制限（1 分 10 回）で同時投入は起きにくく、超えても上限が少し緩むだけで壊れない。
        if (history.Count >= ChatLimits.MaxTurns)
        {
            return new ChatAskOutcome(ChatAskStatus.LimitReached, null, []);
        }

        var notes = new List<string>();
        IReadOnlyList<MeetingCandidate> candidates;

        if (meetingIds.Count > 0)
        {
            candidates = await ReadSelectedAsync(ownerId, meetingIds, question, cancellationToken);

            // 絞り込みで 1 件も残らなかった。存在しない ID と他人の ID で応答を変えると、
            // ID の実在を確かめる手段になる
            if (candidates.Count == 0)
            {
                return new ChatAskOutcome(ChatAskStatus.NoMatch, null, notes);
            }
        }
        else
        {
            // 議事録が 1 件も無い利用者に Gemini を呼ぶ意味は無い。選抜より前に切る。
            var hasMinutes = await _db.Meetings
                .AnyAsync(m => m.OwnerId == ownerId && m.Minutes != string.Empty, cancellationToken);

            if (!hasMinutes)
            {
                return new ChatAskOutcome(ChatAskStatus.NoMeetings, null, []);
            }

            var recentQuestions = history
                .TakeLast(ChatLimits.RecentQuestionsForSelection)
                .Select(t => t.Question)
                .ToArray();

            var selection = await _selector.SelectAsync(ownerId, question, recentQuestions, cancellationToken);

            if (selection.CandidatesTruncated)
            {
                notes.Add(ChatNotes.CandidatesTruncated);
            }

            if (selection.Meetings.Count == 0)
            {
                return new ChatAskOutcome(ChatAskStatus.NoMatch, null, notes);
            }

            candidates = selection.Meetings;
        }

        var stacked = StackMinutes(candidates);

        if (stacked.Count < candidates.Count)
        {
            notes.Add(ChatNotes.SourcesDropped);
        }

        var request = new ChatRequest(
            _options.GenerateModel,
            _prompts.BuildChatSystemInstruction(),
            _prompts.BuildChatPrompt(new ChatPromptContext(
                Sources: [.. stacked.Select((m, i) =>
                    new ChatSourceContext(i + 1, m.Title, m.HeldAt, m.Minutes, m.TranscriptExcerpt))],
                History: BuildHistory(history),
                Question: question)));

        using var timeout = new CancellationTokenSource(ChatLimits.AnswerTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        // 失敗もタイムアウトも例外のまま呼び出し元へ抜ける。エンドポイントが 502 に変換する。
        var result = await _gemini.AskAsync(request, linked.Token);

        if (string.IsNullOrWhiteSpace(result.Answer))
        {
            // 空の往復が履歴に残ると、以降の質問の文脈にも空が混ざり続ける。
            throw new InvalidOperationException("Gemini から空の答えが返りました。");
        }

        var used = NarrowByUsed(result.UsedMeetingNumbers, stacked);

        var turn = new ChatTurn
        {
            OwnerId = ownerId,
            Question = question,
            Answer = result.Answer,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        };

        for (var i = 0; i < used.Count; i++)
        {
            turn.Sources.Add(new ChatTurnSource
            {
                MeetingId = used[i].Id,
                // 会議名は写し取る。会議を消しても、どの会議を根拠にしたかは残す（決定 8）。
                MeetingTitle = Shorten(used[i].Title),
                Order = i
            });
        }

        _db.ChatTurns.Add(turn);

        // 答えはこの時点で生成されて課金も済んでいる。直後に利用者が離脱しても、
        // 保存だけは取り消さない。Gemini 呼び出しまでのキャンセルは上の linked token が担う。
        await _db.SaveChangesAsync(CancellationToken.None);

        return new ChatAskOutcome(ChatAskStatus.Answered, turn, notes);
    }

    public async Task ClearAsync(string ownerId, CancellationToken cancellationToken)
    {
        // 根拠は ChatTurn への連鎖削除で一緒に消える。
        // 消えた件数は返さない。呼び出し元は常に 204 を返すので、使い道が無い。
        await _db.ChatTurns
            .Where(t => t.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>
    /// 選ばれた順に議事録を積み、20 万文字で止める。
    /// 1 件目だけは、それ単体で上限を超えていても入れる。落とすと根拠ゼロで答えを作らせることになる。
    /// break ではなく continue で回すのは、大きい議事録の後ろにある小さい議事録を拾うためである。
    /// </summary>
    private static IReadOnlyList<MeetingCandidate> StackMinutes(IReadOnlyList<MeetingCandidate> meetings)
    {
        var stacked = new List<MeetingCandidate>();
        var total = 0;

        foreach (var meeting in meetings)
        {
            if (stacked.Count > 0 && total + Weight(meeting) > ChatLimits.MaxAnswerContextChars)
            {
                continue;
            }

            total += Weight(meeting);
            stacked.Add(meeting);
        }

        return stacked;
    }

    /// <summary>枠を数えるときは窓のぶんも足す。全体の枠は変えない。</summary>
    private static int Weight(MeetingCandidate meeting) =>
        meeting.Minutes.Length + meeting.TranscriptExcerpt.Length;

    /// <summary>
    /// 画面が並べた順のまま、先頭 10 件だけを読む。順位を決めるのは呼び出し元である。
    /// bm25 の計算には検索語が要るが、回答の要求に載るのは質問文であって、
    /// 直前の検索語と同じとは限らない。
    /// </summary>
    private async Task<IReadOnlyList<MeetingCandidate>> ReadSelectedAsync(
        string ownerId,
        IReadOnlyList<Guid> meetingIds,
        string question,
        CancellationToken cancellationToken)
    {
        // 上限は読む前に掛ける。数千件を並べた要求で会議の読み出しを膨らませられないようにする
        var wanted = meetingIds.Distinct().Take(SearchLimits.MaxAnswerMeetings).ToList();

        // 所有者で必ず絞る。省くと、検索では出せない会議の中身を根拠として読ませる経路が開く
        var meetings = await _db.Meetings
            .AsNoTracking()
            .Where(m => m.OwnerId == ownerId && wanted.Contains(m.Id))
            .Select(m => new { m.Id, m.Title, m.HeldAt, m.Minutes, m.Transcription })
            .ToListAsync(cancellationToken);

        // 位置を探すのに使うのは、直前の検索語ではなく質問文である。
        // 「予算」で絞ってから「誰が反対した？」と聞く使い方では、切り出すべきは「反対」の周辺である
        var surfaces = _tokenizer.Surfaces(question, cancellationToken);
        var byId = meetings.ToDictionary(m => m.Id);
        var candidates = new List<MeetingCandidate>();

        foreach (var id in wanted)
        {
            // 絞り込みで消えた ID は黙って無視し、要求そのものは失敗させない
            if (!byId.TryGetValue(id, out var meeting))
            {
                continue;
            }

            // 見つからないときに冒頭を切って埋めることはしない。
            // 質問と無関係な箇所を根拠として積むと、答えの質が下がる
            var excerpt = SearchExcerpt.Windows(
                _tokenizer.Normalize(meeting.Transcription),
                surfaces,
                SearchLimits.TranscriptWindowRadius,
                SearchLimits.MaxTranscriptWindows);

            candidates.Add(new MeetingCandidate(meeting.Id, meeting.Title, meeting.HeldAt, meeting.Minutes, excerpt));
        }

        return candidates;
    }

    /// <summary>
    /// 直近 10 往復を新しいほうから 2 万文字まで詰め、時系列に戻す。
    /// 落ちるのは古い往復だけなので、直前のやりとりは必ず残る。落としたことは注記にしない。
    /// </summary>
    private static IReadOnlyList<ChatHistoryEntry> BuildHistory(IReadOnlyList<ChatTurn> history)
    {
        var recent = history.TakeLast(ChatLimits.HistoryTurnsForAnswer).ToList();
        var entries = new List<ChatHistoryEntry>();
        var total = 0;

        for (var i = recent.Count - 1; i >= 0; i--)
        {
            var length = recent[i].Question.Length + recent[i].Answer.Length;
            if (total + length > ChatLimits.MaxHistoryChars)
            {
                break;
            }

            total += length;
            entries.Add(new ChatHistoryEntry(recent[i].Question, recent[i].Answer));
        }

        entries.Reverse();

        return entries;
    }

    /// <summary>
    /// 2 段目が「使った」と答えた番号だけに根拠を絞る（決定 14）。
    /// 空や壊れた番号しか返らなければ、渡した全件を根拠にする。絞れないことを根拠ゼロの理由にしない。
    /// </summary>
    private static IReadOnlyList<MeetingCandidate> NarrowByUsed(
        IReadOnlyList<int> numbers, IReadOnlyList<MeetingCandidate> stacked)
    {
        var used = new List<MeetingCandidate>();
        var seen = new HashSet<int>();

        foreach (var number in numbers)
        {
            if (number < 1 || number > stacked.Count || !seen.Add(number))
            {
                continue;
            }

            used.Add(stacked[number - 1]);
        }

        return used.Count > 0 ? used : stacked;
    }

    private static string Shorten(string title) =>
        title.Length <= ChatLimits.MaxSourceTitleChars
            ? title
            : title[..ChatLimits.MaxSourceTitleChars];
}
