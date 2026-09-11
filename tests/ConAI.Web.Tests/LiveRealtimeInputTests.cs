using ConAI.Web.Gemini;

namespace ConAI.Web.Tests;

/// <summary>
/// Live へ送る音声チャンクの組み立てを検証する。
/// Gemini Live API は realtime_input.media_chunks を受け付けなくなり（2026-08-25 の実機確認で
/// InvalidPayloadData として初回チャンク送信直後に切断された）、audio フィールドで送る必要がある。
/// Fake プロバイダでは Gemini に届く形を検査できないため、純粋関数の戻り値を直接検査する。
/// </summary>
public sealed class LiveRealtimeInputTests
{
    [Fact]
    public void 音声チャンクはaudioフィールドで送る()
    {
        var pcm = new byte[] { 1, 2, 3, 4 };

        var input = GoogleGeminiLiveSession.BuildRealtimeInput(pcm);

        Assert.NotNull(input.Audio);
        Assert.Equal(pcm, input.Audio.Data);
        Assert.Equal("audio/pcm;rate=16000", input.Audio.MimeType);
    }

    [Fact]
    public void 音声チャンクは非推奨のmedia_chunksを使わない()
    {
        var input = GoogleGeminiLiveSession.BuildRealtimeInput(new byte[] { 1, 2 });

        Assert.Null(input.Media);
    }
}
