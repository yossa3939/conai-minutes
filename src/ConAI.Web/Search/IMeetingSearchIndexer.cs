namespace ConAI.Web.Search;

public interface IMeetingSearchIndexer
{
    /// <summary>1 会議を索引に入れ直す。行が無ければ作る。</summary>
    Task IndexAsync(Guid meetingId, CancellationToken cancellationToken);

    /// <summary>索引が古い会議を、静止待ちを満たすものだけ返す。</summary>
    Task<IReadOnlyList<Guid>> ListStaleAsync(
        DateTime now, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// その利用者の未索引の会議の数。静止待ちと件数の上限は掛けない。
    /// 検索側に置かず索引側に置くのは、<see cref="ListStaleAsync"/> と同じ述語を使うためである。
    /// 2 つのクラスに書き写すと、片方だけが腐る。
    /// </summary>
    Task<int> CountUnindexedAsync(string ownerId, CancellationToken cancellationToken);
}
