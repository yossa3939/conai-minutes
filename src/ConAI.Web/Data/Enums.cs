namespace ConAI.Web.Data;

public enum GenerationStatus
{
    None = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4
}

public enum MeetingFileKind
{
    Media = 0,
    Reference = 1,
    Recording = 2
}

/// <summary>Webhook 宛先のチャットサービス。登録時に決め、以後は変更しない。</summary>
public enum WebhookKind
{
    Slack = 0,
    Discord = 1,
    MicrosoftTeams = 2,
    GoogleChat = 3
}

/// <summary>宛先への最後の送信結果。</summary>
public enum WebhookDeliveryStatus
{
    None = 0,
    Succeeded = 1,
    Failed = 2
}
