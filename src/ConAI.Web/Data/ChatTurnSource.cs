namespace ConAI.Web.Data;

/// <summary>
/// 往復 1 件が根拠にした会議 1 件。
/// 会議名は聞いた時点の写しを持つ。会議名を後から変えても、答えが指していた対象は変わらないためである。
/// </summary>
public class ChatTurnSource
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChatTurnId { get; set; }

    public ChatTurn? ChatTurn { get; set; }

    /// <summary>根拠にした会議。会議が消えると NULL になり、リンクだけが失われる。</summary>
    public Guid? MeetingId { get; set; }

    public Meeting? Meeting { get; set; }

    /// <summary>聞いた時点の会議名の写し。表示は常にこちらを使う。</summary>
    public string MeetingTitle { get; set; } = string.Empty;

    /// <summary>根拠を並べる順。0 から始まる。</summary>
    public int Order { get; set; }
}
