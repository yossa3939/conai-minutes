namespace ConAI.Web.Notifications;

/// <summary>宛先の種別によらない通知の中身。種別ごとの見た目は payload builder が作る。</summary>
public sealed record MeetingNotification(
    NotificationEvent Event,
    string Title,
    DateTime? HeldAt,
    string Excerpt,
    string? Url,
    string? ErrorMessage);

/// <summary>待ち行列に積む 1 件。宛先はここでは決めず、取り出したときに購読設定で絞る。</summary>
public sealed record NotificationRequest(Guid MeetingId, NotificationEvent Event);
