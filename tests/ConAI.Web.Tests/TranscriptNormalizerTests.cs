using ConAI.Web.Live;

namespace ConAI.Web.Tests;

public class TranscriptNormalizerTests
{
    [Theory]
    [InlineData("本日 の 会議 を 始め ます 。", "本日の会議を始めます。")]
    [InlineData("ABC 社 の 売上 は 10 億 円", "ABC社の売上は10億円")]
    [InlineData("東京 Tokyo 駅", "東京Tokyo駅")]
    [InlineData("　本日 は　晴れ ", "本日は晴れ")]
    [InlineData("１２ 時 に ｱｲｳ", "１２時にｱｲｳ")]
    [InlineData("hello   world", "hello world")]
    [InlineData("테스트 입니다", "테스트 입니다")]
    [InlineData("テスト文字起こし1。", "テスト文字起こし1。")]
    public void CJK文字に隣接する空白だけを除く(string input, string expected)
    {
        Assert.Equal(expected, TranscriptNormalizer.Normalize(input));
    }

    [Fact]
    public void 空文字はそのまま返す()
    {
        Assert.Equal(string.Empty, TranscriptNormalizer.Normalize(string.Empty));
    }
}
