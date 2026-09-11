using ConAI.Web.Data;

namespace ConAI.Web.Services;

public enum DeleteTemplateResult
{
    Deleted,
    NotFound,
    IsDefault
}

public enum UpdateTemplateResult
{
    Updated,
    NotFound,
    Invalid
}

public interface IMinutesTemplateService
{
    /// <summary>その利用者のテンプレートが 0 件なら「標準」「簡潔」を配る。入口のメソッドが先に呼ぶ。</summary>
    Task EnsureSeededAsync(string ownerId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MinutesTemplate>> ListAsync(string ownerId, CancellationToken cancellationToken);

    Task<MinutesTemplate?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>既定のテンプレート。配布のあとは必ず 1 件返る。</summary>
    Task<MinutesTemplate> GetDefaultAsync(string ownerId, CancellationToken cancellationToken);

    /// <summary>名前か本文が安全弁を超えていれば null を返す。</summary>
    Task<MinutesTemplate?> CreateAsync(string ownerId, string name, string body, CancellationToken cancellationToken);

    /// <summary>見つからない場合と内容が不正な場合を分けて返す。画面はこの区別で 404 と入力の案内を出し分ける。</summary>
    Task<UpdateTemplateResult> UpdateAsync(Guid id, string ownerId, string name, string body, CancellationToken cancellationToken);

    Task<MinutesTemplate?> DuplicateAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    Task<bool> SetDefaultAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>既定は消せない。消すときは使っていた会議を既定へ付け替えてから消す。</summary>
    Task<DeleteTemplateResult> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>削除の確認画面に出す、そのテンプレートを使っている会議の件数。</summary>
    Task<int> CountMeetingsAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>そのテンプレートを消したときに、使っていた会議が付け替わる先。残りが無ければ null。
    /// 確認画面と DeleteAsync が同じ選び方をするための共通の入口。</summary>
    Task<MinutesTemplate?> FindFallbackAsync(Guid excludedId, string ownerId, CancellationToken cancellationToken);

    /// <summary>Id が自分のテンプレートかどうか。配布はしない（保存前の検査に使う）。</summary>
    Task<bool> OwnsAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>生成に使う本文。会議が自分のテンプレートを指していればその本文、そうでなければ既定の本文。</summary>
    Task<string> ResolveForGenerationAsync(Meeting meeting, CancellationToken cancellationToken);
}
