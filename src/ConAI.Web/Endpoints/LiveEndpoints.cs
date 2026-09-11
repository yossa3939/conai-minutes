using System.Security.Claims;
using ConAI.Web.Configuration;
using ConAI.Web.Infrastructure;
using ConAI.Web.Live;
using ConAI.Web.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Endpoints;

public static class LiveEndpoints
{
    public static RouteHandlerBuilder MapLiveEndpoints(this IEndpointRouteBuilder routes) =>
        routes.MapGet("/api/live/transcribe", TranscribeAsync)
            .RequireAuthorization()
            // 同時数制限は「つないでは切る」の連打を止められない。接続確立そのものに枠を掛ける。
            .RequireRateLimiting(RateLimitPolicies.Live);

    private static async Task<IResult> TranscribeAsync(
        Guid meetingId,
        HttpContext context,
        ClaimsPrincipal user,
        IMeetingService meetings,
        ILiveSessionRegistry registry,
        ILiveSessionService session,
        IOptions<LiveOptions> liveOptions,
        CancellationToken cancellationToken)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            return Results.BadRequest(new { message = "WebSocket 接続でのみ利用できます。" });
        }

        if (!IsAllowedOrigin(context, liveOptions.Value))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var meeting = await meetings.GetAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null)
        {
            return Results.NotFound();
        }

        if (!registry.TryAcquire(ownerId, meetingId))
        {
            return Results.StatusCode(StatusCodes.Status409Conflict);
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            var liveContext = new LiveSessionContext(meetingId, meeting.TranslateMode, meeting.TargetLanguage);
            await session.RunAsync(socket, liveContext, cancellationToken);
        }
        finally
        {
            registry.Release(ownerId, meetingId);
        }

        return Results.Empty;
    }

    private static bool IsAllowedOrigin(HttpContext context, LiveOptions options)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (string.IsNullOrEmpty(origin))
        {
            return false;
        }

        var allowed = options.AllowedOrigins.Length > 0
            ? options.AllowedOrigins
            : new[] { $"{context.Request.Scheme}://{context.Request.Host.Value}" };

        return allowed.Contains(origin, StringComparer.Ordinal);
    }
}
