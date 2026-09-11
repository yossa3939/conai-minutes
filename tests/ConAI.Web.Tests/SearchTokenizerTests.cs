using ConAI.Web.Search;

namespace ConAI.Web.Tests;

public class SearchTokenizerTests
{
    // 辞書の読み込みが 1 個あたり 13 MB あるため、テストごとに作らず 1 つを使い回す。
    private static readonly NMeCabSearchTokenizer Tokenizer = new();

    [Fact]
    public void 複合語が語に割れる()
    {
        Assert.Equal(["予算", "会議"], Tokenizer.Tokenize("予算会議"));
    }

    [Fact]
    public void 活用した語が原形に寄る()
    {
        Assert.Contains("話す", Tokenizer.Tokenize("話した"));
    }

    [Fact]
    public void 表層は打った形のまま返る()
    {
        Assert.Contains("話し", Tokenizer.Surfaces("話した"));
    }

    [Fact]
    public void 助詞と記号とフィラーが落ちる()
    {
        var tokens = Tokenizer.Tokenize("えーと、予算の話を。");

        Assert.Contains("予算", tokens);
        Assert.Contains("話", tokens);
        Assert.DoesNotContain("の", tokens);
        Assert.DoesNotContain("を", tokens);
        Assert.DoesNotContain("、", tokens);
        Assert.DoesNotContain("。", tokens);
    }

    [Fact]
    public void 辞書に無い語は表層のまま採られる()
    {
        // 造語は OriginalForm が * になる。落とすと固有名詞がまったく引けなくなる。
        Assert.Contains("ぴよぽよ", Tokenizer.Tokenize("ぴよぽよ社と契約した"));
    }

    [Fact]
    public void 全角英数と半角カナが正規化で寄る()
    {
        Assert.Equal("abc123 テスト", Tokenizer.Normalize("ＡＢＣ１２３ ﾃｽﾄ"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ??? ***")]
    public void 語を含まない入力ではトークンが0個になる(string text)
    {
        Assert.Empty(Tokenizer.Tokenize(text));
    }

    [Theory]
    [InlineData("!!! ??? ***")]
    [InlineData("。、！？")]
    public void 語を含まない入力では表層も0個になる(string text)
    {
        Assert.Empty(Tokenizer.Surfaces(text));
    }

    [Fact]
    public void 区切りの無い長文でも解析できる()
    {
        // 1,000 文字で強制的に切る。切らないと MeCab のラティスが膨らみ続ける。
        var text = string.Concat(Enumerable.Repeat("予算会議", 800));

        var tokens = Tokenizer.Tokenize(text);

        Assert.Equal(1_600, tokens.Count);
    }

    [Fact]
    public void MATCH式がANDで結ばれる()
    {
        var (expression, truncated) = SearchQuery.BuildMatch(["予算", "話", "島田"]);

        Assert.Equal("\"予算\" AND \"話\" AND \"島田\"", expression);
        Assert.False(truncated);
    }

    [Fact]
    public void MATCH式のダブルクォートが二重になる()
    {
        var (expression, _) = SearchQuery.BuildMatch(["a\"b"]);

        Assert.Equal("\"a\"\"b\"", expression);
    }

    [Fact]
    public void MATCH式が32語で切られる()
    {
        var tokens = Enumerable.Range(0, 40).Select(i => $"語{i}").ToArray();

        var (expression, truncated) = SearchQuery.BuildMatch(tokens);

        Assert.True(truncated);
        Assert.Equal(SearchLimits.MaxQueryTokens, expression.Split(" AND ").Length);
        Assert.DoesNotContain("語32", expression);
    }

    [Fact]
    public void LIKEのパターンが記号をエスケープする()
    {
        Assert.Equal("%100\\%の\\_と\\\\%", SearchQuery.BuildLikePattern("100%の_と\\"));
    }
}
