using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace ConAI.Web.Infrastructure;

public static class RateLimitPolicies
{
    public const string Files = "files";
    public const string Generate = "generate";
    /// <summary>生成状況のポーリング用。3 秒間隔（20 回/分）に余裕を持たせる。</summary>
    public const string Status = "status";
    /// <summary>Live の接続確立用。1 セッションは数十分続くため、正常な使い方なら 1 分に数回で足りる。</summary>
    public const string Live = "live";

    /// <summary>議事録への質問用。1 回が軽く続けて聞く使い方をするため、生成（5 回）とは別枠にする。</summary>
    public const string Chat = "chat";

    /// <summary>会議横断の検索用。Gemini を呼ばず数ミリ秒で返るため、質問（10 回）とは別枠で広く取る。</summary>
    public const string Search = "search";

    /// <summary>手動送信とテスト送信。どちらも押すたびに外へ出ていくので、まとめて絞る。</summary>
    public const string Notifications = "notifications";

    public static IServiceCollection AddConAIRateLimiter(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(Files, context => FixedWindow(context, Files, permitLimit: 30));
            options.AddPolicy(Generate, context => FixedWindow(context, Generate, permitLimit: 5));
            options.AddPolicy(Status, context => FixedWindow(context, Status, permitLimit: 60));
            options.AddPolicy(Live, context => FixedWindow(context, Live, permitLimit: 10));
            options.AddPolicy(Chat, context => FixedWindow(context, Chat, permitLimit: 10));
            options.AddPolicy(Search, context => FixedWindow(context, Search, permitLimit: 60));
            options.AddPolicy(Notifications, context => FixedWindow(context, Notifications, permitLimit: 5));
        });

        return services;
    }

    // 区画キーにポリシー名を含める。上限だけで区切ると、同じ上限を持つポリシー同士
    // （Live と Chat はどちらも 10 回）が 1 つの枠を食い合う。
    private static RateLimitPartition<string> FixedWindow(HttpContext context, string policyName, int permitLimit)
    {
        var key = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            $"{policyName}:{permitLimit}:{key}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }
}
