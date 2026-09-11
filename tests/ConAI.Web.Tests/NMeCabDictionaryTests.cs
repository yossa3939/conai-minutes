using NMeCab.Specialized;
using Xunit.Abstractions;

namespace ConAI.Web.Tests;

/// <summary>
/// 辞書は ConAI.Web のパッケージであり、試験プロジェクトへ届く保証が無い。
/// 届かないことに Task 2 の実装で気付くと、原因が分かち書きの誤りに見えてしまう。
/// </summary>
public class NMeCabDictionaryTests(ITestOutputHelper output)
{
    [Fact]
    public void 辞書が試験の出力に届いて解析できる()
    {
        using var tagger = MeCabIpaDicTagger.Create();

        var nodes = tagger.Parse("予算会議");

        Assert.Contains(nodes, node => node.Surface == "予算");
        Assert.Contains(nodes, node => node.Surface == "会議");
    }

    [Fact]
    public void Tagger1個あたりの常駐メモリを測る()
    {
        var beforeWorkingSet = Environment.WorkingSet;
        var beforeManaged = GC.GetTotalMemory(forceFullCollection: true);

        var taggers = new List<MeCabIpaDicTagger>();

        for (var i = 0; i < 4; i++)
        {
            var tagger = MeCabIpaDicTagger.Create();

            // 生成だけでは遅延読み込みが走らない実装がありうる。1 度は解析させる。
            tagger.Parse("予算会議の話をした");
            taggers.Add(tagger);
        }

        var afterWorkingSet = Environment.WorkingSet;
        var afterManaged = GC.GetTotalMemory(forceFullCollection: true);

        output.WriteLine($"working set: {(afterWorkingSet - beforeWorkingSet) / 1024 / 1024} MB / 4 個");
        output.WriteLine($"managed: {(afterManaged - beforeManaged) / 1024 / 1024} MB / 4 個");

        foreach (var tagger in taggers)
        {
            tagger.Dispose();
        }

        Assert.Equal(4, taggers.Count);
    }
}
