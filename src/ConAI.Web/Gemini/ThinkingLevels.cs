using System.Collections.Immutable;
using Google.GenAI.Types;

namespace ConAI.Web.Gemini;

/// <summary>thinking level 設定文字列の解釈。許可値の一覧はここだけが正で、起動時検証のメッセージもこれを使う。</summary>
public static class ThinkingLevels
{
    // 未指定（空）は ThinkingConfig を送らないことを意味するため、許可値とは別扱いにする。
    // 許可値の一覧はここだけが正であるため、呼び出し側から要素を差し替えられない読み取り専用で公開する。
    public static readonly ImmutableArray<string> AllowedValues = ["MINIMAL", "LOW", "MEDIUM", "HIGH"];

    public static bool IsAllowed(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalized = Normalize(value);
        return AllowedValues.Contains(normalized);
    }

    public static ThinkingConfig? Build(string? value)
    {
        // 未指定なら null を返し、リクエストに ThinkingConfig を載せない（モデル任せ）。
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // 許可値の判定は起動時検証だけが担うわけではない。Build は公開メソッドで、
        // 設定ファイルを経由しない呼び出し経路もあり得るため、ここでも受け付けない値を弾く。
        if (!IsAllowed(value))
        {
            var allowedList = string.Join("/", AllowedValues.Select(allowed => $"'{allowed}'"));
            throw new ArgumentException(
                $"thinking level には {allowedList} のいずれか、または空（未指定）を指定してください。実際の値: '{value}'",
                nameof(value));
        }

        return new ThinkingConfig { ThinkingLevel = ThinkingLevel.FromString(Normalize(value)) };
    }

    private static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
