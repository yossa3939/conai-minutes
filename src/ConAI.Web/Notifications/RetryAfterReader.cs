using System.Text.Json;

namespace ConAI.Web.Notifications;

/// <summary>429 のときに、どれだけ待てばよいかを読む。</summary>
public static class RetryAfterReader
{
    /// <summary>相手が長い待ち時間を返しても、ここで頭打ちにする。</summary>
    public static TimeSpan Max { get; } = TimeSpan.FromSeconds(60);

    public static TimeSpan? Read(HttpResponseMessage response, string? body, TimeProvider timeProvider)
    {
        var fromHeader = FromHeader(response, timeProvider);
        var fromBody = FromBody(body);

        // Discord はヘッダーと本文の両方に値を入れ、食い違うことがある。
        // 短いほうを採ると再び 429 を踏むので、長いほうに合わせる
        var wait = (fromHeader, fromBody) switch
        {
            (null, null) => (TimeSpan?)null,
            (null, { } b) => b,
            ({ } h, null) => h,
            ({ } h, { } b) => h > b ? h : b
        };

        if (wait is not { } value || value <= TimeSpan.Zero)
        {
            return null;
        }

        return value > Max ? Max : value;
    }

    private static TimeSpan? FromHeader(HttpResponseMessage response, TimeProvider timeProvider)
    {
        var header = response.Headers.RetryAfter;
        if (header is null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta;
        }

        return header.Date is { } date ? date - timeProvider.GetUtcNow() : null;
    }

    private static TimeSpan? FromBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            // TryGetDouble は数値以外の要素に対して例外を投げる。種別を先に確かめる
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("retry_after", out var element)
                && element.ValueKind == JsonValueKind.Number
                && element.TryGetDouble(out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        catch (JsonException)
        {
            // 本文が JSON でないことはある。ヘッダーだけで判断する
        }

        return null;
    }
}
