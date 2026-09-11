using System.Security.Claims;
using ConAI.Web.Data;
using ConAI.Web.Services;

namespace ConAI.Web.Endpoints;

public sealed record UploadedFileDto(Guid Id, string Kind, string OriginalFileName, long SizeBytes);

public sealed record RejectedFileDto(string FileName, string Message);

public sealed record UploadResponse(IReadOnlyList<UploadedFileDto> Accepted, IReadOnlyList<RejectedFileDto> Rejected);

public static class MeetingFileEndpoints
{
    public static RouteGroupBuilder MapMeetingFileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/meetings/{meetingId:guid}/files").RequireAuthorization();

        group.MapPost("/", UploadAsync);
        group.MapGet("/{fileId:guid}", DownloadAsync);
        group.MapDelete("/{fileId:guid}", DeleteAsync);

        return group;
    }

    private static async Task<IResult> UploadAsync(
        Guid meetingId,
        IFormFileCollection files,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IFileStorageService storage,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var meeting = await meetings.GetAsync(meetingId, ownerId, cancellationToken);
        if (meeting is null)
        {
            return Results.NotFound();
        }

        if (files.Count == 0)
        {
            return Results.BadRequest(new UploadResponse(
                Array.Empty<UploadedFileDto>(),
                new[] { new RejectedFileDto(string.Empty, "ファイルが選択されていません。") }));
        }

        var accepted = new List<UploadedFileDto>();
        var rejected = new List<RejectedFileDto>();

        foreach (var file in files)
        {
            await using var stream = file.OpenReadStream();
            var validation = storage.Validate(file.FileName, file.ContentType, file.Length, stream);
            if (!validation.IsValid)
            {
                rejected.Add(new RejectedFileDto(file.FileName, validation.Message));
                continue;
            }

            var entity = new MeetingFile
            {
                Kind = validation.Kind,
                OriginalFileName = Path.GetFileName(file.FileName),
                Extension = validation.Extension,
                ContentType = validation.ContentType,
                SizeBytes = file.Length
            };

            var saved = await meetings.AddFileAsync(meetingId, ownerId, entity, cancellationToken);
            if (saved is null)
            {
                return Results.NotFound();
            }

            try
            {
                await using var content = file.OpenReadStream();
                await storage.SaveAsync(meetingId, saved.Id, saved.Extension, content, cancellationToken);
            }
            catch
            {
                // 実体を書けなかった添付行を残すと、以後の生成が毎回 FileNotFoundException で失敗する。
                // 失敗した 1 件だけを消し、既に成功した分は受理のまま残す。
                try
                {
                    await meetings.RemoveFileAsync(meetingId, ownerId, saved.Id, CancellationToken.None);
                }
                catch
                {
                    // 行の削除に失敗しても、保存の失敗を見落とさない。
                }

                rejected.Add(new RejectedFileDto(saved.OriginalFileName, "ファイルを保存できませんでした。"));
                continue;
            }

            accepted.Add(new UploadedFileDto(saved.Id, saved.Kind.ToString(), saved.OriginalFileName, saved.SizeBytes));
        }

        var response = new UploadResponse(accepted, rejected);
        return accepted.Count == 0
            ? Results.BadRequest(response)
            : Results.Created($"/api/meetings/{meetingId}/files", response);
    }

    private static async Task<IResult> DownloadAsync(
        Guid meetingId,
        Guid fileId,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IFileStorageService storage,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var file = await meetings.GetFileAsync(meetingId, ownerId, fileId, cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        var path = storage.GetPath(meetingId, file.Id, file.Extension);
        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        return Results.File(path, file.ContentType, file.OriginalFileName);
    }

    private static async Task<IResult> DeleteAsync(
        Guid meetingId,
        Guid fileId,
        ClaimsPrincipal user,
        IMeetingService meetings,
        IFileStorageService storage,
        CancellationToken cancellationToken)
    {
        var ownerId = user.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var file = await meetings.GetFileAsync(meetingId, ownerId, fileId, cancellationToken);
        if (file is null)
        {
            return Results.NotFound();
        }

        if (!await meetings.RemoveFileAsync(meetingId, ownerId, fileId, cancellationToken))
        {
            return Results.NotFound();
        }

        storage.Delete(meetingId, file.Id, file.Extension);
        return Results.NoContent();
    }
}
