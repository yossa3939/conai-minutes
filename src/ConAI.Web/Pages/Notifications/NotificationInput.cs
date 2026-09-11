using System.ComponentModel.DataAnnotations;
using ConAI.Web.Data;
using ConAI.Web.Services;

namespace ConAI.Web.Pages.Notifications;

/// <summary>作成と編集で共有する入力ボックス。OwnerId と送信結果を束縛させないため、エンティティとは別に持つ。</summary>
public sealed class NotificationInput
{
    [Required(ErrorMessage = "宛先の名前を入力してください。")]
    [StringLength(WebhookLimits.MaxNameChars, ErrorMessage = "宛先の名前は {1} 文字以内で入力してください。")]
    [Display(Name = "名前")]
    public string Name { get; set; } = string.Empty;

    [Display(Name = "種別")]
    public WebhookKind Kind { get; set; } = WebhookKind.Slack;

    /// <summary>
    /// 宛先 URL。編集で空なら保存済みの URL を据え置く。
    /// 妥当性は <c>WebhookUrlValidator</c> が見るので、ここでは長さだけを断る。
    /// </summary>
    [StringLength(WebhookLimits.MaxUrlChars, ErrorMessage = "宛先 URL が長すぎます。")]
    [DataType(DataType.Password)]
    [Display(Name = "宛先 URL")]
    public string? Url { get; set; }

    [Display(Name = "議事録の生成に成功したとき送る")]
    public bool NotifyOnSuccess { get; set; } = true;

    [Display(Name = "議事録の生成に失敗したとき送る")]
    public bool NotifyOnFailure { get; set; } = true;

    [Display(Name = "有効にする")]
    public bool IsEnabled { get; set; } = true;
}
