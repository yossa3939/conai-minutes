using System.Security.Claims;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Chat;

/// <summary>根拠 1 件。MeetingId が null なら会議は消えている（決定 8）。</summary>
public sealed record ChatSourceView(Guid? MeetingId, string Title);

/// <summary>画面に描く 1 往復。答えは Markdown から HTML に変換済みである。</summary>
public sealed record ChatTurnView(
    Guid Id,
    string Question,
    string AnswerHtml,
    string CreatedAtText,
    IReadOnlyList<ChatSourceView> Sources);

[Authorize]
public class IndexModel : PageModel
{
    private readonly IChatService _chat;
    private readonly IMarkdownRenderer _markdown;

    public IndexModel(IChatService chat, IMarkdownRenderer markdown)
    {
        _chat = chat;
        _markdown = markdown;
    }

    public IReadOnlyList<ChatTurnView> Turns { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var turns = await _chat.ListAsync(ownerId, cancellationToken);

        Turns =
        [
            .. turns.Select(t => new ChatTurnView(
                t.Id,
                t.Question,
                _markdown.ToHtml(t.Answer),
                DisplayFormats.ToLocalText(t.CreatedAt),
                [.. t.Sources.Select(s => new ChatSourceView(s.MeetingId, s.MeetingTitle))]))
        ];
    }
}
