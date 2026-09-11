using System.Text.RegularExpressions;
using ConAI.Web.Services;

namespace ConAI.Web.Tests;

public class MarkdownRendererTests
{
    private readonly MarkdownRenderer _renderer = new();

    [Fact]
    public void 見出しと表を描画する()
    {
        var html = _renderer.ToHtml("# 議事録\n\n| A | B |\n|---|---|\n| 1 | 2 |");

        Assert.Contains("<h1", html);
        Assert.Contains("<table", html);
    }

    [Fact]
    public void 生のHTMLは描画せずエスケープする()
    {
        var html = _renderer.ToHtml("<script>alert(1)</script>\n\n本文");

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void 汎用属性は描画されない()
    {
        var html = _renderer.ToHtml("# 見出し {#evil onclick=\"alert(1)\"}");

        // {...} は汎用属性として解釈されず、エスケープ済みの本文テキストとして出る。
        // 検証の対象は「属性として注入されないこと」であり、文字列そのものの不在ではない。
        Assert.DoesNotContain("<h1 onclick", html);
    }

    [Fact]
    public void 危険なスキームのリンクと画像は要素を出さずラベルだけ残す()
    {
        var html = _renderer.ToHtml(
            "[押す](javascript:alert(1)) と [開く](data:text/html,本文) と ![絵](data:image/svg+xml,xx)");

        Assert.DoesNotContain("javascript:", html);
        Assert.DoesNotContain("data:", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("<img", html);
        // リンクを外しても、ラベルと代替文字列は表示から落とさない。
        Assert.Contains("押す", html);
        Assert.Contains("開く", html);
        Assert.Contains("絵", html);
    }

    [Fact]
    public void 危険なスキームの山括弧オートリンクも要素を出さず文字だけ残す()
    {
        var html = _renderer.ToHtml(
            "<javascript:alert(1)> と <vbscript:msgbox(1)> と <data:text/html,本文>"
            + " と <https://example.com> と <user@example.com>");

        // 弾いた URL は href に出ない。リンクを外しても、URL の文字は表示から落とさない。
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.DoesNotContain("href=\"vbscript:", html);
        Assert.DoesNotContain("href=\"data:", html);
        Assert.Contains("javascript:alert(1)", html);
        // 残る要素は、通してよいスキームのオートリンク 2 つだけ。
        Assert.Equal(2, Regex.Count(html, "<a "));
        Assert.Contains("href=\"https://example.com\"", html);
        Assert.Contains("href=\"mailto:user@example.com\"", html);
    }

    [Fact]
    public void http_https_mailto_と相対URLのリンクはそのまま残る()
    {
        var html = _renderer.ToHtml(
            "[資料](https://example.com/a) と [内部](/meetings/1) と [連絡](mailto:a@example.com) と ![図](https://example.com/a.png)");

        Assert.Contains("href=\"https://example.com/a\"", html);
        Assert.Contains("href=\"/meetings/1\"", html);
        Assert.Contains("href=\"mailto:a@example.com\"", html);
        Assert.Contains("src=\"https://example.com/a.png\"", html);
    }

    [Fact]
    public void スキームに制御文字を挟んだリンクも無害化する()
    {
        // 実体参照でスキーム名にタブや改行を挟むと絶対 URI として読めず、
        // 相対 URL と誤判定して href に出てしまう。判定前にこれらの文字を取り除くことで防ぐ。
        var html = _renderer.ToHtml(
            "[押す](java&#9;script:alert&#40;1&#41;) と [開く](java&#10;script:alert&#40;1&#41;) と "
            + "[見る](java&#13;script:alert&#40;1&#41;) と [辿る](<java&#9;script:alert&#40;1&#41;>)");

        Assert.DoesNotContain("href", html);
        Assert.DoesNotContain("<a ", html);
        // リンクを外しても、ラベルは表示から落とさない。
        Assert.Contains("押す", html);
        Assert.Contains("開く", html);
        Assert.Contains("見る", html);
        Assert.Contains("辿る", html);
    }

    [Fact]
    public void 空文字は空文字になる()
    {
        Assert.Equal(string.Empty, _renderer.ToHtml(string.Empty));
        Assert.Equal(string.Empty, _renderer.ToPlainText(string.Empty));
    }

    [Fact]
    public void 平文は見出しと強調の記号を落とし本文だけ残す()
    {
        var text = _renderer.ToPlainText("# 議事録\n\n## 決定事項\n\n- 予算を**承認**\n- [資料](https://example.com)を共有\n\n本文です。");

        Assert.Equal("議事録\n決定事項\n予算を承認\n資料を共有\n本文です。\n", text);
    }

    [Fact]
    public void 平文の表は1行1レコードでセルをタブ区切りにする()
    {
        // Markdig 標準の ToPlainText は表のセルと後続の段落を 1 行に連結してしまうため、表だけ独自に描画する。
        var text = _renderer.ToPlainText("| 項目 | 担当 |\n|---|---|\n| 報告書 | 田中 |\n| 会場 | 佐藤 |\n\n以上。");

        Assert.Equal("項目\t担当\n報告書\t田中\n会場\t佐藤\n以上。\n", text);
    }

    [Fact]
    public void 平文でも生のHTMLは本文として残りタグにならない()
    {
        var text = _renderer.ToPlainText("<b>太字</b>\n\n本文");

        Assert.Contains("<b>太字</b>", text);
        Assert.Contains("本文", text);
    }
}
