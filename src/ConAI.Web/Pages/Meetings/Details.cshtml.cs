using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Meetings;

[Authorize]
public class DetailsModel : PageModel
{
    private readonly IMeetingService _meetings;
    private readonly IMarkdownRenderer _markdown;
    private readonly IWebhookEndpointService _endpoints;

    public DetailsModel(IMeetingService meetings, IMarkdownRenderer markdown, IWebhookEndpointService endpoints)
    {
        _meetings = meetings;
        _markdown = markdown;
        _endpoints = endpoints;
    }

    public Meeting Meeting { get; private set; } = new();

    public string MinutesHtml { get; private set; } = string.Empty;

    /// <summary>手動送信のボタンを出すか。押しても意味のない状態では出さない。</summary>
    public bool CanNotify { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var meeting = await _meetings.GetAsync(id, ownerId, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        Meeting = meeting;
        MinutesHtml = _markdown.ToHtml(meeting.Minutes);
        CanNotify = !string.IsNullOrWhiteSpace(meeting.Minutes)
            && await _endpoints.HasEnabledAsync(ownerId, cancellationToken);

        return Page();
    }
}
