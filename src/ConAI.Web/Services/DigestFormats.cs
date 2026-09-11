using System.Globalization;

namespace ConAI.Web.Services;

/// <summary>
/// 1 段目のダイジェストの見出し行の書式。
/// MinutesSelector は 600 文字に収めるための長さの見積もりに、PromptService は実際の描画に、同じ関数を使う。
/// 二重に書くと、片方だけ書式を変えたときに上限がずれる。
/// </summary>
public static class DigestFormats
{
    public const string HeldAt = "yyyy-MM-dd HH:mm";

    public static string Header(int number, string title, DateTime? heldAt) =>
        heldAt is { } value
            ? $"{number}. {title}（{value.ToString(HeldAt, CultureInfo.InvariantCulture)}）"
            : $"{number}. {title}";
}
