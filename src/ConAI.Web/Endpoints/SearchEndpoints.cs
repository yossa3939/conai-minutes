using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ConAI.Web.Search;

namespace ConAI.Web.Endpoints;

public sealed record SearchAskRequest(string? Query, int? Page);

public sealed record SearchResultResponse(
    Guid MeetingId, string Title, string HeldAtText, string Excerpt, string MatchedIn);

public sealed record SearchResponse(
    string Status,
    int Total,
    int Page,
    int PageSize,
    int IndexingRemaining,
    IReadOnlyList<SearchResultResponse> Results,
    IReadOnlyList<string> Notes);

public static class SearchEndpoints
{
    private const string EmptyQueryMessage = "検索語を入力してください。";
    private const string UnreadableBodyMessage = "リクエストの本文を読み取れませんでした。";

    private static readonly string TooLongMessage =
        $"検索語は {SearchLimits.MaxQueryChars.ToString("N0", CultureInfo.InvariantCulture)} 文字以内で入力してください。";

    /// <summary>
    /// /api/chat とは別のグループにする。グループ単位で掛かる質問のレート制限（1 分 10 回）を
    /// 検索と食い合わせないためで、エンドポイント単位の上書きに頼ると、
    /// グループとエンドポイントのどちらの規約が後に適用されるかという内部の順序に依存する。
    /// </summary>
    public static RouteGroupBuilder MapSearchEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/search").RequireAuthorization();
        group.MapPost("/", SearchAsync);

        return group;
    }

    private static async Task<IResult> SearchAsync(
        HttpContext http,
        ClaimsPrincipal user,
        IMeetingSearchService search,
        CancellationToken cancellationToken)
    {
        // 本文は [FromBody] で束縛しない。最小 API の束縛はエンドポイントフィルタより先に動くため、
        // form 形式の POST が AntiforgeryEndpointFilter の 400 に届かず 415 になってしまう。
        if (!http.Request.HasJsonContentType())
        {
            return Results.BadRequest(new { message = UnreadableBodyMessage });
        }

        SearchAskRequest? request;

        try
        {
            request = await http.Request.ReadFromJsonAsync<SearchAskRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { message = UnreadableBodyMessage });
        }

        var query = request?.Query?.Trim() ?? string.Empty;

        if (query.Length == 0)
        {
            return Results.BadRequest(new { message = EmptyQueryMessage });
        }

        if (query.Length > SearchLimits.MaxQueryChars)
        {
            return Results.BadRequest(new { message = TooLongMessage });
        }

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var outcome = await search.SearchAsync(ownerId, query, request?.Page ?? 1, cancellationToken);

        // 検索は外部サービスを呼ばないので 502 の経路が無い。
        return Results.Ok(ToResponse(outcome));
    }

    private static SearchResponse ToResponse(SearchOutcome outcome) =>
        new(
            outcome.Status.ToString().ToLowerInvariant(),
            outcome.Total,
            outcome.Page,
            SearchLimits.PageSize,
            outcome.IndexingRemaining,
            [.. outcome.Hits.Select(hit => new SearchResultResponse(
                hit.MeetingId,
                hit.Title,
                // 書式は閲覧画面と同じものを使う。並べて見える 2 つの画面で桁が食い違うと、
                // 同じ会議の別の日時に見える。
                hit.HeldAt?.ToString(DisplayFormats.HeldAt, CultureInfo.InvariantCulture) ?? string.Empty,
                hit.Excerpt,
                hit.MatchedIn.ToString().ToLowerInvariant()))],
            outcome.Notes);
}
