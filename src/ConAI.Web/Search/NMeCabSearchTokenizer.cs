using System.Collections.Concurrent;
using System.Text;
using NMeCab.Specialized;

namespace ConAI.Web.Search;

/// <summary>
/// NMeCab（IPAdic）で分かち書きする。索引も検索もこの 1 つを通す。
/// </summary>
public sealed class NMeCabSearchTokenizer : ISearchTokenizer, IDisposable
{
    // 助詞と助動詞は語の切れ目にしか現れず、記号とフィラーは意味を持たない。
    // 索引に入れると、どの会議にも当たる語が混ざって AND が骨抜きになる。
    private static readonly HashSet<string> DroppedPartsOfSpeech =
        new(StringComparer.Ordinal) { "助詞", "助動詞", "記号", "フィラー", "その他" };

    private static readonly char[] SentenceSeparators = ['\n', '\r', '。', '！', '？', '.'];

    private readonly SemaphoreSlim _slots = new(SearchLimits.TaggerCount, SearchLimits.TaggerCount);
    private readonly ConcurrentBag<MeCabIpaDicTagger> _taggers = [];

    public NMeCabSearchTokenizer()
    {
        // 生成のたびに辞書を読むため都度生成はできず、内部に作業領域を持つためスレッド安全でもない。
        // 先に決まった数だけ作って貸し出す。ObjectPool は同時に存在する個数を抑えないので使わない。
        for (var i = 0; i < SearchLimits.TaggerCount; i++)
        {
            _taggers.Add(MeCabIpaDicTagger.Create());
        }
    }

    public IReadOnlyList<string> Tokenize(string text, CancellationToken cancellationToken = default) =>
        Analyze(text, original: true, cancellationToken);

    public IReadOnlyList<string> Surfaces(string text, CancellationToken cancellationToken = default) =>
        Analyze(text, original: false, cancellationToken);

    public string Normalize(string text) =>
        text.Normalize(NormalizationForm.FormKC).ToLowerInvariant();

    public void Dispose()
    {
        while (_taggers.TryTake(out var tagger))
        {
            tagger.Dispose();
        }

        _slots.Dispose();
    }

    private IReadOnlyList<string> Analyze(string text, bool original, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var tokens = new List<string>();

        // 空きを待つあいだに要求が切れたら、解析を始めずに抜ける。
        _slots.Wait(cancellationToken);

        MeCabIpaDicTagger? tagger = null;

        try
        {
            if (!_taggers.TryTake(out tagger))
            {
                throw new ObjectDisposedException(nameof(NMeCabSearchTokenizer));
            }

            foreach (var sentence in Split(Normalize(text)))
            {
                foreach (var node in tagger.Parse(sentence))
                {
                    var token = Select(node, original);

                    if (token.Length > 0)
                    {
                        tokens.Add(token);
                    }
                }
            }
        }
        finally
        {
            if (tagger is not null)
            {
                _taggers.Add(tagger);
            }

            _slots.Release();
        }

        return tokens;
    }

    private static string Select(MeCabIpaDicNode node, bool original)
    {
        if (DroppedPartsOfSpeech.Contains(node.PartsOfSpeech))
        {
            return string.Empty;
        }

        var surface = (node.Surface ?? string.Empty).Trim();

        // IPAdic は未知語を名詞として推定するため !!! のような文字を 1 つも含まない塊まで語として返る。
        // 語でないことに原形も表層も無いので、どちらを採るか決める前に落とす。
        if (!surface.Any(char.IsLetterOrDigit))
        {
            return string.Empty;
        }

        if (!original)
        {
            return surface;
        }

        var form = node.OriginalForm;

        if (string.IsNullOrEmpty(form) || form == "*")
        {
            // 未知語は原形を持たない。落とすと固有名詞がまったく引けなくなる。
            return surface;
        }

        return form.Trim();
    }

    /// <summary>文に割る。区切りが 1 つも来ないまま上限に達したら、そこで切る。</summary>
    private static IEnumerable<string> Split(string normalized)
    {
        var start = 0;

        for (var i = 0; i < normalized.Length; i++)
        {
            var isSeparator = Array.IndexOf(SentenceSeparators, normalized[i]) >= 0;
            var isForced = i - start + 1 >= SearchLimits.MaxSentenceChars;

            if (!isSeparator && !isForced)
            {
                continue;
            }

            var sentence = normalized[start..(i + 1)].Trim();

            if (sentence.Length > 0)
            {
                yield return sentence;
            }

            start = i + 1;
        }

        var tail = normalized[start..].Trim();

        if (tail.Length > 0)
        {
            yield return tail;
        }
    }
}
