namespace ConAI.Web.Services;

public static class MinutesTemplateLimits
{
    /// <summary>テンプレート名の上限。画面には案内せず、超えたときだけ検証で弾く安全弁。</summary>
    public const int MaxNameChars = 100;

    /// <summary>テンプレート本文の上限。本文はプロンプトへそのまま入るため、桁外れの入力だけを止める。</summary>
    public const int MaxBodyChars = 20_000;
}
