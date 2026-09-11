using Microsoft.AspNetCore.Antiforgery;

namespace ConAI.Web.Endpoints;

public sealed class AntiforgeryEndpointFilter : IEndpointFilter
{
    private static readonly string[] SafeMethods = ["GET", "HEAD", "OPTIONS", "TRACE"];

    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<AntiforgeryEndpointFilter> _logger;

    public AntiforgeryEndpointFilter(IAntiforgery antiforgery, ILogger<AntiforgeryEndpointFilter> logger)
    {
        _antiforgery = antiforgery;
        _logger = logger;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        if (SafeMethods.Contains(http.Request.Method))
        {
            return await next(context);
        }

        // form 系は UseAntiforgery も検証するが、form を束縛しないエンドポイント（本文なしの POST など）へ
        // form Content-Type で送られた要求はそこでは検証されない。内容種別に関わらずここで必ず検証する
        // （二重検証は無害。トークンはヘッダ X-CSRF-TOKEN か form 欄 __RequestVerificationToken のどちらでもよい）。

        try
        {
            await _antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException exception)
        {
            _logger.LogWarning(exception, "Antiforgery validation failed for {Path}", http.Request.Path);
            return Results.BadRequest(new { message = "リクエストの検証に失敗しました。画面を再読み込みしてください。" });
        }

        return await next(context);
    }
}
