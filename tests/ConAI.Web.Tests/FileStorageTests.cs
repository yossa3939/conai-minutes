using System.Text;
using ConAI.Web.Configuration;
using ConAI.Web.Services;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class FileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "conai-storage", Guid.NewGuid().ToString("N"));
    private readonly FileStorageService _service;

    public FileStorageTests()
    {
        var paths = new StoragePaths(_root);
        paths.EnsureCreated();
        _service = new FileStorageService(paths, Options.Create(new UploadOptions()));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void 保存パスは会議IDとファイルIDだけで決まる()
    {
        var meetingId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        var path = _service.GetPath(meetingId, fileId, ".pdf");

        Assert.Equal(
            Path.Combine(_root, "uploads", meetingId.ToString(), fileId + ".pdf"),
            path);
    }

    [Fact]
    public async Task 保存して読み戻せる()
    {
        var meetingId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("hello conai");
        using var source = new MemoryStream(payload);

        await _service.SaveAsync(meetingId, fileId, ".txt", source, CancellationToken.None);
        var loaded = await _service.ReadAllBytesAsync(meetingId, fileId, ".txt", CancellationToken.None);

        Assert.Equal(payload, loaded);
        Assert.True(File.Exists(_service.GetPath(meetingId, fileId, ".txt")));
    }

    [Fact]
    public async Task 個別削除は存在しなくても例外を投げない()
    {
        var meetingId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        using var source = new MemoryStream(new byte[] { 1, 2, 3 });
        await _service.SaveAsync(meetingId, fileId, ".txt", source, CancellationToken.None);

        _service.Delete(meetingId, fileId, ".txt");
        _service.Delete(meetingId, fileId, ".txt");

        Assert.False(File.Exists(_service.GetPath(meetingId, fileId, ".txt")));
    }

    [Fact]
    public async Task 会議ディレクトリごと削除できる()
    {
        var meetingId = Guid.NewGuid();
        using var source = new MemoryStream(new byte[] { 1 });
        await _service.SaveAsync(meetingId, Guid.NewGuid(), ".txt", source, CancellationToken.None);

        _service.DeleteMeetingDirectory(meetingId);
        _service.DeleteMeetingDirectory(meetingId);

        Assert.False(Directory.Exists(Path.Combine(_root, "uploads", meetingId.ToString())));
    }

    [Fact]
    public async Task 存在しないファイルの読み出しはFileNotFoundになる()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ReadAllBytesAsync(Guid.NewGuid(), Guid.NewGuid(), ".txt", CancellationToken.None));
    }
}
