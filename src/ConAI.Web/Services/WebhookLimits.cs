namespace ConAI.Web.Services;

/// <summary>Webhook 宛先の文字数上限。画面・エンティティ・DB 制約で同じ値を使う。</summary>
public static class WebhookLimits
{
    public const int MaxNameChars = 50;

    /// <summary>宛先 URL の上限。許可リストのホストはどれもこの長さに収まる。</summary>
    public const int MaxUrlChars = 2048;

    /// <summary>画面に出すマスク済み断片の上限。</summary>
    public const int MaxHintChars = 120;

    /// <summary>最後に失敗した理由の上限。</summary>
    public const int MaxErrorChars = 400;
}
