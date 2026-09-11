using System.Security.Claims;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Templates;

[Authorize]
public class EditModel : PageModel
{
    private readonly IMinutesTemplateService _templates;

    public EditModel(IMinutesTemplateService templates) => _templates = templates;

    [BindProperty]
    public TemplateInput Input { get; set; } = new();

    public Guid Id { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _templates.GetAsync(id, OwnerId, cancellationToken);
        if (template is null)
        {
            return NotFound();
        }

        Id = id;
        Input = new TemplateInput { Name = template.Name, Body = template.Body };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        Id = id;
        if (!ModelState.IsValid)
        {
            return Page();
        }

        switch (await _templates.UpdateAsync(id, OwnerId, Input.Name, Input.Body, cancellationToken))
        {
            case UpdateTemplateResult.NotFound:
                return NotFound();
            case UpdateTemplateResult.Invalid:
                // 属性の検証を抜けてもサービスの安全弁が断ることがある（空白だけの入力など）
                ModelState.AddModelError(string.Empty, "この内容では保存できません。名前と本文を確かめてください。");
                return Page();
            case UpdateTemplateResult.Updated:
                return RedirectToPage("./Index");
            default:
                // 断る理由が増えたときに、黙って保存できたことにしない
                throw new InvalidOperationException("保存の結果を判別できません。");
        }
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
