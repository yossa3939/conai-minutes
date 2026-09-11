using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Notifications;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly IWebhookEndpointService _endpoints;

    public DeleteModel(IWebhookEndpointService endpoints) => _endpoints = endpoints;

    public WebhookEndpoint Endpoint { get; private set; } = new();

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await _endpoints.GetAsync(id, OwnerId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        Endpoint = endpoint;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken) =>
        await _endpoints.DeleteAsync(id, OwnerId, cancellationToken)
            ? RedirectToPage("./Index")
            : NotFound();

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
