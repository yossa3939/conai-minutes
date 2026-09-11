using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Templates;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IMinutesTemplateService _templates;

    public IndexModel(IMinutesTemplateService templates) => _templates = templates;

    public IReadOnlyList<MinutesTemplate> Templates { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken) =>
        Templates = await _templates.ListAsync(OwnerId, cancellationToken);

    public async Task<IActionResult> OnPostDuplicateAsync(Guid id, CancellationToken cancellationToken)
    {
        var copy = await _templates.DuplicateAsync(id, OwnerId, cancellationToken);
        return copy is null ? NotFound() : RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetDefaultAsync(Guid id, CancellationToken cancellationToken) =>
        await _templates.SetDefaultAsync(id, OwnerId, cancellationToken) ? RedirectToPage() : NotFound();

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
