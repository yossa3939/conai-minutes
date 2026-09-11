using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Notifications;

[Authorize]
public class EditModel : PageModel
{
    private readonly IWebhookEndpointService _endpoints;

    public EditModel(IWebhookEndpointService endpoints) => _endpoints = endpoints;

    [BindProperty]
    public NotificationInput Input { get; set; } = new();

    public Guid Id { get; private set; }

    public WebhookKind Kind { get; private set; }

    public string UrlHint { get; private set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await _endpoints.GetAsync(id, OwnerId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        Load(endpoint);
        Input = new NotificationInput
        {
            Name = endpoint.Name,
            Kind = endpoint.Kind,
            // URL は平文で読み出せないので初期値を入れない。空のまま保存すれば据え置く
            Url = null,
            NotifyOnSuccess = endpoint.NotifyOnSuccess,
            NotifyOnFailure = endpoint.NotifyOnFailure,
            IsEnabled = endpoint.IsEnabled
        };

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var endpoint = await _endpoints.GetAsync(id, OwnerId, cancellationToken);
        if (endpoint is null)
        {
            return NotFound();
        }

        // 描き直しに使う読み取り専用の値は、検証で戻る前に埋める
        Load(endpoint);

        if (!ModelState.IsValid)
        {
            return Page();
        }

        // 種別は登録時のものを渡す。画面に入力欄が無くても、直接送られた値を通さない
        var (result, error) = await _endpoints.UpdateAsync(
            id,
            OwnerId,
            new WebhookEndpointEdit(
                Input.Name, endpoint.Kind, Input.Url, Input.NotifyOnSuccess, Input.NotifyOnFailure, Input.IsEnabled),
            cancellationToken);

        switch (result)
        {
            case SaveEndpointResult.Saved:
                return RedirectToPage("./Index");
            case SaveEndpointResult.NotFound:
                return NotFound();
            default:
                ModelState.AddModelError(string.Empty, error ?? "この内容では保存できません。");
                return Page();
        }
    }

    private void Load(WebhookEndpoint endpoint)
    {
        Id = endpoint.Id;
        Kind = endpoint.Kind;
        UrlHint = endpoint.UrlHint;
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
