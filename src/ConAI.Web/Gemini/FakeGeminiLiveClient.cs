using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace ConAI.Web.Gemini;

public sealed class FakeGeminiLiveClient : IGeminiLiveClient
{
    public Task<IGeminiLiveSession> ConnectAsync(
        LiveSessionSettings settings,
        string? resumptionHandle,
        CancellationToken cancellationToken) =>
        Task.FromResult<IGeminiLiveSession>(new FakeGeminiLiveSession(settings));
}

public sealed class FakeGeminiLiveSession : IGeminiLiveSession
{
    // 16kHz・16bit モノラルの 1 秒ぶん。この量が溜まるたびに 1 発話を返す。
    private const int BytesPerUtterance = 32000;

    private readonly Channel<LiveServerEvent> _events =
        Channel.CreateUnbounded<LiveServerEvent>(new UnboundedChannelOptions { SingleReader = true });

    private readonly LiveSessionSettings _settings;
    private long _received;
    private int _utteranceNumber;

    public FakeGeminiLiveSession(LiveSessionSettings settings) => _settings = settings;

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        _received += pcm.Length;

        while (_received >= BytesPerUtterance)
        {
            _received -= BytesPerUtterance;
            EmitUtterance();
            _events.Writer.TryWrite(new LiveServerEvent(LiveEventKind.TurnComplete));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 音声の送信終了（audioStreamEnd）。1 発話に満たない端数が溜まっていれば、それを 1 発話として返す。
    /// 本物の Gemini は溜まった発話が無いとき audioStreamEnd の後に何も返さないが、Fake は必ず TurnComplete を返す。
    /// 単体テストと E2E がサーバ側の停止待ち上限（LiveSessionService.StopDrainTimeout）を待たずに済むためで、
    /// 上限で打ち切る経路は LiveSessionServiceTests の台本付きセッションで別途担保する。
    /// </summary>
    public Task EndAudioAsync(CancellationToken cancellationToken)
    {
        if (_received > 0)
        {
            _received = 0;
            EmitUtterance();
        }

        _events.Writer.TryWrite(new LiveServerEvent(LiveEventKind.TurnComplete));

        return Task.CompletedTask;
    }

    private void EmitUtterance()
    {
        var number = ++_utteranceNumber;

        _events.Writer.TryWrite(new LiveServerEvent(LiveEventKind.Transcript, $"テスト文字起こし{number}。"));

        if (_settings.Translate)
        {
            _events.Writer.TryWrite(new LiveServerEvent(LiveEventKind.Translation, $"Fake transcript {number}."));
        }
    }

    public async IAsyncEnumerable<LiveServerEvent> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var serverEvent in _events.Reader.ReadAllAsync(cancellationToken))
        {
            yield return serverEvent;
        }
    }

    public ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
