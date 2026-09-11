namespace ConAI.Web.Notifications;

/// <summary>4 種類の宛先で共通に使う本文の部品。</summary>
public static class NotificationText
{
    public static string BuildSubject(MeetingNotification notification)
    {
        // 開催日時は利用者が入力した壁時計の値。UTC として扱うと時刻がずれる
        var heldAt = notification.HeldAt is { } value
            ? $"（{value.ToString(DisplayFormats.HeldAt)}）"
            : string.Empty;

        return $"{notification.Title}{heldAt}";
    }

    public static string BuildDetail(MeetingNotification notification) =>
        notification.Event == NotificationEvent.Failed
            ? notification.ErrorMessage ?? string.Empty
            : notification.Excerpt;

    /// <summary>
    /// 末尾に必ず残したい行（リンクなど）を確保してから、本文を <paramref name="maxChars"/> に収める。
    /// 先に連結してから切ると、いちばん長い議事録のときだけリンクが消える。
    /// </summary>
    public static string Compose(string body, string? tail, int maxChars)
    {
        if (string.IsNullOrEmpty(tail))
        {
            return MinutesExcerpt.Clamp(body, maxChars);
        }

        var suffix = $"\n{tail}";
        var room = maxChars - suffix.Length;

        return room <= 0
            ? MinutesExcerpt.Clamp(tail, maxChars)
            : $"{MinutesExcerpt.Clamp(body, room)}{suffix}";
    }
}
