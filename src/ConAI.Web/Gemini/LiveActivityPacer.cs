using System.Runtime.InteropServices;

namespace ConAI.Web.Gemini;

/// <summary>送信側が実行する手順。ActivityStart → 音声 → ActivityEnd の順に実行する。</summary>
public sealed record LivePacerAction(
    bool SendActivityStart,
    IReadOnlyList<ReadOnlyMemory<byte>> AudioToSend,
    bool SendActivityEnd);

/// <summary>
/// Gemini Live の手動の発話区切りを決める判定器。ネットワーク I/O を持たず、時刻は
/// <see cref="TimeProvider"/> から取る。送信側（OnAudio / TryResume / OnStop）と
/// 受信側（OnTurnComplete）の 2 つのタスクから呼ばれるため、内部状態は lock で守る。
/// </summary>
public sealed class LiveActivityPacer
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan ResumeTimeout = TimeSpan.FromMilliseconds(1500);
    public const double GateDbfs = -45.0;

    /// <summary>RMS が 0（無音）のときに据える dBFS。門（-45）より必ず小さい値。</summary>
    private const double SilentDbfs = -100.0;

    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly List<ReadOnlyMemory<byte>> _buffer = [];

    private bool _started;
    private bool _buffering;
    private DateTimeOffset _lastCheckAt;
    private DateTimeOffset _bufferingStartedAt;
    private bool _turnCompleteWhileBuffering;
    private long _sumSquares;
    private int _sampleCount;

    public LiveActivityPacer(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    /// <summary>溜めている（TurnComplete 待ち）かどうか。</summary>
    public bool IsBuffering
    {
        get
        {
            lock (_gate)
            {
                return _buffering;
            }
        }
    }

    /// <summary>音声チャンクを渡し、送信側が実行する手順を受け取る。</summary>
    public LivePacerAction OnAudio(ReadOnlyMemory<byte> pcm)
    {
        lock (_gate)
        {
            Accumulate(pcm);
            var now = _timeProvider.GetUtcNow();

            if (!_started)
            {
                _started = true;
                _lastCheckAt = now;
                return new LivePacerAction(
                    SendActivityStart: true,
                    AudioToSend: [pcm],
                    SendActivityEnd: false);
            }

            if (_buffering)
            {
                return ResumeOrQueue(pcm, now);
            }

            if (now - _lastCheckAt >= FlushInterval)
            {
                var dbfs = ConsumeDbfs();
                if (dbfs >= GateDbfs)
                {
                    // 中身のある区間だけを区切る。このチャンクは新しい区間の溜めに回す。
                    _buffering = true;
                    _bufferingStartedAt = now;
                    _turnCompleteWhileBuffering = false;
                    _buffer.Add(pcm);
                    return new LivePacerAction(
                        SendActivityStart: false,
                        AudioToSend: [],
                        SendActivityEnd: true);
                }

                // 中身の無い区間では区切らない。Gemini が幻覚の文字を返すため。
                // 次の判定はここから数え直す。
                _lastCheckAt = now;
            }

            return new LivePacerAction(
                SendActivityStart: false,
                AudioToSend: [pcm],
                SendActivityEnd: false);
        }
    }

    /// <summary>受信側が TurnComplete を受け取ったときに呼ぶ。</summary>
    public void OnTurnComplete()
    {
        lock (_gate)
        {
            _turnCompleteWhileBuffering = true;
        }
    }

    /// <summary>溜め中に再開条件（TurnComplete 受信 or ResumeTimeout 経過）を満たしたら手順を返す。まだなら null。</summary>
    public LivePacerAction? TryResume()
    {
        lock (_gate)
        {
            if (!_buffering)
            {
                return null;
            }

            var now = _timeProvider.GetUtcNow();
            return CanResume(now) ? Resume(now) : null;
        }
    }

    /// <summary>停止時に呼ぶ。溜めた分があれば吐き出し、最後に ActivityEnd を送る手順を返す。</summary>
    public LivePacerAction OnStop()
    {
        lock (_gate)
        {
            var hasBuffer = _buffer.Count > 0;
            var flushed = hasBuffer ? _buffer.ToArray() : [];
            _buffer.Clear();
            _buffering = false;

            // 停止時は残りを確実に届けるため、音量の門は適用しない。
            return new LivePacerAction(
                SendActivityStart: hasBuffer,
                AudioToSend: flushed,
                SendActivityEnd: true);
        }
    }

    /// <summary>溜め中の音声の再開を試み、再開できなければ今回のチャンクを溜める。</summary>
    private LivePacerAction ResumeOrQueue(ReadOnlyMemory<byte> pcm, DateTimeOffset now)
    {
        if (!CanResume(now))
        {
            _buffer.Add(pcm);
            return new LivePacerAction(SendActivityStart: false, AudioToSend: [], SendActivityEnd: false);
        }

        var action = Resume(now);
        return action with { AudioToSend = [.. action.AudioToSend, pcm] };
    }

    /// <summary>再開条件（TurnComplete 受信 or ResumeTimeout 経過）を満たしているか。</summary>
    private bool CanResume(DateTimeOffset now) =>
        _turnCompleteWhileBuffering || now - _bufferingStartedAt >= ResumeTimeout;

    /// <summary>溜めた分を送る手順を作り、溜めを解消する。区切りの間隔は再開した時点から数え直す。</summary>
    private LivePacerAction Resume(DateTimeOffset now)
    {
        _buffering = false;
        _lastCheckAt = now;
        var flushed = _buffer.ToArray();
        _buffer.Clear();

        return new LivePacerAction(
            SendActivityStart: true,
            AudioToSend: flushed,
            SendActivityEnd: false);
    }

    /// <summary>前回の判定から今回のチャンクまでの音声を二乗和に足し込む（16 bit LE モノラル）。</summary>
    private void Accumulate(ReadOnlyMemory<byte> pcm)
    {
        var samples = MemoryMarshal.Cast<byte, short>(pcm.Span);
        foreach (var sample in samples)
        {
            _sumSquares += (long)sample * sample;
        }

        _sampleCount += samples.Length;
    }

    /// <summary>溜めた二乗和から dBFS を算出し、二乗和とサンプル数を 0 に戻す。</summary>
    private double ConsumeDbfs()
    {
        if (_sampleCount == 0)
        {
            return SilentDbfs;
        }

        var rms = Math.Sqrt((double)_sumSquares / _sampleCount);
        _sumSquares = 0;
        _sampleCount = 0;

        return rms == 0 ? SilentDbfs : 20 * Math.Log10(rms / 32768);
    }
}
