using System.Text.RegularExpressions;

namespace ConAI.Web.Tests;

public static class HtmlTestHelpers
{
    public static string ExtractAntiforgeryToken(string html)
    {
        // 計画原文は末尾の閉じ引用符マッチ \" を含むが、raw string 内で " が 4 連続すると
        // CS8998 になるため取り除いた。[^"]+ は " の手前で停止するのでキャプチャ結果は同じ。
        var match = Regex.Match(html, """name="__RequestVerificationToken"[^>]*value="([^"]+)""");
        Assert.True(match.Success, "Antiforgery トークンが HTML に見つかりません。");
        return match.Groups[1].Value;
    }

    public static string ExtractCsrfMeta(string html)
    {
        var match = Regex.Match(html, """<meta name="csrf" content="([^"]+)""");
        Assert.True(match.Success, "csrf メタタグが HTML に見つかりません。");
        return match.Groups[1].Value;
    }
}
