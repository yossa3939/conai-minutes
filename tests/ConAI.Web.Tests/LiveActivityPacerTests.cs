using ConAI.Web.Gemini;
using Microsoft.Extensions.Time.Testing;

namespace ConAI.Web.Tests;

/// <summary>
/// 手動の発話区切り判定（LiveActivityPacer）の振る舞いを検証する。
/// Gemini Live の入力文字起こしはサーバ側 VAD の発話区切りでしか返らず、PC 音声には切れ目が
/// 立たないため、自動検出を切って約 2 秒ごとに手動で区切る。判定は FakeTimeProvider で時刻を
/// 制御し、ネットワーク I/O を持たない純粋な判定器の入出力を直接検査する。
/// </summary>
public sealed class LiveActivityPacerTests
{
    /// <summary>全標本が指定した振幅の 16bit LE モノラル PCM（100 ミリ秒分）。</summary>
    private static byte[] TonePcm(short amplitude)
    {
        var pcm = new byte[3200];
        for (var sample = 0; sample < 1600; sample++)
        {
            pcm[sample * 2] = (byte)(amplitude & 0xFF);
            pcm[sample * 2 + 1] = (byte)((amplitude >> 8) & 0xFF);
        }

        return pcm;
    }

    private static byte[] SilentPcm() => new byte[3200];

    private static byte[][] ToArrays(IReadOnlyList<ReadOnlyMemory<byte>> chunks) =>
        chunks.Select(chunk => chunk.ToArray()).ToArray();

    [Fact]
    public void 最初の音声にはActivityStartが付く()
    {
        var pacer = new LiveActivityPacer(new FakeTimeProvider());
        var chunk = TonePcm(1000);

        var action = pacer.OnAudio(chunk);

        Assert.True(action.SendActivityStart);
        Assert.False(action.SendActivityEnd);
        Assert.Equal([chunk], ToArrays(action.AudioToSend));
    }

    [Fact]
    public void 区切りの時刻より前は素通しで送る()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        pacer.OnAudio(TonePcm(1000));

        timeProvider.Advance(LiveActivityPacer.FlushInterval - TimeSpan.FromMilliseconds(1));
        var chunk = TonePcm(1001);
        var action = pacer.OnAudio(chunk);

        Assert.False(action.SendActivityStart);
        Assert.False(action.SendActivityEnd);
        Assert.Equal([chunk], ToArrays(action.AudioToSend));
        Assert.False(pacer.IsBuffering);
    }

    [Fact]
    public void 区切りの時刻でActivityEndを返し以後の音声を溜める()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        pacer.OnAudio(TonePcm(1000));

        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        var action = pacer.OnAudio(TonePcm(1001));

        Assert.False(action.SendActivityStart);
        Assert.True(action.SendActivityEnd);
        Assert.Empty(action.AudioToSend);
        Assert.True(pacer.IsBuffering);

        var queued = pacer.OnAudio(TonePcm(1002));
        Assert.False(queued.SendActivityStart);
        Assert.False(queued.SendActivityEnd);
        Assert.Empty(queued.AudioToSend);
        Assert.True(pacer.IsBuffering);
    }

    [Fact]
    public void TurnCompleteを受けると溜めた分を順番を保って返す()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        var first = TonePcm(1000);
        var second = TonePcm(1001);
        var third = TonePcm(1002);
        var fourth = TonePcm(1003);

        pacer.OnAudio(first);
        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        pacer.OnAudio(second);

        var queued = pacer.OnAudio(third);
        Assert.True(pacer.IsBuffering);
        Assert.Empty(queued.AudioToSend);

        pacer.OnTurnComplete();
        var resumed = pacer.OnAudio(fourth);

        Assert.True(resumed.SendActivityStart);
        Assert.False(resumed.SendActivityEnd);
        Assert.Equal([second, third, fourth], ToArrays(resumed.AudioToSend));
        Assert.False(pacer.IsBuffering);
    }

    [Fact]
    public void TurnCompleteが来なくてもResumeTimeoutの経過で再開する()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        var first = TonePcm(1000);
        var second = TonePcm(1001);
        var third = TonePcm(1002);

        pacer.OnAudio(first);
        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        pacer.OnAudio(second);

        timeProvider.Advance(LiveActivityPacer.ResumeTimeout);
        var resumed = pacer.OnAudio(third);

        Assert.True(resumed.SendActivityStart);
        Assert.False(resumed.SendActivityEnd);
        Assert.Equal([second, third], ToArrays(resumed.AudioToSend));
        Assert.False(pacer.IsBuffering);
    }

    [Fact]
    public void TryResumeは再開条件を満たすまではnullを返す()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        var second = TonePcm(1001);

        pacer.OnAudio(TonePcm(1000));
        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        pacer.OnAudio(second);

        Assert.Null(pacer.TryResume());

        timeProvider.Advance(LiveActivityPacer.ResumeTimeout);
        var resumed = pacer.TryResume();

        Assert.NotNull(resumed);
        Assert.True(resumed!.SendActivityStart);
        Assert.False(resumed.SendActivityEnd);
        Assert.Equal([second], ToArrays(resumed.AudioToSend));
        Assert.False(pacer.IsBuffering);
    }

    [Fact]
    public void 門より小さい音では区切らない()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        pacer.OnAudio(SilentPcm());

        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        var chunk = SilentPcm();
        var action = pacer.OnAudio(chunk);

        Assert.False(action.SendActivityEnd);
        Assert.Equal([chunk], ToArrays(action.AudioToSend));
        Assert.False(pacer.IsBuffering);
    }

    [Fact]
    public void 門以上の音では区切る()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        pacer.OnAudio(TonePcm(30000));

        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        var action = pacer.OnAudio(TonePcm(30000));

        Assert.True(action.SendActivityEnd);
        Assert.True(pacer.IsBuffering);
    }

    [Fact]
    public void 停止時は溜めた分を吐き出してからActivityEndを返す()
    {
        var timeProvider = new FakeTimeProvider();
        var pacer = new LiveActivityPacer(timeProvider);
        var second = TonePcm(1001);

        pacer.OnAudio(TonePcm(1000));
        timeProvider.Advance(LiveActivityPacer.FlushInterval);
        pacer.OnAudio(second);

        var stop = pacer.OnStop();

        Assert.True(stop.SendActivityStart);
        Assert.Equal([second], ToArrays(stop.AudioToSend));
        Assert.True(stop.SendActivityEnd);
    }

    [Fact]
    public void 溜めが無い状態での停止はActivityStartを付けずにActivityEndだけ返す()
    {
        var pacer = new LiveActivityPacer(new FakeTimeProvider());
        pacer.OnAudio(TonePcm(1000));

        var stop = pacer.OnStop();

        Assert.False(stop.SendActivityStart);
        Assert.Empty(stop.AudioToSend);
        Assert.True(stop.SendActivityEnd);
    }
}
