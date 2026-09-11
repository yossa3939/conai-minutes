using ConAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Services;

public sealed class FileStorageService : IFileStorageService
{
    private const int HeadLength = 32;

    private readonly StoragePaths _paths;
    private readonly UploadOptions _uploadOptions;

    public FileStorageService(StoragePaths paths, IOptions<UploadOptions> uploadOptions)
    {
        _paths = paths;
        _uploadOptions = uploadOptions.Value;
    }

    public FileValidationResult Validate(string fileName, string contentType, long sizeBytes, Stream content)
    {
        if (sizeBytes <= 0)
        {
            return FileValidationResult.Fail(FileValidationError.Empty, "空のファイルはアップロードできません。");
        }

        if (sizeBytes > _uploadOptions.MaxFileSizeBytes)
        {
            return FileValidationResult.Fail(
                FileValidationError.TooLarge,
                $"ファイルサイズが上限（{_uploadOptions.MaxFileSizeMb} MB）を超えています。");
        }

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedFileTypes.TryGet(extension, out var kind, out var mimeTypes))
        {
            return FileValidationResult.Fail(
                FileValidationError.ExtensionNotAllowed,
                "この形式のファイルは登録できません。音声、動画、または pdf / txt / md / docx / pptx / xlsx を選んでください。");
        }

        var normalizedContentType = (contentType ?? string.Empty).Split(';')[0].Trim();
        if (!mimeTypes.Any(m => string.Equals(m, normalizedContentType, StringComparison.OrdinalIgnoreCase)))
        {
            return FileValidationResult.Fail(
                FileValidationError.ContentTypeMismatch,
                "ファイルの種類が拡張子と一致しません。");
        }

        var head = new byte[HeadLength];
        var originalPosition = content.CanSeek ? content.Position : 0;
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        var read = content.ReadAtLeast(head, HeadLength, throwOnEndOfStream: false);

        if (content.CanSeek)
        {
            content.Position = originalPosition;
        }

        if (!AllowedFileTypes.MatchesSignature(extension, head.AsSpan(0, read)))
        {
            return FileValidationResult.Fail(
                FileValidationError.SignatureMismatch,
                "ファイルの中身が拡張子と一致しません。");
        }

        return FileValidationResult.Ok(kind, extension, normalizedContentType);
    }

    public string GetPath(Guid meetingId, Guid fileId, string extension) =>
        Path.Combine(_paths.UploadsRoot, meetingId.ToString(), fileId.ToString() + extension);

    public async Task SaveAsync(Guid meetingId, Guid fileId, string extension, Stream content, CancellationToken cancellationToken)
    {
        var path = GetPath(meetingId, fileId, extension);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await content.CopyToAsync(target, cancellationToken);
    }

    public async Task<byte[]> ReadAllBytesAsync(Guid meetingId, Guid fileId, string extension, CancellationToken cancellationToken)
    {
        var path = GetPath(meetingId, fileId, extension);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("保存されたファイルが見つかりません。", path);
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    public void Delete(Guid meetingId, Guid fileId, string extension)
    {
        var path = GetPath(meetingId, fileId, extension);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    public void DeleteMeetingDirectory(Guid meetingId)
    {
        var directory = Path.Combine(_paths.UploadsRoot, meetingId.ToString());
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
