using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Notifications;

[Authorize]
public class IndexModel : PageModel
{
    private readonly IWebhookEndpointService _endpoints;

    public IndexModel(IWebhookEndpointService endpoints) => _endpoints = endpoints;

    public IReadOnlyList<WebhookEndpoint> Endpoints { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        Endpoints = await _endpoints.ListAsync(ownerId, cancellationToken);
    }

    /// <summary>最終送信結果の 1 行。成功なら日時だけ、失敗なら日時と理由を出す。</summary>
    public static string LastResultText(WebhookEndpoint endpoint)
    {
        if (endpoint.LastAttemptedAt is not { } attemptedAt)
        {
            return "まだ送っていません";
        }

        var when = DisplayFormats.ToLocalText(attemptedAt);

        return endpoint.LastStatus == WebhookDeliveryStatus.Succeeded
            ? $"{when} 成功"
            : $"{when} 失敗：{endpoint.LastError}";
    }
}
