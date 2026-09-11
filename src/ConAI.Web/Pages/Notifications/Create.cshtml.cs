using System.Security.Claims;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Notifications;

[Authorize]
public class CreateModel : PageModel
{
    private readonly IWebhookEndpointService _endpoints;

    public CreateModel(IWebhookEndpointService endpoints) => _endpoints = endpoints;

    [BindProperty]
    public NotificationInput Input { get; set; } = new();

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
        var (result, error) = await _endpoints.CreateAsync(
            ownerId,
            new WebhookEndpointEdit(
                Input.Name, Input.Kind, Input.Url, Input.NotifyOnSuccess, Input.NotifyOnFailure, Input.IsEnabled),
            cancellationToken);

        if (result == SaveEndpointResult.Saved)
        {
            return RedirectToPage("./Index");
        }

        // URL の形・重複・件数上限は、サービスが利用者向けの日本語で理由を返す
        ModelState.AddModelError(string.Empty, error ?? "この内容では保存できません。");
        return Page();
    }
}
