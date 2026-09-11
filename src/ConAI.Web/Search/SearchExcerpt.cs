namespace ConAI.Web.Search;

/// <summary>
/// 一致した箇所の切り出し。一覧の抜粋と、回答に積む窓の両方をここで作る。
/// 位置を探すのに使うのは原形ではなく表層である。
/// 原形化した語（「話す」）は文書の表層（「話した」）と一致しない。
/// </summary>
public static class SearchExcerpt
{
    public const string TitleMatchText = "会議名に一致";
    public const string TranscriptMatchText = "文字起こしに一致";

    private const string Ellipsis = "…";
    private const string WindowSeparator = "……";

    /// <summary>打たれた順にトークンを探し、最初に当たった位置を返す。当たらなければ -1。</summary>
    public static int FirstIndexOf(string normalizedText, IReadOnlyList<string> surfaces)
    {
        foreach (var surface in surfaces)
        {
            if (surface.Length == 0)
            {
                continue;
            }

            var position = normalizedText.IndexOf(surface, StringComparison.Ordinal);

            if (position >= 0)
            {
                return position;
            }
        }

        return -1;
    }

    /// <summary>最初に当たった位置の前後を切り出す。当たらなければ空。</summary>
    public static string Around(string normalizedText, IReadOnlyList<string> surfaces, int radius)
    {
        var position = FirstIndexOf(normalizedText, surfaces);

        return position < 0 ? string.Empty : Cut(normalizedText, position, radius);
    }

    /// <summary>当たった箇所を上限の数まで切り出し、記号でつなぐ。当たらなければ空。</summary>
    public static string Windows(
        string normalizedText, IReadOnlyList<string> surfaces, int radius, int maxWindows)
    {
        if (normalizedText.Length == 0 || maxWindows <= 0)
        {
            return string.Empty;
        }

        var positions = new List<int>();

        foreach (var surface in surfaces)
        {
            if (surface.Length == 0)
            {
                continue;
            }

            var from = 0;

            while (from <= normalizedText.Length - surface.Length)
            {
                var position = normalizedText.IndexOf(surface, from, StringComparison.Ordinal);

                if (position < 0)
                {
                    break;
                }

                positions.Add(position);
                from = position + surface.Length;
            }
        }

        positions.Sort();

        var windows = new List<string>();
        var covered = -1;

        foreach (var position in positions)
        {
            // 直前の窓に含まれる位置で切り直すと、ほぼ同じ文字列が 2 度並ぶ。
            if (position <= covered)
            {
                continue;
            }

            windows.Add(Cut(normalizedText, position, radius));
            covered = position + radius;

            if (windows.Count >= maxWindows)
            {
                break;
            }
        }

        return string.Join(WindowSeparator, windows);
    }

    /// <summary>
    /// どこで当たったかを決める。議事録本文、会議名の順に見て、
    /// どちらにも無ければ文字起こしとみなす。文字起こしを 1 文字も読まずに種別が決まる。
    /// </summary>
    public static (SearchMatchKind Kind, string Excerpt) Describe(
        string normalizedTitle, string normalizedMinutes, IReadOnlyList<string> surfaces)
    {
        var excerpt = Around(normalizedMinutes, surfaces, SearchLimits.ExcerptRadius);

        if (excerpt.Length > 0)
        {
            return (SearchMatchKind.Minutes, excerpt);
        }

        // 会議名は結果の 1 行目にそのまま出る。ここから抜粋を切っても同じ文字列が 2 度並ぶだけになる。
        if (FirstIndexOf(normalizedTitle, surfaces) >= 0)
        {
            return (SearchMatchKind.Title, TitleMatchText);
        }

        return (SearchMatchKind.Transcript, TranscriptMatchText);
    }

    private static string Cut(string text, int position, int radius)
    {
        var start = Math.Max(0, position - radius);
        var end = Math.Min(text.Length, position + radius);

        // 「𠮷」のような漢字は 2 つぶんの位置を占める。端がその途中に落ちたら、
        // 片割れだけを残さないよう外側へ 1 つ広げる。狭めると radius が 0 のときに端が交差する。
        if (start > 0 && char.IsLowSurrogate(text[start]))
        {
            start--;
        }

        if (end < text.Length && char.IsLowSurrogate(text[end]))
        {
            end++;
        }

        return (start > 0 ? Ellipsis : string.Empty)
            + text[start..end]
            + (end < text.Length ? Ellipsis : string.Empty);
    }
}
