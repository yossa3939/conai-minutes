using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ConAI.Web.Pages.Meetings;

[Authorize]
public class CreateModel : PageModel
{
    private readonly IMeetingService _meetings;
    private readonly IMinutesTemplateService _templates;

    public CreateModel(IMeetingService meetings, IMinutesTemplateService templates)
    {
        _meetings = meetings;
        _templates = templates;
    }

    [BindProperty]
    public Meeting Input { get; set; } = new();

    public IEnumerable<SelectListItem> Languages =>
        SupportedLanguages.All.Select(l => new SelectListItem(l.Name, l.Code));

    /// <summary>議事録テンプレートの選択肢。初期選択は利用者の既定。</summary>
    public IReadOnlyList<SelectListItem> MinutesTemplates { get; private set; } = [];

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        await LoadTemplatesAsync(null, cancellationToken);

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(Input.TargetLanguage))
        {
            ModelState.AddModelError("Input.TargetLanguage", "対応していない言語です。");
        }

        var ownerId = OwnerId;
        if (Input.MinutesTemplateId is { } templateId
            && !await _templates.OwnsAsync(templateId, ownerId, cancellationToken))
        {
            ModelState.AddModelError("Input.MinutesTemplateId", "選べないテンプレートです。");
        }

        if (!ModelState.IsValid)
        {
            // 再表示でも選択肢は要る。選べない Id は一覧に無いので、既定が選ばれた状態に戻る
            await LoadTemplatesAsync(Input.MinutesTemplateId, cancellationToken);
            return Page();
        }

        var created = await _meetings.CreateAsync(ownerId, new Meeting
        {
            Title = Input.Title,
            HeldAt = Input.HeldAt,
            LiveMode = Input.LiveMode,
            TranslateMode = Input.TranslateMode,
            TargetLanguage = Input.TargetLanguage,
            MinutesTemplateId = Input.MinutesTemplateId
        }, cancellationToken);

        return RedirectToPage("./Edit", new { id = created.Id });
    }

    private async Task LoadTemplatesAsync(Guid? requested, CancellationToken cancellationToken) =>
        MinutesTemplates = MinutesTemplateSelection.Build(
            await _templates.ListAsync(OwnerId, cancellationToken),
            requested);
}
