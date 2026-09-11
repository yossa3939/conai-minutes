using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ConAI.Web.Pages.Templates;

[Authorize]
public class DeleteModel : PageModel
{
    private readonly IMinutesTemplateService _templates;

    public DeleteModel(IMinutesTemplateService templates) => _templates = templates;

    public MinutesTemplate Template { get; private set; } = new();

    public bool CanDelete { get; private set; }

    /// <summary>使っている会議の件数を知らせる文。</summary>
    public string ConfirmMessage { get; private set; } = string.Empty;

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!await LoadAsync(id, cancellationToken))
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await _templates.DeleteAsync(id, OwnerId, cancellationToken);
        switch (result)
        {
            case DeleteTemplateResult.Deleted:
                return RedirectToPage("./Index");
            case DeleteTemplateResult.IsDefault:
                // 画面のボタンは無効だが、直接送られても消さない。確認画面を出し直す
                return await LoadAsync(id, cancellationToken) ? Page() : NotFound();
            default:
                return NotFound();
        }
    }

    private async Task<bool> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        var template = await _templates.GetAsync(id, OwnerId, cancellationToken);
        if (template is null)
        {
            return false;
        }

        Template = template;
        CanDelete = !template.IsDefault;
        ErrorMessage = CanDelete
            ? null
            : "既定のテンプレートは削除できません。先に別のテンプレートを既定にしてください。";

        var count = await _templates.CountMeetingsAsync(id, OwnerId, cancellationToken);

        // 付け替え先は削除が実際に使うものを聞く。GetDefaultAsync は既定の行が欠けていると
        // 名前順の先頭へ落ち、これから消すテンプレート自身の名前を出しうる。
        var fallback = await _templates.FindFallbackAsync(id, OwnerId, cancellationToken);
        ConfirmMessage = (count, fallback) switch
        {
            (0, _) => "このテンプレートを使う会議はありません。",
            // 残りが 1 件も無い＝これが最後の 1 件。付け替え先が無いので会議は空になり、
            // 次の生成のときに配り直された既定へ落ちる。
            (_, null) => $"このテンプレートを使う会議が {count} 件あります。削除すると、それらの会議のテンプレートは空になり、次の生成では新しく配られる既定を使います。",
            _ => $"このテンプレートを使う会議が {count} 件あります。削除すると、それらの会議は「{fallback.Name}」に戻ります。"
        };
        return true;
    }

    private string OwnerId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
}
