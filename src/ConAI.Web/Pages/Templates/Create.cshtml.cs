using System.Security.Claims;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Templates;

[Authorize]
public class CreateModel : PageModel
{
    private readonly IMinutesTemplateService _templates;

    public CreateModel(IMinutesTemplateService templates) => _templates = templates;

    [BindProperty]
    public TemplateInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var created = await _templates.CreateAsync(ownerId, Input.Name, Input.Body, cancellationToken);
        if (created is null)
        {
            // 属性の検証を抜けてもサービスの安全弁が断ることがある（空白だけの入力など）
            ModelState.AddModelError(string.Empty, "この内容では保存できません。名前と本文を確かめてください。");
            return Page();
        }

        return RedirectToPage("./Index");
    }
}
