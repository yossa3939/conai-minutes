namespace ConAI.Web.Notifications;

/// <summary>通知の見出し。</summary>
public static class NotificationHeadings
{
    public static string Text(NotificationEvent notificationEvent) => notificationEvent switch
    {
        NotificationEvent.Succeeded => "議事録ができました",
        NotificationEvent.Failed => "議事録の生成に失敗しました",
        NotificationEvent.Manual => "議事録を共有します",
        NotificationEvent.Test => "ConAI からのテスト通知です",
        _ => throw new ArgumentOutOfRangeException(nameof(notificationEvent), notificationEvent, "未知の通知種別です。")
    };
}
