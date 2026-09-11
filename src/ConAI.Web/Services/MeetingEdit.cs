namespace ConAI.Web.Services;

/// <summary>編集画面の「保存」1 回で書き換える会議の全項目（RV 3）。</summary>
/// <param name="MinutesTemplateId">
/// 議事録テンプレートの Id。null は「この呼び出しでは触らない」の意味で、今の選択をそのまま残す。
/// 画面のセレクトボックスには未選択の選択肢が無いため、null が来るのはこの項目を送らない呼び出しだけである。
/// </param>
public sealed record MeetingEdit(
    string Title,
    DateTime? HeldAt,
    bool LiveMode,
    bool TranslateMode,
    string TargetLanguage,
    string Transcription,
    string TranslatedTranscription,
    string Minutes,
    Guid? MinutesTemplateId = null);
