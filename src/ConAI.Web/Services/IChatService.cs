using ConAI.Web.Data;
using ConAI.Web.Search;

namespace ConAI.Web.Services;

public enum ChatAskStatus
{
    /// <summary>答えを作って保存した。</summary>
    Answered,

    /// <summary>議事録のある会議が 1 件も無い。</summary>
    NoMeetings,

    /// <summary>候補はあったが、質問に関係する議事録が選ばれなかった。</summary>
    NoMatch,

    /// <summary>往復が上限に達している。</summary>
    LimitReached
}

/// <summary>
/// 質問の結果。Notes は画面に出す但し書きで、保存はしない。
/// 打ち切りはそのときの持ち物の量に依存するので、読み直したときに同じ注記が正しいとは限らない。
/// </summary>
public sealed record ChatAskOutcome(
    ChatAskStatus Status,
    ChatTurn? Turn,
    IReadOnlyList<string> Notes);

public static class ChatNotes
{
    public const string CandidatesTruncated = "会議が多いため、新しいものから一部だけを対象に探しました。";

    public const string SourcesDropped = "根拠が多いため、一部の議事録は使いませんでした。";
}

public interface IChatService
{
    Task<IReadOnlyList<ChatTurn>> ListAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>
    /// 質問に答える。<paramref name="meetingIds"/> が 1 件以上あれば選抜を呼ばず、
    /// 渡された順に先頭から <see cref="SearchLimits.MaxAnswerMeetings"/> 件までを根拠にする。
    /// 空なら既存の選抜を呼ぶ。
    /// </summary>
    Task<ChatAskOutcome> AskAsync(
        string ownerId,
        string question,
        IReadOnlyList<Guid> meetingIds,
        CancellationToken cancellationToken);

    /// <summary>消す往復が無くても失敗にしない。エンドポイントは常に 204 を返す。</summary>
    Task ClearAsync(string ownerId, CancellationToken cancellationToken);
}
