using ConAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Services;

public sealed class MeetingService : IMeetingService
{
    public const string InterruptedMessage = "サーバ再起動により中断されました。";

    private readonly ApplicationDbContext _db;
    private readonly IMinutesTemplateService _templates;

    public MeetingService(ApplicationDbContext db, IMinutesTemplateService templates)
    {
        _db = db;
        _templates = templates;
    }

    public async Task<IReadOnlyList<Meeting>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        await _db.Meetings
            .Where(m => m.OwnerId == ownerId)
            .OrderByDescending(m => m.UpdatedAt)
            .ToListAsync(cancellationToken);

    public Task<Meeting?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
        _db.Meetings
            .Include(m => m.Files)
            .FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);

    public Task<Meeting?> GetForGenerationAsync(Guid id, CancellationToken cancellationToken) =>
        _db.Meetings
            .Include(m => m.Files)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<Meeting> CreateAsync(string ownerId, Meeting meeting, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        meeting.OwnerId = ownerId;
        meeting.CreatedAt = now;
        meeting.UpdatedAt = now;
        if (!SupportedLanguages.IsSupported(meeting.TargetLanguage))
        {
            meeting.TargetLanguage = SupportedLanguages.Default;
        }

        _db.Meetings.Add(meeting);
        await _db.SaveChangesAsync(cancellationToken);
        return meeting;
    }

    public async Task<bool> UpdateAsync(Guid id, string ownerId, MeetingEdit edit, CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(edit.TargetLanguage))
        {
            return false;
        }

        // 画面の選択肢に無い Id を送られても保存しない
        if (edit.MinutesTemplateId is { } templateId
            && !await _templates.OwnsAsync(templateId, ownerId, cancellationToken))
        {
            return false;
        }

        var meeting = await FindOwnedAsync(id, ownerId, cancellationToken);
        if (meeting is null)
        {
            return false;
        }

        meeting.Title = edit.Title;
        meeting.HeldAt = edit.HeldAt;
        meeting.LiveMode = edit.LiveMode;
        meeting.TranslateMode = edit.TranslateMode;
        meeting.TargetLanguage = edit.TargetLanguage;
        meeting.Transcription = edit.Transcription;
        meeting.TranslatedTranscription = edit.TranslatedTranscription;
        meeting.Minutes = edit.Minutes;
        meeting.MinutesTemplateId = edit.MinutesTemplateId ?? meeting.MinutesTemplateId;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AppendTranscriptAsync(
        Guid id,
        string transcriptDelta,
        string translationDelta,
        CancellationToken cancellationToken)
    {
        if (transcriptDelta.Length == 0 && translationDelta.Length == 0)
        {
            return;
        }

        var meeting = await _db.Meetings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (meeting is null)
        {
            return;
        }

        meeting.Transcription += transcriptDelta;
        meeting.TranslatedTranscription += translationDelta;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<MeetingFile?> AddFileAsync(Guid meetingId, string ownerId, MeetingFile file, CancellationToken cancellationToken)
    {
        var meeting = await FindOwnedAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null)
        {
            return null;
        }

        file.MeetingId = meetingId;
        file.CreatedAt = DateTime.UtcNow;
        _db.MeetingFiles.Add(file);
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return file;
    }

    public async Task<MeetingFile?> GetFileAsync(Guid meetingId, string ownerId, Guid fileId, CancellationToken cancellationToken)
    {
        var meeting = await FindOwnedAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null)
        {
            return null;
        }

        return await _db.MeetingFiles
            .FirstOrDefaultAsync(f => f.Id == fileId && f.MeetingId == meetingId, cancellationToken);
    }

    public async Task<bool> RemoveFileAsync(Guid meetingId, string ownerId, Guid fileId, CancellationToken cancellationToken)
    {
        var file = await GetFileAsync(meetingId, ownerId, fileId, cancellationToken);
        if (file is null)
        {
            return false;
        }

        _db.MeetingFiles.Remove(file);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<DeleteResult> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        var meeting = await FindOwnedAsync(id, ownerId, cancellationToken);
        if (meeting is null)
        {
            return DeleteResult.NotFound;
        }

        if (meeting.GenerationStatus is GenerationStatus.Queued or GenerationStatus.Running)
        {
            return DeleteResult.JobInProgress;
        }

        _db.Meetings.Remove(meeting);
        await _db.SaveChangesAsync(cancellationToken);
        return DeleteResult.Deleted;
    }

    public async Task<SetTemplateResult> SetMinutesTemplateAsync(
        Guid id,
        string ownerId,
        Guid templateId,
        CancellationToken cancellationToken)
    {
        // 他人のテンプレートを指させない。UpdateAsync と同じ検査を、こちらにも置く。
        if (!await _templates.OwnsAsync(templateId, ownerId, cancellationToken))
        {
            return SetTemplateResult.TemplateNotAllowed;
        }

        // 生成開始の途中で呼ばれる。この 1 列だけを更新し、UpdatedAt は触らない
        // （テンプレートを選び直しただけで会議一覧の並びが動かないようにする）。
        var updated = await _db.Meetings
            .Where(m => m.Id == id && m.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.MinutesTemplateId, templateId),
                cancellationToken);

        // ExecuteUpdateAsync は ChangeTracker を経由しないので、追跡済みの会議が古いまま残る。
        if (updated != 1)
        {
            return SetTemplateResult.MeetingNotFound;
        }

        _db.ChangeTracker.Clear();
        return SetTemplateResult.Updated;
    }

    public async Task<bool> TryMarkQueuedAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        // 読んでから書くと同時要求で二重投入されうるので、状態の条件ごと 1 文で更新して、更新できた行数で判定する。
        var updated = await _db.Meetings
            .Where(m => m.Id == id
                && m.OwnerId == ownerId
                && m.GenerationStatus != GenerationStatus.Queued
                && m.GenerationStatus != GenerationStatus.Running)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(m => m.GenerationStatus, GenerationStatus.Queued)
                    .SetProperty(m => m.GenerationError, (string?)null)
                    .SetProperty(m => m.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

        // ExecuteUpdateAsync は ChangeTracker を経由しないので、追跡済みのエンティティが古いまま残る。読み直させる。
        if (updated == 1)
        {
            _db.ChangeTracker.Clear();
        }

        return updated == 1;
    }

    public async Task MarkRunningAsync(Guid id, CancellationToken cancellationToken) =>
        await SetStatusAsync(id, GenerationStatus.Running, null, cancellationToken);

    /// <summary>生成結果を保存する。<paramref name="transcription"/> / <paramref name="translatedTranscription"/> が
    /// null のときはその項目を書き換えない（Live や手編集の内容を壊さないため）。</summary>
    public async Task MarkSucceededAsync(
        Guid id,
        string? transcription,
        string? translatedTranscription,
        string minutes,
        CancellationToken cancellationToken)
    {
        var meeting = await _db.Meetings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (meeting is null)
        {
            return;
        }

        if (transcription is not null)
        {
            meeting.Transcription = transcription;
        }

        if (translatedTranscription is not null)
        {
            meeting.TranslatedTranscription = translatedTranscription;
        }

        meeting.Minutes = minutes;              // Minutes は常に上書きする
        meeting.GenerationStatus = GenerationStatus.Succeeded;
        meeting.GenerationError = null;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid id, string userMessage, CancellationToken cancellationToken) =>
        await SetStatusAsync(id, GenerationStatus.Failed, userMessage, cancellationToken);

    public async Task<int> ResetInterruptedJobsAsync(CancellationToken cancellationToken)
    {
        var stuck = await _db.Meetings
            .Where(m => m.GenerationStatus == GenerationStatus.Queued || m.GenerationStatus == GenerationStatus.Running)
            .ToListAsync(cancellationToken);

        foreach (var meeting in stuck)
        {
            meeting.GenerationStatus = GenerationStatus.Failed;
            meeting.GenerationError = InterruptedMessage;
            meeting.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return stuck.Count;
    }

    private async Task SetStatusAsync(Guid id, GenerationStatus status, string? error, CancellationToken cancellationToken)
    {
        var meeting = await _db.Meetings.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (meeting is null)
        {
            return;
        }

        meeting.GenerationStatus = status;
        meeting.GenerationError = error;
        meeting.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private Task<Meeting?> FindOwnedAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
        _db.Meetings.FirstOrDefaultAsync(m => m.Id == id && m.OwnerId == ownerId, cancellationToken);
}
