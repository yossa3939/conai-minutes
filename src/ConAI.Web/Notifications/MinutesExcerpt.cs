namespace ConAI.Web.Notifications;

/// <summary>議事録の冒頭を通知に載せる形へ整える。</summary>
public static class MinutesExcerpt
{
    /// <summary>見出しと空行を落とした本文の冒頭を、<paramref name="maxChars"/> 文字に収めて返す。</summary>
    public static string Build(string? minutes, int maxChars)
    {
        if (maxChars <= 0 || string.IsNullOrWhiteSpace(minutes))
        {
            return string.Empty;
        }

        var lines = minutes.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            // 見出しは通知の先頭に別に載せる。ここに残すと同じ文言が二度出る
            .Where(line => line.Length > 0 && !line.StartsWith('#'));

        return Clamp(string.Join('\n', lines), maxChars);
    }

    public static string Clamp(string text, int maxChars)
    {
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        var cut = maxChars - 1;

        // 絵文字は char 2 つで 1 文字。境目で割ると壊れた文字がチャットへ送られる
        if (char.IsLowSurrogate(text[cut]))
        {
            cut--;
        }

        return string.Concat(text.AsSpan(0, cut).TrimEnd(), "…");
    }

    /// <summary>Slack と Google Chat の太字は星 1 つ。Markdown の <c>**</c> をそのまま送ると星が見えてしまう。</summary>
    public static string ToSingleAsteriskBold(string text) => text.Replace("**", "*", StringComparison.Ordinal);
}
