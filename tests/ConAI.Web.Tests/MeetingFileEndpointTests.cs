using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingFileEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingFileEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "添付" }, CancellationToken.None);
    }

    private static MultipartFormDataContent Multipart(string token, string fileName, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent
        {
            { new StringContent(token), "__RequestVerificationToken" }
        };
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        content.Add(file, "files", fileName);
        return content;
    }

    private static byte[] PdfBytes()
    {
        var buffer = new byte[64];
        Encoding.ASCII.GetBytes("%PDF-1.7").CopyTo(buffer, 0);
        return buffer;
    }

    private async Task<(HttpClient Client, string Token)> AuthenticatedAsync(string userId, Guid meetingId)
    {
        var client = _factory.CreateClientAs(userId);
        var html = await client.GetStringAsync($"/Meetings/Edit/{meetingId}");
        return (client, HtmlTestHelpers.ExtractAntiforgeryToken(html));
    }

    [Fact]
    public async Task 参考資料をアップロードして一覧に載る()
    {
        var meeting = await CreateAsync("file-a");
        var (client, token) = await AuthenticatedAsync("file-a", meeting.Id);

        var response = await client.PostAsync($"/api/meetings/{meeting.Id}/files",
            Multipart(token, "資料.pdf", "application/pdf", PdfBytes()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadResponseDto>();
        Assert.Single(body!.Accepted);
        Assert.Equal("Reference", body.Accepted[0].Kind);
        Assert.Equal("資料.pdf", body.Accepted[0].OriginalFileName);
    }

    [Fact]
    public async Task 許可されない形式は却下されて400になる()
    {
        var meeting = await CreateAsync("file-b");
        var (client, token) = await AuthenticatedAsync("file-b", meeting.Id);

        var response = await client.PostAsync($"/api/meetings/{meeting.Id}/files",
            Multipart(token, "evil.exe", "application/octet-stream", new byte[] { 0x4D, 0x5A, 0x90 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadResponseDto>();
        Assert.Empty(body!.Accepted);
        Assert.Single(body.Rejected);
    }

    [Fact]
    public async Task Antiforgeryトークンなしのアップロードは400になる()
    {
        var meeting = await CreateAsync("file-c");
        var client = _factory.CreateClientAs("file-c");

        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(PdfBytes());
        file.Headers.ContentType = MediaTypeHeaderValue.Parse("application/pdf");
        content.Add(file, "files", "資料.pdf");

        var response = await client.PostAsync($"/api/meetings/{meeting.Id}/files", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task 他人の会議へのアップロードは404になる()
    {
        var meeting = await CreateAsync("file-d");
        var (client, token) = await AuthenticatedAsync("file-d", meeting.Id);
        var otherClient = _factory.CreateClientAs("file-e");
        await otherClient.GetAsync("/Meetings");

        var response = await otherClient.PostAsync($"/api/meetings/{meeting.Id}/files",
            Multipart(token, "資料.pdf", "application/pdf", PdfBytes()));

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ダウンロードはattachmentとnosniffで返る()
    {
        var meeting = await CreateAsync("file-f");
        var (client, token) = await AuthenticatedAsync("file-f", meeting.Id);
        var upload = await client.PostAsync($"/api/meetings/{meeting.Id}/files",
            Multipart(token, "資料.pdf", "application/pdf", PdfBytes()));
        var uploaded = (await upload.Content.ReadFromJsonAsync<UploadResponseDto>())!.Accepted[0];

        var response = await client.GetAsync($"/api/meetings/{meeting.Id}/files/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
    }

    [Fact]
    public async Task 削除すると実体ファイルも消える()
    {
        var meeting = await CreateAsync("file-g");
        var (client, token) = await AuthenticatedAsync("file-g", meeting.Id);
        var upload = await client.PostAsync($"/api/meetings/{meeting.Id}/files",
            Multipart(token, "資料.pdf", "application/pdf", PdfBytes()));
        var uploaded = (await upload.Content.ReadFromJsonAsync<UploadResponseDto>())!.Accepted[0];

        string path;
        using (var scope = _factory.CreateScope())
        {
            path = scope.ServiceProvider.GetRequiredService<IFileStorageService>()
                .GetPath(meeting.Id, uploaded.Id, ".pdf");
        }

        // DELETE は JSON 本文なので multipart 自動検証の対象外。ヘッダーでトークンを渡す。
        await client.WithCsrfTokenAsync();
        var response = await client.DeleteAsync($"/api/meetings/{meeting.Id}/files/{uploaded.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(path));
    }

    public sealed record UploadedFileResponseDto(Guid Id, string Kind, string OriginalFileName, long SizeBytes);

    public sealed record RejectedFileResponseDto(string FileName, string Message);

    public sealed record UploadResponseDto(
        List<UploadedFileResponseDto> Accepted,
        List<RejectedFileResponseDto> Rejected);
}
