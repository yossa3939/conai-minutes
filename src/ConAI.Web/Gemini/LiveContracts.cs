namespace ConAI.Web.Gemini;

public sealed record LiveSessionSettings(string Model, bool Translate, string TargetLanguage);

public enum LiveEventKind
{
    Transcript = 0,
    Translation = 1,
    TurnComplete = 2,
    GoAway = 3,
    ResumptionUpdate = 4
}

public sealed record LiveServerEvent(LiveEventKind Kind, string? Text = null, string? ResumptionHandle = null);

public interface IGeminiLiveClient
{
    Task<IGeminiLiveSession> ConnectAsync(
        LiveSessionSettings settings,
        string? resumptionHandle,
        CancellationToken cancellationToken);
}

public interface IGeminiLiveSession : IAsyncDisposable
{
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken);

    /// <summary>音声の送信終了を伝える。以後は音声を送らず、残りの文字起こしを受け取るだけになる。</summary>
    Task EndAudioAsync(CancellationToken cancellationToken);

    IAsyncEnumerable<LiveServerEvent> ReceiveAsync(CancellationToken cancellationToken);
}
