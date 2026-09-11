using ConAI.Web.Data;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ConAI.Web.Pages.Meetings;

/// <summary>会議の作成画面と編集画面が共有する、議事録テンプレートの選択欄。</summary>
internal static class MinutesTemplateSelection
{
    /// <summary>選ばれる Id は「送られた値 → 既定」の順に決める。一覧に無い Id は既定へ落とす。
    /// 既定は一覧の中から見つける。ListAsync が配布まで済ませているので、これで問い合わせが 1 回で済む。</summary>
    public static IReadOnlyList<SelectListItem> Build(IReadOnlyList<MinutesTemplate> templates, Guid? requested)
    {
        // 一覧は名前順。既定の行が失われていても選択が空にならないよう、先頭へ落とす。
        var fallback = templates.FirstOrDefault(t => t.IsDefault) ?? templates.FirstOrDefault();
        var selected = requested is { } id && templates.Any(t => t.Id == id) ? id : fallback?.Id;

        return templates
            .Select(t => new SelectListItem(t.Name, t.Id.ToString(), t.Id == selected))
            .ToList();
    }
}
