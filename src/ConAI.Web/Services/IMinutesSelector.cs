namespace ConAI.Web.Services;

/// <summary>
/// 1 段目の結果。CandidatesTruncated は、候補そのものを上限で切ったことを表す。
/// 選ばれなかっただけの会議とは意味が違うので、画面には注記として出す。
/// </summary>
public sealed record MinutesSelection(IReadOnlyList<MeetingCandidate> Meetings, bool CandidatesTruncated);

public interface IMinutesSelector
{
    Task<MinutesSelection> SelectAsync(
        string ownerId,
        string question,
        IReadOnlyList<string> recentQuestions,
        CancellationToken cancellationToken);
}
