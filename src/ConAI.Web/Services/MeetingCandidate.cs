namespace ConAI.Web.Services;

/// <summary>
/// 選抜が読む会議。ダイジェストと 2 段目の根拠に要る列だけを持つ。
/// 文字起こしは 1 会議 400,000 文字まで入りうるが選抜も回答も全文は読まないため、SQL の段階で外す。
/// 検索から選ばれた会議だけは、質問に当たる箇所を切り出した
/// <paramref name="TranscriptExcerpt"/> を運ぶ。
/// 既定値を持たせるのは、選抜の経路にある既存の 4 引数の呼び出しをそのまま通すためである。
/// </summary>
public sealed record MeetingCandidate(
    Guid Id, string Title, DateTime? HeldAt, string Minutes, string TranscriptExcerpt = "");
