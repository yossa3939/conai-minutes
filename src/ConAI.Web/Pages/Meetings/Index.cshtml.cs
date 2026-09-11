using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Meetings;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IMeetingService _meetings;

    public IndexModel(IMeetingService meetings) => _meetings = meetings;

    public IReadOnlyList<Meeting> Meetings { get; private set; } = Array.Empty<Meeting>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        Meetings = await _meetings.ListAsync(ownerId, cancellationToken);
    }
}
