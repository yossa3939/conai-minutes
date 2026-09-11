using System.ComponentModel.DataAnnotations;
using ConAI.Web.Services;

namespace ConAI.Web.Pages.Templates;

/// <summary>作成と編集で共有する入力ボックス。OwnerId と IsDefault を束縛させないため、エンティティとは別に持つ。</summary>
public sealed class TemplateInput
{
    [Required(ErrorMessage = "名前を入力してください。")]
    [StringLength(MinutesTemplateLimits.MaxNameChars, ErrorMessage = "名前は 100 文字以内で入力してください。")]
    [Display(Name = "名前")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "本文を入力してください。")]
    [StringLength(MinutesTemplateLimits.MaxBodyChars, ErrorMessage = "本文が長すぎます。")]
    [Display(Name = "本文")]
    public string Body { get; set; } = string.Empty;
}
