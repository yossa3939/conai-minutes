using ConAI.Web.Search;

namespace ConAI.Web.Tests;

public class SearchExcerptTests
{
    [Fact]
    public void 抜粋がヒット位置の前後で切り出される()
    {
        var text = new string('あ', 100) + "予算" + new string('い', 100);

        var excerpt = SearchExcerpt.Around(text, ["予算"], radius: 10);

        Assert.Equal("…" + new string('あ', 10) + "予算" + new string('い', 8) + "…", excerpt);
    }

    [Fact]
    public void 前後が続かないときは記号が付かない()
    {
        Assert.Equal("予算の話", SearchExcerpt.Around("予算の話", ["予算"], radius: 10));
    }

    [Fact]
    public void 見つからなければ抜粋は空になる()
    {
        Assert.Equal(string.Empty, SearchExcerpt.Around("予算の話", ["昼食"], radius: 10));
    }

    [Fact]
    public void 抜粋の端でサロゲートペアが割れない()
    {
        // 「𠮷」のような漢字は UTF-16 で 2 つぶんの位置を占める。半径で切った端がその途中に落ちると、
        // 片割れだけが残って文字が壊れる。人名に現れるため、議事録では実際に起こりうる。
        var text = "𠮷𠮷予算𠮷𠮷";

        var excerpt = SearchExcerpt.Around(text, ["予算"], radius: 3);

        Assert.Equal("𠮷𠮷予算𠮷…", excerpt);
    }

    [Fact]
    public void 先に当たった表層トークンの位置で切り出す()
    {
        // 打った順に見る。利用者が最初に書いた語のほうが、探している内容に近い。
        Assert.Equal(2, SearchExcerpt.FirstIndexOf("あい予算うえ島田", ["予算", "島田"]));
    }

    [Fact]
    public void 窓が複数のときは記号でつながる()
    {
        var text = "予算" + new string('あ', 100) + "予算";

        var windows = SearchExcerpt.Windows(text, ["予算"], radius: 5, maxWindows: 5);

        // 前の窓は 0 文字目から半径 5 まで（前方に文字が無いので先頭の記号は付かない）、
        // 後ろの窓は末尾までなので後方の記号が付かない
        Assert.Equal("予算あああ…" + "……" + "…あああああ予算", windows);
    }

    [Fact]
    public void 窓は上限の数で止まる()
    {
        var text = string.Join(new string('あ', 50), Enumerable.Repeat("予算", 10));

        var windows = SearchExcerpt.Windows(text, ["予算"], radius: 5, maxWindows: 3);

        // 窓端の … と区切りの …… が連なって … が 4 個並び、Split が空文字列を 1 個挟む。
        // 空を除いて数えないと窓の個数が測れない。
        Assert.Equal(3, windows.Split("……", StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void 近すぎる位置は1つの窓にまとまる()
    {
        // 半径の中にある 2 つ目で切り直すと、ほぼ同じ文字列が 2 度並ぶ。
        var windows = SearchExcerpt.Windows("予算と予算の話", ["予算"], radius: 20, maxWindows: 5);

        Assert.DoesNotContain("……", windows);
    }

    [Fact]
    public void 議事録に当たれば議事録と判定される()
    {
        var (kind, excerpt) = SearchExcerpt.Describe("定例会", "予算の話をした", ["予算"]);

        Assert.Equal(SearchMatchKind.Minutes, kind);
        Assert.Equal("予算の話をした", excerpt);
    }

    [Fact]
    public void 会議名にだけ当たれば会議名と判定される()
    {
        var (kind, excerpt) = SearchExcerpt.Describe("予算検討会", "昼食の話をした", ["予算"]);

        Assert.Equal(SearchMatchKind.Title, kind);
        Assert.Equal("会議名に一致", excerpt);
    }

    [Fact]
    public void どちらにも無ければ文字起こしと判定される()
    {
        // FTS5 が該当を返したのに会議名にも議事録にも無いなら、当たったのは文字起こしだけである。
        var (kind, excerpt) = SearchExcerpt.Describe("定例会", "昼食の話をした", ["予算"]);

        Assert.Equal(SearchMatchKind.Transcript, kind);
        Assert.Equal("文字起こしに一致", excerpt);
    }
}
