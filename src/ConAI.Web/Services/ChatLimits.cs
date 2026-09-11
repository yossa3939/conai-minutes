namespace ConAI.Web.Services;

/// <summary>議事録への横断質問にかける上限。</summary>
public static class ChatLimits
{
    /// <summary>1 回の質問の長さ。</summary>
    public const int MaxQuestionChars = 2_000;

    /// <summary>1 利用者あたりの往復の上限。超えたら「会話を消す」で全件消してもらう。</summary>
    public const int MaxTurns = 200;

    /// <summary>1 段目の候補にする会議の件数。多くの場合は文字数の上限が先に効く、件数側の保険である。</summary>
    public const int MaxCandidateMeetings = 500;

    /// <summary>1 会議ぶんのダイジェストの上限。会議名と開催日時を含めて数える。</summary>
    public const int MaxDigestChars = 600;

    /// <summary>ダイジェストに拾う見出し行の数。</summary>
    public const int MaxDigestHeadingLines = 15;

    /// <summary>ダイジェストのうち本文冒頭に充てる長さ。</summary>
    public const int DigestExcerptChars = 300;

    /// <summary>1 段目に送るダイジェストの合計。</summary>
    public const int MaxSelectionContextChars = 200_000;

    /// <summary>1 段目が選べる会議の件数。</summary>
    public const int MaxSelectedMeetings = 5;

    /// <summary>2 段目に積む議事録の合計。1 件目だけは単体で超えても含める。</summary>
    public const int MaxAnswerContextChars = 200_000;

    /// <summary>2 段目に送る会話履歴の往復数。</summary>
    public const int HistoryTurnsForAnswer = 10;

    /// <summary>2 段目に送る会話履歴の文字数。議事録の枠とは別に数える。</summary>
    public const int MaxHistoryChars = 20_000;

    /// <summary>1 段目に添える直近の質問文の件数。</summary>
    public const int RecentQuestionsForSelection = 3;

    /// <summary>根拠に写す会議名の最大長。Meeting.Title の StringLength(200) に揃える。</summary>
    public const int MaxSourceTitleChars = 200;

    /// <summary>1 段目の待ち時間。</summary>
    public static readonly TimeSpan SelectTimeout = TimeSpan.FromSeconds(60);

    /// <summary>2 段目の待ち時間。1 段目とは別に数え、合計の上限は設けない。</summary>
    public static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(120);
}
