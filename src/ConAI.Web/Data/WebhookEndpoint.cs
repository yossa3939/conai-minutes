using System.ComponentModel.DataAnnotations;
using ConAI.Web.Services;

namespace ConAI.Web.Data;

public class WebhookEndpoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerId { get; set; } = string.Empty;

    [Required(ErrorMessage = "宛先の名前を入力してください。")]
    [StringLength(WebhookLimits.MaxNameChars, ErrorMessage = "宛先の名前は {1} 文字以内で入力してください。")]
    [Display(Name = "名前")]
    public string Name { get; set; } = string.Empty;

    /// <summary>宛先の種別。登録時に決め、以後は変更しない。</summary>
    [Display(Name = "種別")]
    public WebhookKind Kind { get; set; }

    /// <summary>Data Protection で保護した宛先 URL。平文では保存しない。</summary>
    public string ProtectedUrl { get; set; } = string.Empty;

    /// <summary>画面に出すマスク済みの断片。ホスト名と末尾 4 文字だけを含む。</summary>
    public string UrlHint { get; set; } = string.Empty;

    /// <summary>URL の SHA-256（16 進小文字）。同じ宛先の二重登録を DB 側で弾くために持つ。</summary>
    public string UrlFingerprint { get; set; } = string.Empty;

    [Display(Name = "生成に成功したとき送る")]
    public bool NotifyOnSuccess { get; set; } = true;

    [Display(Name = "生成に失敗したとき送る")]
    public bool NotifyOnFailure { get; set; } = true;

    [Display(Name = "有効")]
    public bool IsEnabled { get; set; } = true;

    public WebhookDeliveryStatus LastStatus { get; set; } = WebhookDeliveryStatus.None;

    public DateTime? LastAttemptedAt { get; set; }

    /// <summary>最後に失敗した理由（利用者向けの日本語）。成功したら null に戻す。</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
