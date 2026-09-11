namespace ConAI.Web.Search;

/// <summary>
/// 索引が古いかどうかの判定。ワーカーから独立に試験できるよう純粋関数に切る。
/// 問い合わせ側の LINQ（<see cref="IMeetingSearchIndexer.ListStaleAsync"/>）とは
/// 同じ述語を別の書き方で表しているので、食い違わないことを試験で押さえる。
/// </summary>
public static class SearchStaleness
{
    public static bool IsStale(
        DateTime now, DateTime meetingUpdatedAt, DateTime? indexedUpdatedAt, int? tokenizerVersion)
    {
        // 最終更新から静止待ちのあいだは触らない
        if (meetingUpdatedAt > now - SearchLimits.IndexQuietPeriod)
        {
            return false;
        }

        if (indexedUpdatedAt is null || tokenizerVersion is null)
        {
            return true;
        }

        return indexedUpdatedAt < meetingUpdatedAt || tokenizerVersion != SearchLimits.TokenizerVersion;
    }
}
