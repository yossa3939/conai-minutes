namespace ConAI.Web.Notifications;

/// <summary>通知のきっかけ。見出しの文言と、宛先の絞り込みがこれで決まる。</summary>
public enum NotificationEvent
{
    Succeeded = 0,
    Failed = 1,
    Manual = 2,
    Test = 3
}
