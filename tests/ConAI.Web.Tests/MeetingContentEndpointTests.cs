using System.Net;
using System.Net.Http.Headers;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class MeetingContentEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingContentEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "録音" }, CancellationToken.None);
    }

    private static byte[] WebmBytes()
    {
        var buffer = new byte[64];
        new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public async Task 録音を保存すると録音種別の添付になる()
    {
        var meeting = await CreateAsync("rec-a");
        var client = _factory.CreateClientAs("rec-a");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meeting.Id}"));

        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(WebmBytes());
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
        content.Add(file, "recording", "blob");

        var response = await client.PostAsync($"/api/meetings/{meeting.Id}/recording", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await service.GetAsync(meeting.Id, "rec-a", CancellationToken.None);
        Assert.Single(loaded!.Files);
        Assert.Equal(MeetingFileKind.Recording, loaded.Files[0].Kind);
        Assert.StartsWith("recording-", loaded.Files[0].OriginalFileName);
    }

    [Fact]
    public async Task 実体の保存に失敗したら添付行を残さない()
    {
        using var factory = new FailingSaveFactory();
        Guid meetingId;
        using (var scope = factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await service.CreateAsync("rec-e", new Meeting { Title = "録音" }, CancellationToken.None);
            meetingId = meeting.Id;
        }

        var client = factory.CreateClientAs("rec-e");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Edit/{meetingId}"));

        var content = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(WebmBytes());
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/webm");
        content.Add(file, "recording", "blob");

        var response = await client.PostAsync($"/api/meetings/{meetingId}/recording", content);

        Assert.NotEqual(HttpStatusCode.Created, response.StatusCode);

        using var verifyScope = factory.CreateScope();
        var meetings = verifyScope.ServiceProvider.GetRequiredService<IMeetingService>();
        var loaded = await meetings.GetAsync(meetingId, "rec-e", CancellationToken.None);
        Assert.Empty(loaded!.Files);
    }

    /// <summary>SaveAsync だけ必ず失敗させる。<see cref="IFileStorageService"/> の他の操作は本物に委譲する。</summary>
    private sealed class SaveAlwaysFailingStorage : IFileStorageService
    {
        private readonly IFileStorageService _inner;

        public SaveAlwaysFailingStorage(IFileStorageService inner) => _inner = inner;

        public FileValidationResult Validate(string fileName, string contentType, long sizeBytes, Stream content) =>
            _inner.Validate(fileName, contentType, sizeBytes, content);

        public Task SaveAsync(Guid meetingId, Guid fileId, string extension, Stream content, CancellationToken cancellationToken) =>
            throw new IOException("保存失敗を注入");

        public string GetPath(Guid meetingId, Guid fileId, string extension) =>
            _inner.GetPath(meetingId, fileId, extension);

        public Task<byte[]> ReadAllBytesAsync(Guid meetingId, Guid fileId, string extension, CancellationToken cancellationToken) =>
            _inner.ReadAllBytesAsync(meetingId, fileId, extension, cancellationToken);

        public void Delete(Guid meetingId, Guid fileId, string extension) => _inner.Delete(meetingId, fileId, extension);

        public void DeleteMeetingDirectory(Guid meetingId) => _inner.DeleteMeetingDirectory(meetingId);
    }

    /// <summary>実体の保存に失敗する状況を再現するため、IFileStorageService を差し替えたファクトリ。</summary>
    private sealed class FailingSaveFactory : ConAIWebApplicationFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFileStorageService>();
                services.AddSingleton<IFileStorageService>(provider => new SaveAlwaysFailingStorage(
                    new FileStorageService(
                        provider.GetRequiredService<StoragePaths>(),
                        provider.GetRequiredService<IOptions<UploadOptions>>())));
            });
        }
    }
}
