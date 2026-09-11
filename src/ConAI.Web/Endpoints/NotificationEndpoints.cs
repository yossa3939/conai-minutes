using System.Security.Claims;
using ConAI.Web.Infrastructure;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.AspNetCore.RateLimiting;

namespace ConAI.Web.Endpoints;

public static class NotificationEndpoints
{
    private const string AcceptedMessage = "送信を受け付けました。結果は通知設定の画面で確認できます。";
    private const string NoMinutesMessage = "議事録がまだありません。生成してから送ってください。";
    private const string NoEndpointMessage = "送り先がありません。通知の設定で宛先を登録してください。";
    private const string TestSucceededMessage = "テスト通知を送りました。チャット側で届いているか確認してください。";
    private const string TestFailedMessage = "テスト通知を送れませんでした。";

    /// <summary>
    /// 会議の下と宛先の下に 1 本ずつ置くため、2 つの束を作る。
    /// 束が 2 つあると Program.cs 側での付け外しが読みにくくなるので、
    /// 検証フィルタと回数制限はここで両方に付ける。
    /// </summary>
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var meetings = routes.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();
        meetings.MapPost("/notify", NotifyAsync);

        var endpoints = routes.MapGroup("/api/notifications/{endpointId:guid}").RequireAuthorization();
        endpoints.MapPost("/test", TestAsync);

        foreach (var group in new[] { meetings, endpoints })
        {
            group.AddEndpointFilter<AntiforgeryEndpointFilter>();
            group.RequireRateLimiting(RateLimitPolicies.Notifications);
        }
    }

    private static async Task<IResult> NotifyAsync(
        Guid meetingId,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IWebhookEndpointService endpoints,
        INotificationQueue queue,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        var meeting = await meetings.GetAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null)
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(meeting.Minutes))
        {
            return Results.BadRequest(new { ok = false, message = NoMinutesMessage });
        }

        if (!await endpoints.HasEnabledAsync(ownerId, cancellationToken))
        {
            return Results.BadRequest(new { ok = false, message = NoEndpointMessage });
        }

        // 宛先が増えると送信は数秒かかる。押した人を待たせず、結果は通知設定の画面で見せる
        await queue.EnqueueAsync(new NotificationRequest(meetingId, NotificationEvent.Manual), cancellationToken);

        return Results.Accepted(value: new { ok = true, message = AcceptedMessage });
    }

    private static async Task<IResult> TestAsync(
        Guid endpointId,
        ClaimsPrincipal user,
        IWebhookEndpointService endpoints,
        INotificationDispatcher dispatcher,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (await endpoints.GetAsync(endpointId, ownerId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var result = await dispatcher.SendTestAsync(endpointId, ownerId, cancellationToken);
        var succeeded = result.Outcome == WebhookSendOutcome.Succeeded;

        return Results.Ok(new
        {
            ok = succeeded,
            message = succeeded ? TestSucceededMessage : result.Message ?? TestFailedMessage
        });
    }
}
