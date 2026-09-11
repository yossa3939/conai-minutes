using ConAI.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ConAI.Web.Services;

public sealed class MinutesTemplateService : IMinutesTemplateService
{
    private const string CopySuffix = "（コピー）";

    private readonly ApplicationDbContext _db;
    private readonly IMinutesTemplateSeeds _seeds;
    private readonly ILogger<MinutesTemplateService> _logger;

    /// <summary>この要求の中で配布を確かめ終えた利用者。入口のメソッドが毎回呼ぶので、2 回目以降の問い合わせを省く。</summary>
    private string? _seededOwnerId;

    public MinutesTemplateService(
        ApplicationDbContext db,
        IMinutesTemplateSeeds seeds,
        ILogger<MinutesTemplateService> logger)
    {
        _db = db;
        _seeds = seeds;
        _logger = logger;
    }

    public async Task EnsureSeededAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (_seededOwnerId == ownerId)
        {
            return;
        }

        if (await _db.MinutesTemplates.AnyAsync(t => t.OwnerId == ownerId, cancellationToken))
        {
            _seededOwnerId = ownerId;
            return;
        }

        var now = DateTime.UtcNow;
        _db.MinutesTemplates.AddRange(
            new MinutesTemplate
            {
                OwnerId = ownerId,
                Name = _seeds.StandardName,
                Body = _seeds.StandardBody,
                IsDefault = true,
                CreatedAt = now,
                UpdatedAt = now
            },
            new MinutesTemplate
            {
                OwnerId = ownerId,
                Name = _seeds.ConciseName,
                Body = _seeds.ConciseBody,
                IsDefault = false,
                CreatedAt = now,
                UpdatedAt = now
            });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            // 同じ利用者の初回要求が並ぶと、既定の一意索引が 2 回目を弾く。
            // 先に入ったほうを使えばよいので、入れ損ねた行を捨てて読み直させる。
            _db.ChangeTracker.Clear();

            // 競合なら先に入った 2 件がもう読める。読めないなら別の失敗なので、握りつぶさず投げ直す。
            if (!await _db.MinutesTemplates.AnyAsync(t => t.OwnerId == ownerId, cancellationToken))
            {
                _logger.LogError(exception, "議事録テンプレートの初回配布に失敗しました。");
                throw;
            }

            _logger.LogInformation("議事録テンプレートの初回配布が競合したため、先に入ったほうを使います。");
        }

        _seededOwnerId = ownerId;
    }

    public async Task<IReadOnlyList<MinutesTemplate>> ListAsync(string ownerId, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(ownerId, cancellationToken);
        return await _db.MinutesTemplates
            .Where(t => t.OwnerId == ownerId)
            .OrderBy(t => t.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<MinutesTemplate?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(ownerId, cancellationToken);
        return await _db.MinutesTemplates
            .FirstOrDefaultAsync(t => t.Id == id && t.OwnerId == ownerId, cancellationToken);
    }

    public async Task<MinutesTemplate> GetDefaultAsync(string ownerId, CancellationToken cancellationToken)
    {
        await EnsureSeededAsync(ownerId, cancellationToken);

        // 既定が何らかの理由で欠けても画面が壊れないよう、名前順の先頭へ落ちる
        return await _db.MinutesTemplates
            .Where(t => t.OwnerId == ownerId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .FirstAsync(cancellationToken);
    }

    public async Task<MinutesTemplate?> CreateAsync(string ownerId, string name, string body, CancellationToken cancellationToken)
    {
        if (!IsWithinLimits(name, body))
        {
            return null;
        }

        await EnsureSeededAsync(ownerId, cancellationToken);
        var now = DateTime.UtcNow;
        var template = new MinutesTemplate
        {
            OwnerId = ownerId,
            Name = name.Trim(),
            Body = body,
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.MinutesTemplates.Add(template);
        await _db.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task<UpdateTemplateResult> UpdateAsync(Guid id, string ownerId, string name, string body, CancellationToken cancellationToken)
    {
        // 見つからないほうを先に見る。他人の Id なら、内容の良し悪しを知らせずに 404 で返したい。
        var template = await GetAsync(id, ownerId, cancellationToken);
        if (template is null)
        {
            return UpdateTemplateResult.NotFound;
        }

        if (!IsWithinLimits(name, body))
        {
            return UpdateTemplateResult.Invalid;
        }

        template.Name = name.Trim();
        template.Body = body;
        template.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return UpdateTemplateResult.Updated;
    }

    public async Task<MinutesTemplate?> DuplicateAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        var source = await GetAsync(id, ownerId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var copy = new MinutesTemplate
        {
            OwnerId = ownerId,
            Name = BuildCopyName(source.Name),
            Body = source.Body,
            IsDefault = false,
            CreatedAt = now,
            UpdatedAt = now
        };
        _db.MinutesTemplates.Add(copy);
        await _db.SaveChangesAsync(cancellationToken);
        return copy;
    }

    public async Task<bool> SetDefaultAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        if (await GetAsync(id, ownerId, cancellationToken) is null)
        {
            return false;
        }

        // 部分一意索引があるので、前の既定を外してから立てる。
        // 1 回の SaveChangesAsync では UPDATE の順序が保証されず、一時的に 2 行が既定になりうる。
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;
        await _db.MinutesTemplates
            .Where(t => t.OwnerId == ownerId && t.IsDefault && t.Id != id)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsDefault, false)
                    .SetProperty(t => t.UpdatedAt, now),
                cancellationToken);
        await _db.MinutesTemplates
            .Where(t => t.Id == id && t.OwnerId == ownerId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(t => t.IsDefault, true)
                    .SetProperty(t => t.UpdatedAt, now),
                cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // ExecuteUpdateAsync は ChangeTracker を経由しないので、追跡済みの行が古いまま残る
        _db.ChangeTracker.Clear();
        return true;
    }

    public async Task<DeleteTemplateResult> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        var template = await GetAsync(id, ownerId, cancellationToken);
        if (template is null)
        {
            return DeleteTemplateResult.NotFound;
        }

        if (template.IsDefault)
        {
            return DeleteTemplateResult.IsDefault;
        }

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        // 付け替え先は取引の中で選ぶ。外で選ぶと、選んでから消すまでの間に付け替え先そのものが
        // 消えることがあり、会議が失われた行を指したまま残る。
        var fallbackId = (await FindFallbackAsync(id, ownerId, cancellationToken))?.Id;

        // 使っていた会議を既定へ付け替えてから消す。外部キーの SetNull は安全網。
        // UpdatedAt は触らない。一覧の並び（更新日時の降順）を削除で入れ替えないため。
        // 残りが 1 つも無ければ空にする。生成のときに種が蒔き直され、そのときの既定へ落ちる。
        await _db.Meetings
            .Where(m => m.OwnerId == ownerId && m.MinutesTemplateId == id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(m => m.MinutesTemplateId, fallbackId),
                cancellationToken);
        // 削除は追跡を経由しない。SaveChangesAsync で消すと、追跡中の会議へ外部キーを
        // null へ戻す UPDATE が同じ保存で出て、上の付け替えを上書きしてしまう。
        await _db.MinutesTemplates
            .Where(t => t.Id == id && t.OwnerId == ownerId)
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // 付け替え先が無かった＝これが最後の 1 件だった。配布済みの覚えを捨てないと、
        // 同じ要求のうちに一覧を開いても 0 件のままになる。
        if (fallbackId is null)
        {
            _seededOwnerId = null;
        }

        _db.ChangeTracker.Clear();
        return DeleteTemplateResult.Deleted;
    }

    public async Task<int> CountMeetingsAsync(Guid id, string ownerId, CancellationToken cancellationToken)
    {
        if (await GetAsync(id, ownerId, cancellationToken) is null)
        {
            return 0;
        }

        return await _db.Meetings
            .CountAsync(m => m.OwnerId == ownerId && m.MinutesTemplateId == id, cancellationToken);
    }

    /// <summary>付け替え先は GetDefaultAsync では選べない。既定の行が失われていると名前順の先頭へ落ち、
    /// それが今まさに消すテンプレートでありうる。消したあとの残りだけから、同じ優先順で選ぶ。</summary>
    public Task<MinutesTemplate?> FindFallbackAsync(Guid excludedId, string ownerId, CancellationToken cancellationToken) =>
        _db.MinutesTemplates
            .AsNoTracking()
            .Where(t => t.OwnerId == ownerId && t.Id != excludedId)
            .OrderByDescending(t => t.IsDefault)
            .ThenBy(t => t.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> OwnsAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
        _db.MinutesTemplates.AnyAsync(t => t.Id == id && t.OwnerId == ownerId, cancellationToken);

    public async Task<string> ResolveForGenerationAsync(Meeting meeting, CancellationToken cancellationToken)
    {
        if (meeting.MinutesTemplateId is { } templateId)
        {
            var chosen = await _db.MinutesTemplates
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    t => t.Id == templateId && t.OwnerId == meeting.OwnerId,
                    cancellationToken);
            if (chosen is not null)
            {
                return chosen.Body;
            }
        }

        var fallback = await GetDefaultAsync(meeting.OwnerId, cancellationToken);
        return fallback.Body;
    }

    private static bool IsWithinLimits(string name, string body) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Trim().Length <= MinutesTemplateLimits.MaxNameChars
        && !string.IsNullOrWhiteSpace(body)
        && body.Length <= MinutesTemplateLimits.MaxBodyChars;

    /// <summary>「標準（コピー）」の形にする。上限を超えるときは元の名前の末尾を削る。
    /// 削る位置がサロゲートペアの途中に来ると文字が壊れるので、そのときは 1 つ手前で切る。</summary>
    private static string BuildCopyName(string name)
    {
        var room = MinutesTemplateLimits.MaxNameChars - CopySuffix.Length;
        if (name.Length <= room)
        {
            return name + CopySuffix;
        }

        var cut = char.IsHighSurrogate(name[room - 1]) ? room - 1 : room;
        return name[..cut] + CopySuffix;
    }
}
