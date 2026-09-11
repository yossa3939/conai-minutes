using System.Security.Claims;
using System.Text.Json;
using ConAI.Web.Infrastructure;
using ConAI.Web.Live;
using ConAI.Web.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace ConAI.Web.Endpoints;

public sealed record GenerationStatusDto(
    string Status,
    string Error,
    string Transcription,
    string TranslatedTranscription,
    string Minutes);

/// <summary>生成開始の本文。省略可で、省略時は会議に保存済みのテンプレートを使う。</summary>
public sealed record GenerationStartRequest(Guid? MinutesTemplateId);

public static class GenerationEndpoints
{
    public static RouteGroupBuilder MapGenerationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/meetings/{meetingId:guid}/generation").RequireAuthorization();

        // ポーリング（GET）が生成開始（POST）の枠を食い潰さないよう、書き込み系と読み取り系で枠を分ける。
        group.MapPost("/", StartAsync).RequireRateLimiting(RateLimitPolicies.Generate);
        group.MapGet("/", GetStatusAsync).RequireRateLimiting(RateLimitPolicies.Status);

        return group;
    }

    private static async Task<IResult> StartAsync(
        Guid meetingId,
        HttpContext http,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IMinutesTemplateService templates,
        IGenerationQueue queue,
        ILiveSessionRegistry registry,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (await meetings.GetAsync(meetingId, ownerId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        // 本文は [FromBody] で束縛しない。最小 API の束縛はエンドポイントフィルタより先に動くため、
        // form 形式の POST が AntiforgeryEndpointFilter の 400 に届かず 415 になってしまう。
        GenerationStartRequest? request = null;
        if (http.Request.HasJsonContentType())
        {
            try
            {
                request = await http.Request.ReadFromJsonAsync<GenerationStartRequest>(cancellationToken);
            }
            catch (JsonException)
            {
                return Results.BadRequest(new { message = "リクエストの本文を読み取れませんでした。" });
            }
        }

        // 選べないテンプレートは録音中の判定より先に断る。
        // ただし断る側の判定だけを先に置き、会議への保存は待機にできてから行う。
        var templateId = request?.MinutesTemplateId;
        if (templateId is { } requested
            && !await templates.OwnsAsync(requested, ownerId, cancellationToken))
        {
            return Results.BadRequest(new { message = "選べないテンプレートです。" });
        }

        // 録音中に生成を始めると、Live が追記中の文字起こしを結果で上書きしてしまう。
        if (registry.IsActive(meetingId))
        {
            return Results.Conflict(new { message = "録音中は議事録を生成できません。録音を終了してからお試しください。" });
        }

        if (!await meetings.TryMarkQueuedAsync(meetingId, ownerId, cancellationToken))
        {
            return Results.Conflict(new { message = "すでに生成中です。完了までお待ちください。" });
        }

        // 409 で断った要求がテンプレートだけ書き換えては、実行中の生成が別の書式で出てしまう。
        // 待機にできた要求だけが会議を書き換える。
        if (templateId is { } accepted)
        {
            switch (await meetings.SetMinutesTemplateAsync(meetingId, ownerId, accepted, cancellationToken))
            {
                case SetTemplateResult.TemplateNotAllowed:
                    // 上の検査との間にテンプレートが消えた。待機の印を残すと、この会議は
                    // 生成も削除もできなくなる（起動時の後始末まで戻らない）ので、失敗にしてから断る。
                    await meetings.MarkFailedAsync(meetingId, "選べないテンプレートです。", cancellationToken);
                    return Results.BadRequest(new { message = "選べないテンプレートです。" });
                case SetTemplateResult.MeetingNotFound:
                    // 直前の存在確認との間に会議が消えた場合。待機の印は消えた会議とともに落ちる。
                    return Results.NotFound();
            }
        }

        await queue.EnqueueAsync(meetingId, cancellationToken);

        return Results.Accepted($"/api/meetings/{meetingId}/generation");
    }

    private static async Task<IResult> GetStatusAsync(
        Guid meetingId,
        ClaimsPrincipal user,
        IMeetingService meetings,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var meeting = await meetings.GetAsync(meetingId, ownerId, cancellationToken);

        return meeting is null
            ? Results.NotFound()
            : Results.Ok(new GenerationStatusDto(
                meeting.GenerationStatus.ToString(),
                meeting.GenerationError ?? string.Empty,
                meeting.Transcription ?? string.Empty,
                meeting.TranslatedTranscription ?? string.Empty,
                meeting.Minutes ?? string.Empty));
    }
}
