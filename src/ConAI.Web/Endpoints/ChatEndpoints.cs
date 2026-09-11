using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using ConAI.Web.Data;
using ConAI.Web.Search;
using ConAI.Web.Services;

namespace ConAI.Web.Endpoints;

public sealed record ChatAskRequest(string? Question, IReadOnlyList<Guid>? MeetingIds);

/// <summary>根拠 1 件。会議が消えていると MeetingId は null で、写し取った会議名だけが残る。</summary>
public sealed record ChatSourceDto(Guid? MeetingId, string Title);

public sealed record ChatTurnDto(
    Guid Id,
    string Question,
    string AnswerHtml,
    string CreatedAtText,
    IReadOnlyList<ChatSourceDto> Sources);

public sealed record ChatAskResponse(string Status, ChatTurnDto? Turn, IReadOnlyList<string> Notes);

public static class ChatEndpoints
{
    private const string AnsweredStatus = "answered";
    private const string NoMatchStatus = "no-match";
    private const string NoMeetingsStatus = "no-meetings";

    private const string EmptyQuestionMessage = "質問を入力してください。";
    private const string UnreadableBodyMessage = "リクエストの本文を読み取れませんでした。";
    private const string UpstreamFailureMessage = "答えを作れませんでした。しばらくしてからもう一度お試しください。";
    private const string TimeoutMessage = "答えが返るまでに時間がかかりすぎました。しばらくしてからもう一度お試しください。";

    private static readonly string LimitReachedMessage =
        $"やりとりが上限（{ChatLimits.MaxTurns.ToString("N0", CultureInfo.InvariantCulture)} 往復）に達しました。"
        + "「会話を消す」で全件消してから続けてください。";

    private static readonly string TooLongMessage =
        $"質問は {ChatLimits.MaxQuestionChars.ToString("N0", CultureInfo.InvariantCulture)} 文字以内で入力してください。";

    private static readonly string TooManyMeetingsMessage =
        $"根拠にする会議は {SearchLimits.MaxAnswerMeetings.ToString(CultureInfo.InvariantCulture)} 件以内で指定してください。";

    public static RouteGroupBuilder MapChatEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/chat").RequireAuthorization();
        group.MapPost("/", AskAsync);
        group.MapDelete("/", ClearAsync);

        return group;
    }

    private static async Task<IResult> AskAsync(
        HttpContext http,
        ClaimsPrincipal user,
        IChatService chat,
        IMarkdownRenderer markdown,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // 本文は [FromBody] で束縛しない。最小 API の束縛はエンドポイントフィルタより先に動くため、
        // form 形式の POST が AntiforgeryEndpointFilter の 400 に届かず 415 になってしまう。
        if (!http.Request.HasJsonContentType())
        {
            return Results.BadRequest(new { message = UnreadableBodyMessage });
        }

        ChatAskRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<ChatAskRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return Results.BadRequest(new { message = UnreadableBodyMessage });
        }

        var question = request?.Question?.Trim() ?? string.Empty;
        if (question.Length == 0)
        {
            return Results.BadRequest(new { message = EmptyQuestionMessage });
        }

        if (question.Length > ChatLimits.MaxQuestionChars)
        {
            return Results.BadRequest(new { message = TooLongMessage });
        }

        // 画面が送るのは上位 10 件までで、AskAsync もそこまでしか根拠にしない。
        // 読み捨てる件数の配列を、本文として受け取り切ってしまうのを防ぐ。
        if (request?.MeetingIds is { Count: > SearchLimits.MaxAnswerMeetings })
        {
            return Results.BadRequest(new { message = TooManyMeetingsMessage });
        }

        ChatAskOutcome outcome;
        try
        {
            // 省略時は空として扱い、選抜の経路へ落とす
            outcome = await chat.AskAsync(ownerId, question, request?.MeetingIds ?? [], cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 呼び出し元のトークンが生きているなら、切ったのは 60 秒か 120 秒の打ち切りである。
            return Upstream(loggers, ownerId, null, TimeoutMessage);
        }
        catch (OperationCanceledException)
        {
            // 利用者が離脱した場合。応答を作らずそのまま抜ける。
            throw;
        }
        catch (Exception exception)
        {
            return Upstream(loggers, ownerId, exception, UpstreamFailureMessage);
        }

        return outcome.Status switch
        {
            ChatAskStatus.LimitReached => Results.BadRequest(new { message = LimitReachedMessage }),
            // 該当なしと議事録なしは失敗ではない。画面に出す言い分けは status で伝える。
            ChatAskStatus.NoMeetings => Results.Ok(new ChatAskResponse(NoMeetingsStatus, null, outcome.Notes)),
            ChatAskStatus.NoMatch => Results.Ok(new ChatAskResponse(NoMatchStatus, null, outcome.Notes)),
            _ => Results.Ok(new ChatAskResponse(AnsweredStatus, ToDto(outcome.Turn!, markdown), outcome.Notes))
        };
    }

    private static async Task<IResult> ClearAsync(
        ClaimsPrincipal user, IChatService chat, CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        // 消す往復が無くても 204 を返す。無いことは失敗ではない。
        await chat.ClearAsync(ownerId, cancellationToken);

        return Results.NoContent();
    }

    private static ChatTurnDto ToDto(ChatTurn turn, IMarkdownRenderer markdown) => new(
        turn.Id,
        turn.Question,
        markdown.ToHtml(turn.Answer),
        DisplayFormats.ToLocalText(turn.CreatedAt),
        [.. turn.Sources.OrderBy(s => s.Order).Select(s => new ChatSourceDto(s.MeetingId, s.MeetingTitle))]);

    private static IResult Upstream(
        ILoggerFactory loggers, string ownerId, Exception? exception, string message)
    {
        // 質問文と議事録はログに出さない。
        loggers.CreateLogger(typeof(ChatEndpoints))
            .LogWarning(exception, "議事録への質問に失敗しました。ownerId={OwnerId}", ownerId);

        return Results.Json(new { message }, statusCode: StatusCodes.Status502BadGateway);
    }
}
