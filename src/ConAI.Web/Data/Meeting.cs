using System.ComponentModel.DataAnnotations;

namespace ConAI.Web.Data;

public class Meeting
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string OwnerId { get; set; } = string.Empty;

    [Required(ErrorMessage = "会議名を入力してください。")]
    [StringLength(200, ErrorMessage = "会議名は 200 文字以内で入力してください。")]
    [Display(Name = "会議名")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "開催日時")]
    [DisplayFormat(DataFormatString = DisplayFormats.HeldAtEdit, ApplyFormatInEditMode = true)]
    public DateTime? HeldAt { get; set; }

    [Display(Name = "モード")]
    public bool LiveMode { get; set; } = true;

    [Display(Name = "翻訳モード")]
    public bool TranslateMode { get; set; }

    [StringLength(10, ErrorMessage = "翻訳先言語のコードは 10 文字以内で入力してください。")]
    [Display(Name = "翻訳先言語")]
    public string TargetLanguage { get; set; } = SupportedLanguages.Default;

    public string Transcription { get; set; } = string.Empty;

    public string TranslatedTranscription { get; set; } = string.Empty;

    public string Minutes { get; set; } = string.Empty;

    /// <summary>議事録の生成に使うテンプレート。null は「既定を使う」の意味。</summary>
    public Guid? MinutesTemplateId { get; set; }

    public MinutesTemplate? MinutesTemplate { get; set; }

    public GenerationStatus GenerationStatus { get; set; } = GenerationStatus.None;

    public string? GenerationError { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<MeetingFile> Files { get; set; } = new();
}
