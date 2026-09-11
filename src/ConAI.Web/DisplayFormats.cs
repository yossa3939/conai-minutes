namespace ConAI.Web;

/// <summary>画面の表示形式。入力欄と表示で桁を揃えるため 1 か所で持つ。</summary>
public static class DisplayFormats
{
    /// <summary>開催日時。入力欄（flatpickr, 秒あり）と同じく秒まで出す。</summary>
    public const string HeldAt = "yyyy/MM/dd HH:mm:ss";

    /// <summary><see cref="HeldAt"/> の DisplayFormat 用。入力欄の初期値もこの書式で埋める。</summary>
    public const string HeldAtEdit = "{0:" + HeldAt + "}";

    /// <summary>UTC で保存した日時を、画面の書式の現地時刻に直す。</summary>
    /// <remarks>
    /// SQLite から読み直した <see cref="DateTime"/> は <see cref="DateTimeKind.Unspecified"/> になる。
    /// そのまま <c>ToLocalTime</c> を呼ぶと現地時刻とみなされて変換されないため、先に UTC と決めてから直す。
    /// </remarks>
    public static string ToLocalText(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString(HeldAt);
}
