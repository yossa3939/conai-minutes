using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ConAI.Web.Configuration;
using ConAI.Web.Gemini;
using ConAI.Web.Services;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Live;

public sealed record LiveSessionContext(Guid MeetingId, bool Translate, string TargetLanguage);

public interface ILiveSessionService
{
    Task RunAsync(WebSocket socket, LiveSessionContext context, CancellationToken cancellationToken);
}

public sealed class LiveSessionService : ILiveSessionService
{
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 停止時に音声の終了を伝えてから最後の文字起こしを待つ上限。受信が途切れないまま延びても、
    /// ここで打ち切って保存へ進む。GoAway の drain（GoAwayDrainTimeout）と同じ 3 秒にする。
    /// </summary>
    public static readonly TimeSpan StopDrainTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// 停止後の受信が途切れたと見なすまでの静穏の窓。実測の応答が約 0.2 秒なので、その倍を置く。
    /// </summary>
    public static readonly TimeSpan StopQuietPeriod = TimeSpan.FromMilliseconds(500);

    private static readonly TimeSpan GoAwayDrainTimeout = TimeSpan.FromSeconds(3);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int ReceiveBufferBytes = 32 * 1024;
    private const int MaxMessageBytes = 1024 * 1024;

    private readonly IGeminiLiveClient _client;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly GeminiOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LiveSessionService> _logger;

    private readonly SemaphoreSlim _flushLock = new(1, 1);
    private readonly StringBuilder _pendingTranscript = new();
    private readonly StringBuilder _pendingTranslation = new();

    private IGeminiLiveSession? _session;
    private string? _resumptionHandle;

    /// <summary>Gemini から受け取ったイベントの数。停止時の待ちが、受信の途切れを見分けるために使う。</summary>
    private long _eventsSeen;

    /// <summary>3 つのループから並行に読まれ、GoAway での切り替え時に書き換わる。可視性を保証するため Volatile 経由にする。</summary>
    private IGeminiLiveSession? Session
    {
        get => Volatile.Read(ref _session);
        set => Volatile.Write(ref _session, value);
    }

    /// <summary>クライアントへ返す close コード。既定は正常終了で、上限超過のときだけ差し替える。</summary>
    private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;

    public LiveSessionService(
        IGeminiLiveClient client,
        IServiceScopeFactory scopeFactory,
        IOptions<GeminiOptions> options,
        TimeProvider timeProvider,
        ILogger<LiveSessionService> logger)
    {
        _client = client;
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task RunAsync(WebSocket socket, LiveSessionContext context, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var settings = new LiveSessionSettings(
            context.Translate ? _options.LiveTranslateModel : _options.LiveModel,
            context.Translate,
            context.TargetLanguage);

        try
        {
            Session = await _client.ConnectAsync(settings, resumptionHandle: null, linked.Token);
            await SendAsync(socket, "Info", "接続しました。話し始めてください。", linked.Token);

            var clientLoop = ClientLoopAsync(socket, linked.Token);
            var geminiLoop = GeminiLoopAsync(socket, settings, linked.Token);
            var saveLoop = SaveLoopAsync(context.MeetingId, linked.Token);

            await Task.WhenAny(clientLoop, geminiLoop, saveLoop);
            await linked.CancelAsync();
            await Task.WhenAll(Quiet(clientLoop), Quiet(geminiLoop), Quiet(saveLoop));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Live session failed for meeting {MeetingId}", context.MeetingId);
            await TrySendErrorAsync(socket, "接続に問題が発生しました。もう一度お試しください。");
        }
        finally
        {
            await FlushAsync(context.MeetingId, CancellationToken.None);

            if (Session is not null)
            {
                await Session.DisposeAsync();
            }

            await TryCloseAsync(socket);
        }
    }

    private async Task ClientLoopAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReceiveBufferBytes];
        var message = new MemoryStream();

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await DrainAfterStopAsync(cancellationToken);
                return;
            }

            if (result.MessageType != WebSocketMessageType.Binary)
            {
                // 文字メッセージは使わない。無視して受信を続ける。
                continue;
            }

            message.Write(buffer, 0, result.Count);

            if (message.Length > MaxMessageBytes)
            {
                // 32 ms の PCM は 1KB 程度で、超えるのは異常なクライアントだけ。
                _logger.LogWarning("Client message exceeded {MaxMessageBytes} bytes. Closing with MessageTooBig.", MaxMessageBytes);
                _closeStatus = WebSocketCloseStatus.MessageTooBig;
                return;
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            await Session!.SendAudioAsync(message.ToArray(), cancellationToken);
            message.SetLength(0);
        }
    }

    /// <summary>
    /// 停止時の後始末。Gemini は区間の終わりを受け取るまで入力文字起こしを返さないので、
    /// 音声の終わりを伝えてから受信が途切れるまで待ち、最後の発話を取りこぼさないようにする
    /// （伝え方は EndAudioAsync が決める。文字起こしモードは ActivityEnd、翻訳モードは audioStreamEnd。
    /// 受信そのものは GeminiLoopAsync が続けている）。
    ///
    /// 待ちの合図に TurnComplete は使わない。TurnComplete は発話の切れ目ごとに返るため、
    /// 停止直前の発話がまだ文字になっていない時点でも届きうる。合図に使うと、その続きを捨てて保存へ進む。
    /// 代わりに <see cref="StopQuietPeriod"/> のあいだ何も届かないことを合図にし、
    /// 受信が途切れない場合に備えて <see cref="StopDrainTimeout"/> で打ち切る。
    /// </summary>
    private async Task DrainAfterStopAsync(CancellationToken cancellationToken)
    {
        var session = Session;
        if (session is null)
        {
            return;
        }

        try
        {
            await session.EndAudioAsync(cancellationToken);
        }
        // 取り消しだけは握らずに投げる。ここまでに届いた文字は RunAsync の finally が
        // CancellationToken.None で保存するので、投げても取りこぼしにはならない。
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to signal the end of audio. Saving what has arrived so far.");
            return;
        }

        using var timeout = new CancellationTokenSource(StopDrainTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token, cancellationToken);

        while (true)
        {
            var before = Volatile.Read(ref _eventsSeen);

            try
            {
                await Task.Delay(StopQuietPeriod, _timeProvider, linked.Token);
            }
            catch (OperationCanceledException)
            {
                // 上限に達したか、セッション自体が止められた。届いた分だけ保存して終える。
                return;
            }

            if (Volatile.Read(ref _eventsSeen) == before)
            {
                return;
            }
        }
    }

    private async Task GeminiLoopAsync(WebSocket socket, LiveSessionSettings settings, CancellationToken cancellationToken)
    {
        var retried = false;

        while (!cancellationToken.IsCancellationRequested)
        {
            var switching = false;

            await foreach (var serverEvent in Session!.ReceiveAsync(cancellationToken))
            {
                if (serverEvent.Kind == LiveEventKind.GoAway)
                {
                    await SendAsync(socket, "Info", "接続を張り直しています。", cancellationToken);
                    await CutOverAsync(socket, settings, cancellationToken);
                    switching = true;
                    break;
                }

                await HandleEventAsync(socket, serverEvent, cancellationToken);
            }

            if (switching)
            {
                // 予告された切り替えは異常ではないので、再試行の枠を消費しない。
                retried = false;
                continue;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (retried)
            {
                await SendAsync(socket, "Error", "接続が切れました。もう一度録音を開始してください。", cancellationToken);
                return;
            }

            retried = true;
            _logger.LogWarning("Gemini live stream ended unexpectedly. Reconnecting once.");
            await ReplaceSessionAsync(settings, cancellationToken);
        }
    }

    private async Task HandleEventAsync(WebSocket socket, LiveServerEvent serverEvent, CancellationToken cancellationToken)
    {
        // 種類を問わず数える。停止時の待ちは、この数が動かなくなったことを受信の途切れと見なす。
        Interlocked.Increment(ref _eventsSeen);

        switch (serverEvent.Kind)
        {
            // 文字起こしモデルは日本語のトークン間に空白を入れて返すので、蓄積と配信の前に除く（spec §6.3）
            case LiveEventKind.Transcript when TranscriptNormalizer.Normalize(serverEvent.Text ?? string.Empty) is { Length: > 0 } transcript:
                Append(_pendingTranscript, transcript);
                await SendAsync(socket, "Transcript", transcript, cancellationToken);
                break;

            case LiveEventKind.Translation when TranscriptNormalizer.Normalize(serverEvent.Text ?? string.Empty) is { Length: > 0 } translation:
                Append(_pendingTranslation, translation);
                await SendAsync(socket, "Translation", translation, cancellationToken);
                break;

            case LiveEventKind.TurnComplete:
                await SendAsync(socket, "TurnComplete", string.Empty, cancellationToken);
                break;

            case LiveEventKind.ResumptionUpdate:
                _resumptionHandle = serverEvent.ResumptionHandle;
                break;
        }
    }

    private void Append(StringBuilder builder, string text)
    {
        lock (builder)
        {
            builder.Append(text);
            if (!text.EndsWith('\n'))
            {
                builder.Append('\n');
            }
        }
    }

    private async Task CutOverAsync(WebSocket socket, LiveSessionSettings settings, CancellationToken cancellationToken)
    {
        var old = Session!;
        Session = await _client.ConnectAsync(settings, _resumptionHandle, cancellationToken);

        using var drain = new CancellationTokenSource(GoAwayDrainTimeout, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(drain.Token, cancellationToken);

        try
        {
            await foreach (var serverEvent in old.ReceiveAsync(linked.Token))
            {
                await HandleEventAsync(socket, serverEvent, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 3 秒で読み切れなければ諦める。
        }
        finally
        {
            await old.DisposeAsync();
        }
    }

    private async Task ReplaceSessionAsync(LiveSessionSettings settings, CancellationToken cancellationToken)
    {
        var old = Session;
        Session = await _client.ConnectAsync(settings, _resumptionHandle, cancellationToken);

        if (old is not null)
        {
            await old.DisposeAsync();
        }
    }

    private async Task SaveLoopAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(SaveInterval, _timeProvider);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            await FlushAsync(meetingId, cancellationToken);
        }
    }

    private async Task FlushAsync(Guid meetingId, CancellationToken cancellationToken)
    {
        await _flushLock.WaitAsync(cancellationToken);

        try
        {
            string transcript;
            string translation;

            lock (_pendingTranscript)
            {
                transcript = _pendingTranscript.ToString();
                _pendingTranscript.Clear();
            }

            lock (_pendingTranslation)
            {
                translation = _pendingTranslation.ToString();
                _pendingTranslation.Clear();
            }

            if (transcript.Length == 0 && translation.Length == 0)
            {
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await meetings.AppendTranscriptAsync(meetingId, transcript, translation, cancellationToken);
        }
        finally
        {
            _flushLock.Release();
        }
    }

    private static Task SendAsync(WebSocket socket, string type, string text, CancellationToken cancellationToken)
    {
        // CloseReceived（相手からの close 待ちの半閉鎖）でも応答の close を返すまでは送れる。
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return Task.CompletedTask;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(new { type, text }, JsonOptions);
        return socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task TrySendErrorAsync(WebSocket socket, string text)
    {
        try
        {
            await SendAsync(socket, "Error", text, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to send error frame");
        }
    }

    private async Task TryCloseAsync(WebSocket socket)
    {
        try
        {
            if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    _closeStatus,
                    _closeStatus == WebSocketCloseStatus.MessageTooBig ? "message too big" : "done",
                    CancellationToken.None);
            }
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Failed to close client socket");
        }
    }

    private static async Task Quiet(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }
}
