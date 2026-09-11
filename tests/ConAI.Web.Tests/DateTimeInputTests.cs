namespace ConAI.Web.Tests;

/// <summary>開催日時の文字列は、カルチャに関係なく yyyy/MM/dd HH:mm:ss（ゼロ埋めなし・秒なしも可）だけを受け付ける。</summary>
public class DateTimeInputTests
{
    [Theory]
    [InlineData("2026/08/28 09:05:07", 9, 5, 7)]
    [InlineData("2026/8/28 9:05", 9, 5, 0)]
    [InlineData(" 2026/08/28 09:05:07 ", 9, 5, 7)]
    public void 書式どおりの文字列を読める(string text, int hour, int minute, int second)
    {
        Assert.True(DateTimeInput.TryParse(text, out var value));
        Assert.Equal(new DateTime(2026, 8, 28, hour, minute, second), value);
        Assert.Equal(DateTimeKind.Unspecified, value.Kind);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("2026-08-28T09:05:07")]
    [InlineData("2026/08/28")]
    [InlineData("2026/13/01 00:00")]
    [InlineData("")]
    [InlineData(null)]
    public void 書式に合わない文字列は読めない(string? text)
    {
        Assert.False(DateTimeInput.TryParse(text, out _));
    }

    [Fact]
    public void 書式の案内は表示名と書式を含む()
    {
        Assert.Equal("開催日時は yyyy/MM/dd HH:mm:ss の形式で入力してください。", DateTimeInput.FormatError("開催日時"));
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void 保存された日時は_Kind_に関わらず_UTC_として現地時刻に直る(DateTimeKind kind)
    {
        var stored = new DateTime(2026, 9, 3, 5, 23, 45, kind);
        var expected = new DateTime(2026, 9, 3, 5, 23, 45, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString(DisplayFormats.HeldAt);

        Assert.Equal(expected, DisplayFormats.ToLocalText(stored));
    }
}
