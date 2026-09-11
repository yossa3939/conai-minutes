using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public static class CsrfTestHelpers
{
    public static async Task<HttpClient> WithCsrfTokenAsync(this HttpClient client, string pageUrl = "/Meetings")
    {
        var html = await client.GetStringAsync(pageUrl);
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", HtmlTestHelpers.ExtractCsrfMeta(html));
        return client;
    }
}

public class AntiforgeryTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public AntiforgeryTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "CSRF" }, CancellationToken.None);
    }

    // JSON で本文を送る更新 API（PUT /transcription・PUT /minutes）は保存ボタン一本化（RV 3）で削除された。
    // 残る書き込み API（ファイル削除・生成・録音・添付）の検証は、下の 3 つが担う。

    [Fact]
    public async Task トークンなしのJSON削除は400になる()
    {
        var meeting = await CreateAsync("csrf-b");
        var client = _factory.CreateClientAs("csrf-b");

        var response = await client.DeleteAsync($"/api/meetings/{meeting.Id}/files/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task トークンなしのform形式の本文なし要求も400になる()
    {
        // 本文を束縛しないエンドポイントは UseAntiforgery の自動検証を受けない。
        // form Content-Type を付けても（内容種別に関わらず）フィルタが検証することを確かめる
        // （素通しだとハンドラまで届き 404/204 になる）。
        var meeting = await CreateAsync("csrf-d");
        var client = _factory.CreateClientAs("csrf-d");
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/meetings/{meeting.Id}/files/{Guid.NewGuid()}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["dummy"] = "1" })
        };

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task トークンなしのform形式の生成開始は400になる()
    {
        // 本文なしの POST はクロスサイトの <form method=post> から送れる。内容種別に関わらず 400 で止める。
        var meeting = await CreateAsync("csrf-e");
        var client = _factory.CreateClientAs("csrf-e");

        var response = await client.PostAsync(
            $"/api/meetings/{meeting.Id}/generation",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["dummy"] = "1" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
