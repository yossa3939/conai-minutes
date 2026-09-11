namespace ConAI.Web.Search;

/// <summary>FTS5 の MATCH 式と LIKE のパターンを組み立てる。</summary>
public static class SearchQuery
{
    /// <summary>原形トークンから MATCH 式を作る。32 個で切ったかどうかも返す。</summary>
    public static (string Expression, bool Truncated) BuildMatch(IReadOnlyList<string> tokens)
    {
        var truncated = tokens.Count > SearchLimits.MaxQueryTokens;
        var used = truncated ? tokens.Take(SearchLimits.MaxQueryTokens) : tokens;

        // 正規化と品詞の絞り込みを通ればダブルクォートはまず残らないが、
        // 残ったときに構文エラーになるのを防ぐ。式そのものはパラメータで渡す。
        var phrases = used.Select(token =>
            $"\"{token.Replace("\"", "\"\"", StringComparison.Ordinal)}\"");

        return (string.Join(" AND ", phrases), truncated);
    }

    /// <summary>LIKE の '%…%' パターンを作る。ESCAPE '\' と対にして使う。</summary>
    public static string BuildLikePattern(string normalized)
    {
        // バックスラッシュを先に置き換える。後にすると、あとから足した \ をもう一度エスケープしてしまう。
        var escaped = normalized
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

        return $"%{escaped}%";
    }
}
