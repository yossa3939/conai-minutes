using ConAI.Web.Gemini;
using Google.GenAI.Types;

namespace ConAI.Web.Tests;

/// <summary>thinking level の文字列は、未指定（空）と MINIMAL / LOW / MEDIUM / HIGH の 4 値だけを受け付ける。</summary>
public class ThinkingLevelsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("　")]
    public void 未指定ならBuildはnullを返し未指定扱いになる(string? value)
    {
        Assert.Null(ThinkingLevels.Build(value));
    }

    [Theory]
    [InlineData("HIGH")]
    [InlineData("high")]
    [InlineData(" High ")]
    public void 大文字小文字と前後の空白は正規化して詰める(string value)
    {
        var config = ThinkingLevels.Build(value);

        Assert.Equal("HIGH", config!.ThinkingLevel!.Value.Value);
    }

    [Theory]
    [InlineData("MINIMAL", "MINIMAL")]
    [InlineData("LOW", "LOW")]
    [InlineData("MEDIUM", "MEDIUM")]
    [InlineData("HIGH", "HIGH")]
    public void 許可値はその綴りでThinkingLevelに詰める(string value, string expected)
    {
        var config = ThinkingLevels.Build(value);

        Assert.Equal(expected, config!.ThinkingLevel!.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("MINIMAL")]
    [InlineData("LOW")]
    [InlineData("MEDIUM")]
    [InlineData("HIGH")]
    [InlineData("minimal")]
    [InlineData("Low")]
    public void IsAllowedは未指定と4値で真を返す(string? value)
    {
        Assert.True(ThinkingLevels.IsAllowed(value));
    }

    [Theory]
    [InlineData("HIGHEST")]
    [InlineData("none")]
    [InlineData("THINKING_LEVEL_UNSPECIFIED")]
    [InlineData("L OW")]
    public void IsAllowedは許可値以外で偽を返す(string value)
    {
        Assert.False(ThinkingLevels.IsAllowed(value));
    }

    [Theory]
    [InlineData("HIGHEST")]
    [InlineData("L OW")]
    public void Buildは許可値以外でArgumentExceptionを投げる(string value)
    {
        Assert.Throws<ArgumentException>(() => ThinkingLevels.Build(value));
    }
}
