using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ConAI.Web.Pages.Meetings;

[Authorize]
public class EditModel : PageModel
{
    private readonly IMeetingService _meetings;
    private readonly IMarkdownRenderer _markdown;
    private readonly IMinutesTemplateService _templates;

    public EditModel(IMeetingService meetings, IMarkdownRenderer markdown, IMinutesTemplateService templates)
    {
        _meetings = meetings;
        _markdown = markdown;
        _templates = templates;
    }

    public Meeting Meeting { get; private set; } = new();

    /// <summary>議事録の Markdown を描画した HTML。閲覧タブと同じ表示に使う。</summary>
    public string MinutesHtml { get; private set; } = string.Empty;

    /// <summary>音声・動画欄の accept 属性。許す拡張子は AllowedFileTypes が唯一の正。</summary>
    public string MediaAccept => string.Join(",", AllowedFileTypes.ExtensionsFor(MeetingFileKind.Media));

    /// <summary>参考資料欄の accept 属性。許す拡張子は AllowedFileTypes が唯一の正。</summary>
    public string ReferenceAccept => string.Join(",", AllowedFileTypes.ExtensionsFor(MeetingFileKind.Reference));

    [BindProperty]
    public BasicInputModel Basic { get; set; } = new();

    [BindProperty]
    public TranscriptInputModel Transcript { get; set; } = new();

    [BindProperty]
    [StringLength(MeetingLimits.MaxTextChars, ErrorMessage = "議事録が長すぎます。")]
    public string? Minutes { get; set; }

    /// <summary>保存に成功したあとの再表示で 1 回だけ出す文。</summary>
    [TempData]
    public string? SavedMessage { get; set; }

    public bool CanGenerate { get; private set; }

    public string? GenerateDisabledReason { get; private set; }

    public IEnumerable<SelectListItem> Languages =>
        SupportedLanguages.All.Select(l => new SelectListItem(l.Name, l.Code));

    /// <summary>議事録テンプレートの選択肢。生成ボタンの横に出す。</summary>
    public IReadOnlyList<SelectListItem> MinutesTemplates { get; private set; } = [];

    public sealed class BasicInputModel
    {
        [Required(ErrorMessage = "会議名を入力してください。")]
        [StringLength(200, ErrorMessage = "会議名は 200 文字以内で入力してください。")]
        [Display(Name = "会議名")]
        public string Title { get; set; } = string.Empty;

        [Display(Name = "開催日時")]
        [DisplayFormat(DataFormatString = DisplayFormats.HeldAtEdit, ApplyFormatInEditMode = true)]
        public DateTime? HeldAt { get; set; }

        [Display(Name = "モード")]
        public bool LiveMode { get; set; }

        [Display(Name = "翻訳モード")]
        public bool TranslateMode { get; set; }

        [Display(Name = "翻訳先言語")]
        public string TargetLanguage { get; set; } = SupportedLanguages.Default;

        [Display(Name = "テンプレート")]
        public Guid? MinutesTemplateId { get; set; }
    }

    public sealed class TranscriptInputModel
    {
        // 空の textarea は空文字で送られ、モデルバインダが null に変換するため null 許容にする。
        // 非 null にすると暗黙の必須検証で空の会議が保存できなくなる（OnPostAsync で空文字に寄せる）。
        [StringLength(MeetingLimits.MaxTextChars, ErrorMessage = "文字起こしが長すぎます。")]
        [Display(Name = "文字起こし")]
        public string? Transcription { get; set; }

        [StringLength(MeetingLimits.MaxTextChars, ErrorMessage = "文字起こしが長すぎます。")]
        [Display(Name = "翻訳された文字起こし")]
        public string? TranslatedTranscription { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        Basic = new BasicInputModel
        {
            Title = Meeting.Title,
            HeldAt = Meeting.HeldAt,
            LiveMode = Meeting.LiveMode,
            TranslateMode = Meeting.TranslateMode,
            TargetLanguage = Meeting.TargetLanguage,
            MinutesTemplateId = Meeting.MinutesTemplateId
        };
        Transcript = new TranscriptInputModel
        {
            Transcription = Meeting.Transcription,
            TranslatedTranscription = Meeting.TranslatedTranscription
        };
        Minutes = Meeting.Minutes;
        return Page();
    }

    /// <summary>画面の「保存」1 本で、基本情報・文字起こし・議事録をまとめて保存する（RV 3）。</summary>
    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(Basic.TargetLanguage))
        {
            ModelState.AddModelError("Basic.TargetLanguage", "対応していない言語です。");
        }

        // 画面で止める。ここを抜けても UpdateAsync が false を返し、404 として二重に止まる
        if (Basic.MinutesTemplateId is { } templateId
            && !await _templates.OwnsAsync(templateId, OwnerId, cancellationToken))
        {
            ModelState.AddModelError("Basic.MinutesTemplateId", "選べないテンプレートです。");
        }

        if (!ModelState.IsValid)
        {
            await ForgiveUnchangedOverLimitAsync(id, cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            // 入力値は ModelState に残るので、読み直した会議と合わせて再表示する
            return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
        }

        var ownerId = OwnerId;
        var updated = await _meetings.UpdateAsync(id, ownerId, new MeetingEdit(
            Basic.Title,
            Basic.HeldAt,
            Basic.LiveMode,
            Basic.TranslateMode,
            Basic.TargetLanguage,
            Transcript.Transcription ?? string.Empty,
            Transcript.TranslatedTranscription ?? string.Empty,
            Minutes ?? string.Empty,
            Basic.MinutesTemplateId), cancellationToken);

        if (!updated)
        {
            return NotFound();
        }

        SavedMessage = "保存しました。";
        return RedirectToPage("./Edit", new { id });
    }

    /// <summary>
    /// 保存済みの本文と同じ値に対する「長すぎます」を取り下げる。
    /// 録音の追記には上限が無いため、保存済みの文字起こしが上限を超えることがある。
    /// そのままでは会議名ひとつ直せない会議ができてしまうので、値を変えない保存だけ通す。
    /// 上限を超える値を新しく書き込む道は塞いだままにする。
    /// </summary>
    private async Task ForgiveUnchangedOverLimitAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await _meetings.GetAsync(id, OwnerId, cancellationToken);
        if (stored is null)
        {
            return;
        }

        Forgive("Transcript.Transcription", Transcript.Transcription, stored.Transcription);
        Forgive("Transcript.TranslatedTranscription", Transcript.TranslatedTranscription, stored.TranslatedTranscription);
        Forgive("Minutes", Minutes, stored.Minutes);

        void Forgive(string key, string? posted, string storedValue)
        {
            if (!ModelState.TryGetValue(key, out var entry) || entry.Errors.Count == 0)
            {
                return;
            }

            if (posted is null
                || posted.Length <= MeetingLimits.MaxTextChars
                || !string.Equals(posted, storedValue, StringComparison.Ordinal))
            {
                return;
            }

            ModelState.ClearValidationState(key);
            ModelState.MarkFieldValid(key);
        }
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var meeting = await _meetings.GetAsync(id, OwnerId, cancellationToken);
        if (meeting is null)
        {
            return false;
        }

        Meeting = meeting;
        MinutesHtml = _markdown.ToHtml(meeting.Minutes);
        // 送られた値が先。GET では Basic が空なので会議の値、それも無ければ既定になる（規則 3）
        await LoadTemplatesAsync(Basic.MinutesTemplateId ?? meeting.MinutesTemplateId, cancellationToken);
        EvaluateGenerateButton();
        return true;
    }

    private async Task LoadTemplatesAsync(Guid? requested, CancellationToken cancellationToken) =>
        MinutesTemplates = MinutesTemplateSelection.Build(
            await _templates.ListAsync(OwnerId, cancellationToken),
            requested);

    private void EvaluateGenerateButton()
    {
        var hasMedia = Meeting.Files.Any(f => f.Kind is MeetingFileKind.Media or MeetingFileKind.Recording);
        var hasTranscript = !string.IsNullOrWhiteSpace(Meeting.Transcription);

        if (Meeting.GenerationStatus is GenerationStatus.Queued or GenerationStatus.Running)
        {
            CanGenerate = false;
            GenerateDisabledReason = "議事録を生成しています。しばらくお待ちください。";
            return;
        }

        if (Meeting.LiveMode)
        {
            CanGenerate = hasTranscript || hasMedia;
            GenerateDisabledReason = CanGenerate ? null : "録音するか、音声・動画を追加してください";
            return;
        }

        CanGenerate = hasMedia;
        GenerateDisabledReason = CanGenerate ? null : "音声・動画を追加してください";
    }
}
