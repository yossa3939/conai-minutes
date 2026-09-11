using System.Runtime.CompilerServices;
using ConAI.Web.Configuration;
using Google.GenAI;
using Google.GenAI.Types;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Gemini;

public sealed class GoogleGeminiLiveClient : IGeminiLiveClient
{
    private readonly GeminiOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GoogleGeminiLiveClient> _logger;

    public GoogleGeminiLiveClient(
        IOptions<GeminiOptions> options,
        TimeProvider timeProvider,
        ILogger<GoogleGeminiLiveClient> logger)
    {
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<IGeminiLiveSession> ConnectAsync(
        LiveSessionSettings settings,
        string? resumptionHandle,
        CancellationToken cancellationToken)
    {
        var client = new Client(apiKey: _options.ApiKey);

        var config = BuildConfig(settings, _options.UseSessionResumption, resumptionHandle);

        var session = await client.Live.ConnectAsync(settings.Model, config, cancellationToken);
        _logger.LogInformation("Gemini Live session connected with model {Model}", settings.Model);

        // 翻訳モードは "no tools or instructions" 制約があり手動区切りを検証できていないため、
        // 自動検出のままにする（pacer を付けない）。
        var pacer = settings.Translate ? null : new LiveActivityPacer(_timeProvider);
        return new GoogleGeminiLiveSession(session, _timeProvider, pacer, _logger);
    }

    /// <summary>
    /// Live 接続の設定どおりに <see cref="LiveConnectConfig"/> を組み立てる（純粋関数）。
    /// </summary>
    public static LiveConnectConfig BuildConfig(
        LiveSessionSettings settings,
        bool useSessionResumption,
        string? resumptionHandle)
    {
        var config = new LiveConnectConfig
        {
            // TEXT を応答モダリティに指定すると 1007 で切断されるため、AUDIO ＋ 入力文字起こしにする。
            ResponseModalities = [Modality.Audio],
            InputAudioTranscription = new AudioTranscriptionConfig()
        };

        if (settings.Translate)
        {
            // 翻訳モデルは "no tools or instructions"。SystemInstruction・圧縮・MaxOutputTokens は付けない。
            config.TranslationConfig = new TranslationConfig
            {
                TargetLanguageCode = settings.TargetLanguage
            };
            // 訳文は OutputTranscription で届く。この設定が無いと Gemini は返さない。
            config.OutputAudioTranscription = new AudioTranscriptionConfig();
        }
        else
        {
            config.ContextWindowCompression = new ContextWindowCompressionConfig
            {
                SlidingWindow = new SlidingWindow()
            };
            config.SystemInstruction = new Content
            {
                Parts = [Part.FromText("発話をそのまま書き起こします。返答は最小限にします。")]
            };
            // 応答音声を抑止する（実機計測: 設定ありで 0 バイト、無しで約 321 KB）。
            config.MaxOutputTokens = 1;
            // サーバ側 VAD の発話区切りでしか入力文字起こしが返らず、PC 音声には切れ目が立たないため、
            // 自動検出では文字がなかなか返らない（実測: 44 秒の録音で最初の文字が 9.2 秒・最大間隔 25.9 秒）。
            // 自動検出を切り、LiveActivityPacer で約 2 秒ごとに手動で区切ると 2.5 秒間隔で返る。
            // 手動区切りには中身の無い区間で Gemini が幻覚の文字を返す副作用があるため、
            // pacer が音量の門（RMS -45 dBFS）で中身の無い区間を区切らないようにしている。
            config.RealtimeInputConfig = new RealtimeInputConfig
            {
                AutomaticActivityDetection = new AutomaticActivityDetection { Disabled = true }
            };
        }

        if (useSessionResumption)
        {
            config.SessionResumption = new SessionResumptionConfig { Handle = resumptionHandle };
        }

        return config;
    }
}

public sealed class GoogleGeminiLiveSession : IGeminiLiveSession
{
    private readonly AsyncSession _session;
    private readonly TimeProvider _timeProvider;
    private readonly LiveActivityPacer? _pacer;
    private readonly ILogger _logger;

    public GoogleGeminiLiveSession(
        AsyncSession session,
        TimeProvider timeProvider,
        LiveActivityPacer? pacer,
        ILogger logger)
    {
        _session = session;
        _timeProvider = timeProvider;
        _pacer = pacer;
        _logger = logger;
    }

    public async Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        if (_pacer is null)
        {
            await _session.SendRealtimeInputAsync(BuildRealtimeInput(pcm), cancellationToken);
            return;
        }

        await SendPacerActionAsync(_pacer.OnAudio(pcm), cancellationToken);
    }

    /// <summary>
    /// 音声の送信終了を伝える。自動検出のときは Gemini の audioStreamEnd を送り、発話の切れ目を
    /// 検出するまで返らない入力文字起こしを停止時に受け取る。手動検出のときは audioStreamEnd を
    /// 送ってはならない（SDK の説明どおり）ため、溜めた分を吐き出して ActivityEnd で閉じる。
    /// </summary>
    public async Task EndAudioAsync(CancellationToken cancellationToken)
    {
        if (_pacer is null)
        {
            await _session.SendRealtimeInputAsync(
                new LiveSendRealtimeInputParameters { AudioStreamEnd = true },
                cancellationToken);
            return;
        }

        // モデルが応答中に ActivityStart を送ると黙って無視されて音声が落ちるため、
        // TurnComplete を待つ。ResumeTimeout で必ず解けるので、このループは有限回で抜ける。
        while (_pacer.IsBuffering)
        {
            var resumed = _pacer.TryResume();
            if (resumed is not null)
            {
                await SendPacerActionAsync(resumed, cancellationToken);
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(32), _timeProvider, cancellationToken);
        }

        await SendPacerActionAsync(_pacer.OnStop(), cancellationToken);
    }

    /// <summary>pacer が返した手順を ActivityStart → 音声 → ActivityEnd の順に送る。</summary>
    private async Task SendPacerActionAsync(LivePacerAction action, CancellationToken cancellationToken)
    {
        if (action.SendActivityStart)
        {
            await _session.SendRealtimeInputAsync(
                new LiveSendRealtimeInputParameters { ActivityStart = new ActivityStart() },
                cancellationToken);
        }

        foreach (var audio in action.AudioToSend)
        {
            await _session.SendRealtimeInputAsync(BuildRealtimeInput(audio), cancellationToken);
        }

        if (action.SendActivityEnd)
        {
            await _session.SendRealtimeInputAsync(
                new LiveSendRealtimeInputParameters { ActivityEnd = new ActivityEnd() },
                cancellationToken);
        }
    }

    /// <summary>
    /// 音声チャンクを Live API の realtime_input に組み立てる（純粋関数）。
    /// Media（realtime_input.media_chunks）は Gemini 側で非推奨になり、送ると InvalidPayloadData で
    /// セッションを閉じられる（2026-08-25 実機確認）。Audio（realtime_input.audio）で送る。
    /// </summary>
    public static LiveSendRealtimeInputParameters BuildRealtimeInput(ReadOnlyMemory<byte> pcm) =>
        new()
        {
            Audio = new Blob
            {
                Data = pcm.ToArray(),
                MimeType = "audio/pcm;rate=16000"
            }
        };

    public async IAsyncEnumerable<LiveServerEvent> ReceiveAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // ReceiveAsync は 1 メッセージずつ返し、接続が正常に閉じると null を返す。
        while (true)
        {
            var message = await _session.ReceiveAsync(cancellationToken);
            if (message is null)
            {
                yield break;
            }

            if (message.ServerContent?.InputTranscription?.Text is { Length: > 0 } transcript)
            {
                yield return new LiveServerEvent(LiveEventKind.Transcript, transcript);
            }

            if (message.ServerContent?.OutputTranscription?.Text is { Length: > 0 } translation)
            {
                yield return new LiveServerEvent(LiveEventKind.Translation, translation);
            }

            if (message.ServerContent?.TurnComplete == true)
            {
                // ターンが終わってもループは抜けない。抜けるとセッションが即終了する。
                _pacer?.OnTurnComplete();
                yield return new LiveServerEvent(LiveEventKind.TurnComplete);
            }

            if (message.GoAway is not null)
            {
                yield return new LiveServerEvent(LiveEventKind.GoAway);
            }

            if (message.SessionResumptionUpdate?.NewHandle is { Length: > 0 } handle)
            {
                yield return new LiveServerEvent(LiveEventKind.ResumptionUpdate, ResumptionHandle: handle);
            }

            // モデル音声（message.ServerContent.ModelTurn の音声パート）は捨てる。文字だけを使う。
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _session.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to dispose Gemini Live session");
        }
    }
}
