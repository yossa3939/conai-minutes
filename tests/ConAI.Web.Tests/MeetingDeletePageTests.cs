using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingDeletePageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingDeletePageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "削除対象" }, CancellationToken.None);
    }

    [Fact]
    public async Task 確認ページからの削除で一覧へ戻り実体ファイルも消える()
    {
        var meeting = await CreateAsync("del-a");
        string uploadDir;
        using (var scope = _factory.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<IFileStorageService>();
            using var content = new MemoryStream(new byte[] { 1, 2, 3 });
            var fileId = Guid.NewGuid();
            await storage.SaveAsync(meeting.Id, fileId, ".txt", content, CancellationToken.None);
            uploadDir = Path.GetDirectoryName(storage.GetPath(meeting.Id, fileId, ".txt"))!;
        }

        var client = _factory.CreateClientAs("del-a");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(await client.GetStringAsync($"/Meetings/Delete/{meeting.Id}"));

        var response = await client.PostAsync($"/Meetings/Delete/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/Meetings", response.Headers.Location!.ToString());
        Assert.False(Directory.Exists(uploadDir));
    }

    [Fact]
    public async Task 生成中は削除ボタンを非活性にしサーバでも拒否する()
    {
        var meeting = await CreateAsync("del-b");
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await service.TryMarkQueuedAsync(meeting.Id, "del-b", CancellationToken.None);
        }

        var client = _factory.CreateClientAs("del-b");
        var html = await client.GetStringAsync($"/Meetings/Delete/{meeting.Id}");
        var token = HtmlTestHelpers.ExtractAntiforgeryToken(html);
        // 動的出力の日本語は数値文字参照で出るため、デコードしてから比較する
        var decoded = WebUtility.HtmlDecode(html);

        Assert.Contains("disabled", decoded);
        Assert.Contains("議事録を生成しています", decoded);

        var response = await client.PostAsync($"/Meetings/Delete/{meeting.Id}",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task 他人の削除ページは404になる()
    {
        var meeting = await CreateAsync("del-c");
        var client = _factory.CreateClientAs("del-d");

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/Meetings/Delete/{meeting.Id}")).StatusCode);
    }
}
