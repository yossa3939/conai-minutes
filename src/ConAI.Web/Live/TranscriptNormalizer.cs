using System.Text.RegularExpressions;

namespace ConAI.Web.Live;

/// <summary>
/// Live 文字起こしモデルは日本語でもトークン間に空白を入れて返す（「本日 の 会議 を 始め ます 。」）。
/// CJK 文字に隣接する空白を取り除き、英単語や数字どうしの空白は 1 つに詰めて残す。
/// </summary>
public static partial class TranscriptNormalizer
{
    [GeneratedRegex(@"[ \t 　]+")]
    private static partial Regex HorizontalWhitespace();

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        return HorizontalWhitespace().Replace(text, match =>
        {
            var before = match.Index > 0 ? text[match.Index - 1] : '\0';
            var afterIndex = match.Index + match.Length;
            var after = afterIndex < text.Length ? text[afterIndex] : '\0';
            return IsCjk(before) || IsCjk(after) ? string.Empty : " ";
        });
    }

    // 全角空白（U+3000）は空白として扱うので、CJK の記号と句読点は U+3001 から数える。
    // ハングルは分かち書きが正しいので含めない。サロゲートペア（拡張漢字 B 以降）は対象外。
    private static bool IsCjk(char c) =>
        c is (>= '\u3001' and <= '\u303F')   // CJK の記号と句読点
          or (>= '\u3040' and <= '\u309F')   // ひらがな
          or (>= '\u30A0' and <= '\u30FF')   // カタカナ
          or (>= '\u3400' and <= '\u4DBF')   // CJK 統合漢字拡張 A
          or (>= '\u4E00' and <= '\u9FFF')   // CJK 統合漢字
          or (>= '\uF900' and <= '\uFAFF')   // CJK 互換漢字
          or (>= '\uFF01' and <= '\uFF60')   // 全角英数と記号
          or (>= '\uFF61' and <= '\uFF9F')   // 半角カタカナ
          or (>= '\uFFE0' and <= '\uFFEE');  // 全角の通貨記号など
}
