using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Meetings;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly IMeetingService _meetings;
    private readonly IFileStorageService _storage;

    public DeleteModel(IMeetingService meetings, IFileStorageService storage)
    {
        _meetings = meetings;
        _storage = storage;
    }

    public Meeting Meeting { get; private set; } = new();

    public bool CanDelete { get; private set; }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var meeting = await _meetings.GetAsync(id, OwnerId, cancellationToken);
        if (meeting is null)
        {
            return NotFound();
        }

        Meeting = meeting;
        CanDelete = meeting.GenerationStatus is not (GenerationStatus.Queued or GenerationStatus.Running);
        if (!CanDelete)
        {
            ErrorMessage = "議事録を生成しています。完了してから削除してください。";
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _meetings.DeleteAsync(id, OwnerId, cancellationToken);
        switch (result)
        {
            case DeleteResult.Deleted:
                _storage.DeleteMeetingDirectory(id);
                return RedirectToPage("./Index");
            case DeleteResult.JobInProgress:
                return StatusCode(StatusCodes.Status409Conflict);
            default:
                return NotFound();
        }
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
