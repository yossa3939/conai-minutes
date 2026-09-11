namespace ConAI.Web.Search;

/// <summary>
/// 索引を作るときと検索するときで同じ実装を通す。
/// ここが分かれると、入れた語と探す語の切れ目が食い違い、
/// 正しく索引されているのに引けないという壊れ方をする。
/// </summary>
public interface ISearchTokenizer
{
    /// <summary>索引と検索の条件式に使う原形トークン。</summary>
    IReadOnlyList<string> Tokenize(string text, CancellationToken cancellationToken = default);

    /// <summary>抜粋の位置探しに使う表層トークン。</summary>
    IReadOnlyList<string> Surfaces(string text, CancellationToken cancellationToken = default);

    /// <summary>抜粋を切り出す対象になる、正規化済みの文字列。</summary>
    string Normalize(string text);
}
