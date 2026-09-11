namespace ConAI.Web.Data;

/// <summary>
/// 議事録への質問 1 往復。答えが返ってから保存するので、失敗した質問は残らない。
/// 会議ではなく利用者に紐づく。会話は利用者ごとに 1 本だけである。
/// </summary>
public class ChatTurn
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>質問した利用者。Identity の利用者 ID を会議と同じ形で持つ。</summary>
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>利用者が入力した生の文字列。保存時に加工しない。</summary>
    public string Question { get; set; } = string.Empty;

    /// <summary>Gemini が返した生の Markdown。HTML への変換は読み出しのときに行う。</summary>
    public string Answer { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>この答えの根拠にした会議。並びは Order で決まる。</summary>
    public List<ChatTurnSource> Sources { get; set; } = [];
}
