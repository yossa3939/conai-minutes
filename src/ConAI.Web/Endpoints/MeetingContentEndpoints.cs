using System.Security.Claims;
using System.Text;
using ConAI.Web.Data;
using ConAI.Web.Services;

namespace ConAI.Web.Endpoints;

public sealed record RecordingSavedDto(Guid Id, string OriginalFileName, long SizeBytes);

public static class MeetingContentEndpoints
{
    public static RouteGroupBuilder MapMeetingContentEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/meetings/{meetingId:guid}").RequireAuthorization();

        group.MapPost("/recording", SaveRecordingAsync);
        group.MapGet("/minutes", DownloadMinutesAsync);

        return group;
    }

    /// <summary>ダウンロードのファイル名に使えない文字。閲覧者の OS が Windows でも通るよう、Windows の禁止文字で固定する。</summary>
    private static readonly char[] InvalidFileNameChars = ['\\', '/', ':', '*', '?', '"', '<', '>', '|'];

    /// <summary>Windows が装置として扱う名前。拡張子が付いていても装置のままなので、そのままでは保存できない。</summary>
    private static readonly HashSet<string> ReservedFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>ファイル名の上限。多くのファイルシステムが 255 バイトを上限にしているので、余裕を見て 200 で切る。</summary>
    private const int MaxFileNameBytes = 200;

    private static async Task<IResult> DownloadMinutesAsync(
        Guid meetingId,
        string? format,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IMarkdownRenderer markdown,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var asText = string.Equals(format, "txt", StringComparison.OrdinalIgnoreCase);
        if (!asText && format is not null && !string.Equals(format, "md", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { message = "format には md か txt を指定してください。" });
        }

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var meeting = await meetings.GetAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null || string.IsNullOrWhiteSpace(meeting.Minutes))
        {
            return Results.NotFound();
        }

        var body = asText ? markdown.ToPlainText(meeting.Minutes) : meeting.Minutes;
        var fileName = ToFileName(meeting.Title, asText ? ".txt" : ".md");

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(
            Encoding.UTF8.GetBytes(body),
            asText ? "text/plain; charset=utf-8" : "text/markdown; charset=utf-8",
            fileName);
    }

    /// <summary>会議名から拡張子まで含むファイル名を作る。使えない文字と制御文字は _ に置き換え、
    /// 末尾の点と空白を落とし、装置名を避け、UTF-8 で 200 バイト以内に収める。空なら「議事録」にする。</summary>
    private static string ToFileName(string title, string extension)
    {
        // Windows は末尾の点と空白を落として保存する。ここで落とさないと、拡張子の前に点が並ぶ。
        var name = new string(title.Trim()
                .Select(c => char.IsControl(c) || InvalidFileNameChars.Contains(c) ? '_' : c)
                .ToArray())
            .TrimEnd('.', ' ');

        // 装置かどうかは最初の点より前で決まる。「aux.部門」も装置として扱われる。
        // うしろの空白は Windows が読み飛ばすので、「CON .部門」も同じく装置になる。
        if (ReservedFileNames.Contains(name.Split('.')[0].TrimEnd(' ')))
        {
            name = '_' + name;
        }

        var truncated = TruncateUtf8(name, MaxFileNameBytes - extension.Length).TrimEnd('.', ' ');
        return (truncated.Length == 0 ? "議事録" : truncated) + extension;
    }

    /// <summary>UTF-8 で budget バイト以内に収める。境界がサロゲートペアの途中に来ると文字が壊れるので、
    /// 文字（Rune）ごとに足しては超えた時点でやめる。</summary>
    private static string TruncateUtf8(string name, int budget)
    {
        if (Encoding.UTF8.GetByteCount(name) <= budget)
        {
            return name;
        }

        var builder = new StringBuilder();
        var used = 0;
        foreach (var rune in name.EnumerateRunes())
        {
            if (used + rune.Utf8SequenceLength > budget)
            {
                break;
            }

            builder.Append(rune.ToString());
            used += rune.Utf8SequenceLength;
        }

        return builder.ToString();
    }

    private static async Task<IResult> SaveRecordingAsync(
        Guid meetingId,
        IFormFile recording,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IFileStorageService storage,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("ConAI.Web.Endpoints.MeetingContentEndpoints");

        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (await meetings.GetAsync(meetingId, ownerId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var extension = recording.ContentType.Contains("mp4", StringComparison.OrdinalIgnoreCase) ? ".mp4" : ".webm";
        if (!AllowedFileTypes.RecordingExtensions.Contains(extension))
        {
            return Results.BadRequest(new { message = "この形式の録音は保存できません。" });
        }

        var fileName = $"recording-{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}{extension}";

        await using var validationStream = recording.OpenReadStream();
        var validation = storage.Validate(fileName, recording.ContentType, recording.Length, validationStream);
        if (!validation.IsValid)
        {
            return Results.BadRequest(new { message = validation.Message });
        }

        var entity = new MeetingFile
        {
            Kind = MeetingFileKind.Recording,
            OriginalFileName = fileName,
            Extension = extension,
            ContentType = validation.ContentType,
            SizeBytes = recording.Length
        };

        var saved = await meetings.AddFileAsync(meetingId, ownerId, entity, cancellationToken);
        if (saved is null)
        {
            return Results.NotFound();
        }

        try
        {
            await using var content = recording.OpenReadStream();
            await storage.SaveAsync(meetingId, saved.Id, extension, content, cancellationToken);
        }
        catch (Exception ex)
        {
            // 実体を書けなかった添付行を残すと、以後の生成が毎回 FileNotFoundException で失敗する。
            logger.LogError(ex, "録音の実体保存に失敗しました。meetingId={MeetingId} fileId={FileId} size={SizeBytes}", meetingId, saved.Id, saved.SizeBytes);
            try
            {
                await meetings.RemoveFileAsync(meetingId, ownerId, saved.Id, CancellationToken.None);
            }
            catch
            {
                // 行の削除に失敗しても、保存の失敗を優先して伝える。
            }

            throw;
        }

        logger.LogInformation("録音を保存しました。meetingId={MeetingId} fileId={FileId} size={SizeBytes}", meetingId, saved.Id, saved.SizeBytes);
        return Results.Created(
            $"/api/meetings/{meetingId}/files/{saved.Id}",
            new RecordingSavedDto(saved.Id, saved.OriginalFileName, saved.SizeBytes));
    }
}
