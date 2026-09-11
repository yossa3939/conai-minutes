using ConAI.Web.Data;

namespace ConAI.Web.Services;

public enum DeleteResult
{
    Deleted,
    NotFound,
    JobInProgress
}

public enum SetTemplateResult
{
    Updated,
    MeetingNotFound,
    TemplateNotAllowed
}

public interface IMeetingService
{
    Task<IReadOnlyList<Meeting>> ListAsync(string ownerId, CancellationToken cancellationToken);

    Task<Meeting?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>生成処理用の取得。所有者検査は登録時に済んでいるため id だけで取る。</summary>
    Task<Meeting?> GetForGenerationAsync(Guid id, CancellationToken cancellationToken);

    Task<Meeting> CreateAsync(string ownerId, Meeting meeting, CancellationToken cancellationToken);

    /// <summary>編集画面の「保存」1 回ぶんの更新。言語が対応外、または自分の会議でなければ false を返す。</summary>
    Task<bool> UpdateAsync(Guid id, string ownerId, MeetingEdit edit, CancellationToken cancellationToken);

    /// <summary>Live 中の追記。所有者チェックは WebSocket 接続時に済んでいるため id だけで足りる。</summary>
    Task AppendTranscriptAsync(Guid id, string transcriptDelta, string translationDelta, CancellationToken cancellationToken);

    Task<MeetingFile?> AddFileAsync(Guid meetingId, string ownerId, MeetingFile file, CancellationToken cancellationToken);

    Task<MeetingFile?> GetFileAsync(Guid meetingId, string ownerId, Guid fileId, CancellationToken cancellationToken);

    Task<bool> RemoveFileAsync(Guid meetingId, string ownerId, Guid fileId, CancellationToken cancellationToken);

    Task<DeleteResult> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    /// <summary>生成の直前に、押した時点で選ばれていたテンプレートを会議へ保存する。
    /// 断る理由を分けて返す。呼び出し元は会議が消えた場合とテンプレートが選べない場合で応答を変える。</summary>
    Task<SetTemplateResult> SetMinutesTemplateAsync(Guid id, string ownerId, Guid templateId, CancellationToken cancellationToken);

    Task<bool> TryMarkQueuedAsync(Guid id, string ownerId, CancellationToken cancellationToken);

    Task MarkRunningAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>生成結果を保存する。<paramref name="transcription"/> / <paramref name="translatedTranscription"/> が
    /// null のときはその欄を書き換えない（Live や手編集の内容を壊さないため）。</summary>
    Task MarkSucceededAsync(
        Guid id,
        string? transcription,
        string? translatedTranscription,
        string minutes,
        CancellationToken cancellationToken);

    Task MarkFailedAsync(Guid id, string userMessage, CancellationToken cancellationToken);

    Task<int> ResetInterruptedJobsAsync(CancellationToken cancellationToken);
}
