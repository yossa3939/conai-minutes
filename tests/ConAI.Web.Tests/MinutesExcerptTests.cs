using ConAI.Web.Notifications;

namespace ConAI.Web.Tests;

public class MinutesExcerptTests
{
    [Fact]
    public void 見出し行は抜粋から外す()
    {
        var minutes = "# 議事録\n\n## 決定事項\n次の版で出す。\n\n## 宿題\n見積もりを出す。";

        var excerpt = MinutesExcerpt.Build(minutes, 200);

        Assert.Equal("次の版で出す。\n見積もりを出す。", excerpt);
    }

    [Fact]
    public void 上限を超えると三点リーダで止める()
    {
        var excerpt = MinutesExcerpt.Build(new string('あ', 100), 10);

        Assert.Equal(10, excerpt.Length);
        Assert.EndsWith("…", excerpt);
    }

    [Fact]
    public void 上限ちょうどなら手を加えない()
    {
        Assert.Equal("あいうえお", MinutesExcerpt.Build("あいうえお", 5));
    }

    [Fact]
    public void 上限が0なら抜粋を作らない()
    {
        Assert.Equal(string.Empty, MinutesExcerpt.Build("決めたこと", 0));
    }

    [Fact]
    public void 議事録が空なら抜粋も空()
    {
        Assert.Equal(string.Empty, MinutesExcerpt.Build("   ", 200));
        Assert.Equal(string.Empty, MinutesExcerpt.Build(null, 200));
    }

    [Fact]
    public void 絵文字を途中で割らない()
    {
        // 🙂 は char 2 つ分。5 文字目で切ると壊れた文字が送られる
        var excerpt = MinutesExcerpt.Clamp("あいう🙂えお", 5);

        Assert.Equal("あいう…", excerpt);
    }

    [Fact]
    public void 太字は星1つの記法に直す()
    {
        Assert.Equal("*決定*：出す", MinutesExcerpt.ToSingleAsteriskBold("**決定**：出す"));
    }

    [Fact]
    public void 改行の種類をそろえる()
    {
        Assert.Equal("上\n下", MinutesExcerpt.Build("上\r\n下", 200));
    }
}
