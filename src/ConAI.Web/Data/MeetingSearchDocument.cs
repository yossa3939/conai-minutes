namespace ConAI.Web.Data;

/// <summary>
/// 1 会議ぶんの索引の状態。FTS5 の仮想テーブルとは <see cref="Rowid"/> で結ぶ。
/// </summary>
public sealed class MeetingSearchDocument
{
    public long Rowid { get; set; }

    public Guid MeetingId { get; set; }

    /// <summary>絞り込みのたびに Meetings へ join せずに済ませるための写し。所有者は会議の作成後に変わらない。</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>この索引が対象にした会議の最終更新。会議のほうが新しければ作り直す。</summary>
    public DateTime IndexedUpdatedAt { get; set; }

    /// <summary>分かち書きの版番号。定数と食い違えば未索引として扱う。</summary>
    public int TokenizerVersion { get; set; }
}
