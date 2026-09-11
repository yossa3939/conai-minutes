using ConAI.Web.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace ConAI.Web.Tests;

public class SecurityHeadersMiddlewareTests
{
    /// <summary>OnStarting のコールバックを溜めておき、テストから明示的に発火させる応答フィーチャ。</summary>
    private sealed class RecordingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _callbacks = [];

        public override void OnStarting(Func<object, Task> callback, object state) =>
            _callbacks.Add((callback, state));

        public async Task FireAsync()
        {
            foreach (var (callback, state) in _callbacks)
            {
                await callback(state);
            }
        }
    }

    private static (DefaultHttpContext Context, RecordingResponseFeature Feature) CreateContext()
    {
        var feature = new RecordingResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        features.Set<IHttpResponseFeature>(feature);
        return (new DefaultHttpContext(features), feature);
    }

    [Fact]
    public async Task 応答が途中で捨てられてもセキュリティヘッダが付く()
    {
        var (context, feature) = CreateContext();

        // UseExceptionHandler の Response.Clear() 相当。
        var middleware = new SecurityHeadersMiddleware(ctx =>
        {
            ctx.Response.Headers.Clear();
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await feature.FireAsync();

        Assert.Contains("script-src 'self'", context.Response.Headers["Content-Security-Policy"].ToString());
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"].ToString());
    }
}
