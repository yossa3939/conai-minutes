using ConAI.Web.Services;

namespace ConAI.Web.Search;

/// <summary>
/// 検索の上限。Gemini の枠を扱う <see cref="ChatLimits"/> とは別にする。
/// </summary>
public static class SearchLimits
{
    /// <summary>入力欄 1 回分の長さ。入力欄が 1 つしかないので、質問の上限と同じ値を指す。
    /// 押したボタンによって同じ文字列の可否が変わると、利用者は理由を理解できない。</summary>
    public const int MaxQueryChars = ChatLimits.MaxQuestionChars;

    /// <summary>MATCH 式に入れるトークンの数。黙って切ると、AND の項が減って広がった結果を
    /// 絞り込んだ結果だと思って読むことになるため、切ったときは注記を添える。</summary>
    public const int MaxQueryTokens = 32;

    /// <summary>LIKE で引き直すかどうかの上限。2,000 文字の質問文をそのまま照合すると、
    /// 全走査の費用を払ったうえで必ず 0 件になる。</summary>
    public const int MaxFallbackQueryChars = 200;

    /// <summary>同時に貸し出す Tagger の数。1 つを索引ワーカーが長く握るため、
    /// 検索の側にもう 1 つ残す。Task 1 の実測では 4 個で 3 MB しか増えず、常駐量は制約にならない。</summary>
    public const int TaggerCount = 2;

    public const int PageSize = 20;

    /// <summary>取得できる最後のページ。深い OFFSET ほど FTS5 が読み捨てる行が増える。</summary>
    public const int MaxPage = 50;

    /// <summary>一覧の抜粋の半径。</summary>
    public const int ExcerptRadius = 60;

    /// <summary>回答に積む窓の半径。</summary>
    public const int TranscriptWindowRadius = 500;

    /// <summary>1 会議あたりの窓の数。</summary>
    public const int MaxTranscriptWindows = 5;

    /// <summary>回答の根拠にする会議の数。上限は読む前に掛ける。</summary>
    public const int MaxAnswerMeetings = 10;

    /// <summary>1 巡で索引する件数。</summary>
    public const int IndexBatchSize = 20;

    /// <summary>区切りが 1 つも現れないまま切る長さ。句読点の無い文字起こしがある。</summary>
    public const int MaxSentenceChars = 1_000;

    /// <summary>分かち書きの版番号。辞書や規則を変えたら上げる。
    /// 上げれば全会議が未索引になり、ワーカーが順に作り直す。</summary>
    public const int TokenizerVersion = 1;

    /// <summary>最終更新からこれだけ経つまで索引しない。
    /// Live 中は 2 秒ごとに文字起こしが伸びるので、伸び終わってから 1 回だけ索引する。</summary>
    public static readonly TimeSpan IndexQuietPeriod = TimeSpan.FromSeconds(10);

    /// <summary>ワーカーの巡回間隔。</summary>
    public static readonly TimeSpan IndexPollInterval = TimeSpan.FromSeconds(10);
}
