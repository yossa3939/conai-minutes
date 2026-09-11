using System.Text;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class FileValidationTests
{
    private static FileStorageService CreateService(int maxFileSizeMb = 200)
    {
        var paths = new StoragePaths(Path.Combine(Path.GetTempPath(), "conai-validate", Guid.NewGuid().ToString("N")));
        var upload = Options.Create(new UploadOptions { MaxFileSizeMb = maxFileSizeMb });
        return new FileStorageService(paths, upload);
    }

    private static MemoryStream Bytes(params byte[] head)
    {
        var buffer = new byte[Math.Max(head.Length, 32)];
        head.CopyTo(buffer, 0);
        return new MemoryStream(buffer);
    }

    [Fact]
    public void PDFは参考資料として受け入れる()
    {
        var service = CreateService();
        using var content = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7"));

        var result = service.Validate("資料.PDF", "application/pdf", 1024, content);

        Assert.True(result.IsValid);
        Assert.Equal(MeetingFileKind.Reference, result.Kind);
        Assert.Equal(".pdf", result.Extension);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public void WAVはメディアとして受け入れる()
    {
        var service = CreateService();
        var buffer = new byte[32];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(buffer, 0);
        Encoding.ASCII.GetBytes("WAVE").CopyTo(buffer, 8);
        using var content = new MemoryStream(buffer);

        var result = service.Validate("rec.wav", "audio/wav", 2048, content);

        Assert.True(result.IsValid);
        Assert.Equal(MeetingFileKind.Media, result.Kind);
    }

    [Fact]
    public void 許可されない拡張子は拒否する()
    {
        var service = CreateService();
        using var content = Bytes(0x4D, 0x5A);

        var result = service.Validate("evil.exe", "application/octet-stream", 10, content);

        Assert.False(result.IsValid);
        Assert.Equal(FileValidationError.ExtensionNotAllowed, result.Error);
    }

    [Fact]
    public void 拡張子とMIMEが食い違うと拒否する()
    {
        var service = CreateService();
        using var content = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7"));

        var result = service.Validate("資料.pdf", "text/html", 10, content);

        Assert.False(result.IsValid);
        Assert.Equal(FileValidationError.ContentTypeMismatch, result.Error);
    }

    [Fact]
    public void 先頭バイトが形式と合わないと拒否する()
    {
        var service = CreateService();
        using var content = Bytes(Encoding.ASCII.GetBytes("<html>"));

        var result = service.Validate("資料.pdf", "application/pdf", 10, content);

        Assert.False(result.IsValid);
        Assert.Equal(FileValidationError.SignatureMismatch, result.Error);
    }

    [Fact]
    public void 上限を超えるサイズは拒否する()
    {
        var service = CreateService(maxFileSizeMb: 1);
        using var content = Bytes(Encoding.ASCII.GetBytes("%PDF-1.7"));

        var result = service.Validate("資料.pdf", "application/pdf", 2L * 1024 * 1024, content);

        Assert.False(result.IsValid);
        Assert.Equal(FileValidationError.TooLarge, result.Error);
        Assert.Contains("1", result.Message);
    }

    [Fact]
    public void 空のファイルは拒否する()
    {
        var service = CreateService();
        using var content = new MemoryStream();

        var result = service.Validate("資料.pdf", "application/pdf", 0, content);

        Assert.False(result.IsValid);
        Assert.Equal(FileValidationError.Empty, result.Error);
    }

    [Fact]
    public void mp3はシグネチャ検査を省いて受け入れる()
    {
        var service = CreateService();
        using var content = Bytes(0x00, 0x11, 0x22);

        var result = service.Validate("voice.mp3", "audio/mpeg", 4096, content);

        Assert.True(result.IsValid);
        Assert.Equal(MeetingFileKind.Media, result.Kind);
    }

    [Fact]
    public void 拡張子の一覧は宣言順で返る()
    {
        // 画面の accept 属性と案内文がこの並びで出る。並びを変えるときは、この期待値も直す。
        Assert.Equal(
            new[] { ".mp3", ".m4a", ".aac", ".flac", ".ogg", ".wav", ".mp4", ".mov", ".webm" },
            AllowedFileTypes.ExtensionsFor(MeetingFileKind.Media).ToArray());
        Assert.Equal(
            new[] { ".pdf", ".txt", ".md", ".docx", ".pptx", ".xlsx" },
            AllowedFileTypes.ExtensionsFor(MeetingFileKind.Reference).ToArray());
    }
}
