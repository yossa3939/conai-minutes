using ConAI.Web.Data;

namespace ConAI.Web.Services;

public enum FileValidationError
{
    None = 0,
    Empty,
    ExtensionNotAllowed,
    ContentTypeMismatch,
    SignatureMismatch,
    TooLarge
}

public sealed record FileValidationResult(
    bool IsValid,
    FileValidationError Error,
    string Message,
    MeetingFileKind Kind,
    string Extension,
    string ContentType)
{
    public static FileValidationResult Ok(MeetingFileKind kind, string extension, string contentType) =>
        new(true, FileValidationError.None, string.Empty, kind, extension, contentType);

    public static FileValidationResult Fail(FileValidationError error, string message) =>
        new(false, error, message, default, string.Empty, string.Empty);
}

public interface IFileStorageService
{
    FileValidationResult Validate(string fileName, string contentType, long sizeBytes, Stream content);

    Task SaveAsync(Guid meetingId, Guid fileId, string extension, Stream content, CancellationToken cancellationToken);

    string GetPath(Guid meetingId, Guid fileId, string extension);

    Task<byte[]> ReadAllBytesAsync(Guid meetingId, Guid fileId, string extension, CancellationToken cancellationToken);

    void Delete(Guid meetingId, Guid fileId, string extension);

    void DeleteMeetingDirectory(Guid meetingId);
}
