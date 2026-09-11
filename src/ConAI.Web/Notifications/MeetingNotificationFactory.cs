using ConAI.Web.Configuration;
using ConAI.Web.Data;

namespace ConAI.Web.Notifications;

public static class MeetingNotificationFactory
{
    private const string UnknownError = "詳しい理由は記録されていません。";
    private const string UntitledMeeting = "（名前のない会議）";

    public static MeetingNotification Create(Meeting meeting, NotificationEvent notificationEvent, NotificationOptions options)
    {
        var failed = notificationEvent == NotificationEvent.Failed;

        return new MeetingNotification(
            notificationEvent,
            string.IsNullOrWhiteSpace(meeting.Title) ? UntitledMeeting : meeting.Title.Trim(),
            meeting.HeldAt,
            // 失敗したときは議事録が無い。空の抜粋を送るより、理由だけを出すほうが読みやすい
            failed ? string.Empty : MinutesExcerpt.Build(meeting.Minutes, options.ExcerptChars),
            BuildUrl(options.PublicBaseUrl, meeting.Id),
            failed ? meeting.GenerationError ?? UnknownError : null);
    }

    public static MeetingNotification CreateTest(NotificationOptions options) =>
        new(NotificationEvent.Test,
            "ConAI",
            HeldAt: null,
            "この通知が届いていれば、宛先の設定は正しく動いています。",
            BuildUrl(options.PublicBaseUrl, meetingId: null),
            ErrorMessage: null);

    private static string? BuildUrl(string baseUrl, Guid? meetingId)
    {
        var trimmed = baseUrl.Trim().TrimEnd('/');
        if (trimmed.Length == 0)
        {
            return null;
        }

        return meetingId is { } id ? $"{trimmed}/Meetings/Details/{id}" : trimmed;
    }
}
