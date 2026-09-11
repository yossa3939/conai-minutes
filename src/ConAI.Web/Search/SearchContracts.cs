namespace ConAI.Web.Search;

public sealed record SearchHit(
    Guid MeetingId,
    string Title,
    DateTime? HeldAt,
    string Excerpt,
    SearchMatchKind MatchedIn);

/// <summary>found は FTS5 で該当、fallback は引き直しで該当、empty はどちらも 0 件。</summary>
public enum SearchStatus
{
    Found,
    Fallback,
    Empty
}

public sealed record SearchOutcome(
    SearchStatus Status,
    int Total,
    int Page,
    int IndexingRemaining,
    IReadOnlyList<SearchHit> Hits,
    IReadOnlyList<string> Notes);

/// <summary>検索結果に添える注記。3 つは同時に立ちうる。</summary>
public static class SearchNotes
{
    public static readonly string QueryTruncated =
        $"検索語が多いため、先頭 {SearchLimits.MaxQueryTokens} 語だけで検索しました。";

    public const string FellBackToLike = "辞書に無い語のようです。会議名と議事録本文をそのまま照合しました。";

    /// <summary>索引が揃う前の検索は、黙って一部だけを返す。
    /// 利用者から見ると「無かった」のか「まだ入っていない」のか区別が付かない。</summary>
    public static string Indexing(int remaining) =>
        $"索引を作成中です（残り {remaining} 件）。結果が揃っていない可能性があります。";
}
