using System.ComponentModel.DataAnnotations;
using ConAI.Web.Services;

namespace ConAI.Web.Data;

/// <summary>議事録の出力フォーマットの見本。利用者ごとにコピーを持つため、組み込みかどうかを表す列は持たない。</summary>
public class MinutesTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerId { get; set; } = string.Empty;

    [Required(ErrorMessage = "名前を入力してください。")]
    [StringLength(MinutesTemplateLimits.MaxNameChars, ErrorMessage = "名前は 100 文字以内で入力してください。")]
    [Display(Name = "名前")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "本文を入力してください。")]
    [StringLength(MinutesTemplateLimits.MaxBodyChars, ErrorMessage = "本文が長すぎます。")]
    [Display(Name = "本文")]
    public string Body { get; set; } = string.Empty;

    /// <summary>利用者ごとに 1 件だけ true。DB 側は IsDefault = 1 に絞った一意索引で守る。</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
