using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Notifications;

namespace ConAI.Web.Tests;

public class MeetingNotificationFactoryTests
{
    private static readonly Guid MeetingId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static Meeting Sample() => new()
    {
        Id = MeetingId,
        Title = "定例",
        HeldAt = new DateTime(2026, 9, 5, 10, 0, 0, DateTimeKind.Unspecified),
        Minutes = "# 議事録\n次の版で出す。",
        GenerationError = "議事録の生成に失敗しました。しばらく待ってからやり直してください。"
    };

    private static NotificationOptions Options(string baseUrl = "https://conai.example.com") =>
        new() { PublicBaseUrl = baseUrl, ExcerptChars = 1500 };

    [Fact]
    public void 成功の通知は抜粋とリンクを持つ()
    {
        var notification = MeetingNotificationFactory.Create(Sample(), NotificationEvent.Succeeded, Options());

        Assert.Equal("定例", notification.Title);
        Assert.Equal("次の版で出す。", notification.Excerpt);
        Assert.Equal($"https://conai.example.com/Meetings/Details/{MeetingId}", notification.Url);
        Assert.Null(notification.ErrorMessage);
    }

    [Fact]
    public void 失敗の通知は抜粋を持たず理由を持つ()
    {
        var notification = MeetingNotificationFactory.Create(Sample(), NotificationEvent.Failed, Options());

        Assert.Equal(string.Empty, notification.Excerpt);
        Assert.Equal("議事録の生成に失敗しました。しばらく待ってからやり直してください。", notification.ErrorMessage);
    }

    [Fact]
    public void 理由が記録されていなければ既定の文言を使う()
    {
        var meeting = Sample();
        meeting.GenerationError = null;

        var notification = MeetingNotificationFactory.Create(meeting, NotificationEvent.Failed, Options());

        Assert.Equal("詳しい理由は記録されていません。", notification.ErrorMessage);
    }

    [Fact]
    public void 基点URLが空ならリンクを入れない()
    {
        var notification = MeetingNotificationFactory.Create(Sample(), NotificationEvent.Succeeded, Options(string.Empty));

        Assert.Null(notification.Url);
    }

    [Fact]
    public void 基点URLの末尾のスラッシュは重ねない()
    {
        var notification = MeetingNotificationFactory.Create(
            Sample(), NotificationEvent.Succeeded, Options("https://conai.example.com/"));

        Assert.Equal($"https://conai.example.com/Meetings/Details/{MeetingId}", notification.Url);
    }

    [Fact]
    public void 会議名が空なら代わりの見出しを使う()
    {
        var meeting = Sample();
        meeting.Title = "   ";

        var notification = MeetingNotificationFactory.Create(meeting, NotificationEvent.Manual, Options());

        Assert.Equal("（名前のない会議）", notification.Title);
    }

    [Fact]
    public void テスト通知は会議に依存しない()
    {
        var notification = MeetingNotificationFactory.CreateTest(Options());

        Assert.Equal(NotificationEvent.Test, notification.Event);
        Assert.Equal("ConAI からのテスト通知です", NotificationHeadings.Text(notification.Event));
        Assert.Null(notification.HeldAt);
        Assert.Contains("宛先の設定", notification.Excerpt);
    }

    [Fact]
    public void 件名は開催日時を壁時計のまま出す()
    {
        var notification = MeetingNotificationFactory.Create(Sample(), NotificationEvent.Succeeded, Options());

        Assert.Equal("定例（2026/09/05 10:00:00）", NotificationText.BuildSubject(notification));
    }
}
