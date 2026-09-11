using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using ConAI.Web.Data;
using ConAI.Web.Search;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class SearchEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public SearchEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private sealed record SearchResponseBody(
        [property: JsonPropertyName("status")] string Status,
        [property: JsonPropertyName("total")] int Total,
        [property: JsonPropertyName("page")] int Page,
        [property: JsonPropertyName("pageSize")] int PageSize,
        [property: JsonPropertyName("indexingRemaining")] int IndexingRemaining,
        [property: JsonPropertyName("results")] IReadOnlyList<SearchResultBody> Results,
        [property: JsonPropertyName("notes")] IReadOnlyList<string> Notes);

    private sealed record SearchResultBody(
        [property: JsonPropertyName("meetingId")] Guid MeetingId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("heldAtText")] string HeldAtText,
        [property: JsonPropertyName("excerpt")] string Excerpt,
        [property: JsonPropertyName("matchedIn")] string MatchedIn);

    private static async Task<string?> MessageOf(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<Dictionary<string, string>>();

        return body?["message"];
    }

    [Fact]
    public async Task 未認証では401を返す()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "予算" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CSRFトークンが無いと400を返す()
    {
        using var client = _factory.CreateClientAs("search-api-csrf");

        var response = await client.PostAsJsonAsync("/api/search", new { query = "予算" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task JSONでない本文は400を返す()
    {
        using var client = await _factory.CreateClientAs("search-api-form").WithCsrfTokenAsync();

        using var content = new StringContent("query=予算", Encoding.UTF8, "application/x-www-form-urlencoded");
        var response = await client.PostAsync("/api/search", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("リクエストの本文を読み取れませんでした。", await MessageOf(response));
    }

    [Fact]
    public async Task 空の検索語は400を返す()
    {
        using var client = await _factory.CreateClientAs("search-api-empty").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("検索語を入力してください。", await MessageOf(response));
    }

    [Fact]
    public async Task 長すぎる検索語は400を返す()
    {
        using var client = await _factory.CreateClientAs("search-api-long").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            "/api/search", new { query = new string('あ', SearchLimits.MaxQueryChars + 1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await MessageOf(response);
        Assert.StartsWith("検索語は", message);
        Assert.Contains(SearchLimits.MaxQueryChars.ToString("N0", CultureInfo.InvariantCulture), message);
    }

    [Fact]
    public async Task 該当した会議が抜粋つきで返る()
    {
        const string owner = "search-api-found";
        await AddAndIndexAsync(owner, "予算検討会", "島田さんから予算の見直しについて話があった");

        using var client = await _factory.CreateClientAs(owner).WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "予算", page = 1 });
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>();

        Assert.NotNull(body);
        Assert.Equal("found", body.Status);
        Assert.Equal(SearchLimits.PageSize, body.PageSize);
        Assert.Equal("予算検討会", body.Results[0].Title);
        Assert.Equal("minutes", body.Results[0].MatchedIn);
        Assert.Equal("2026/08/12 10:00:00", body.Results[0].HeldAtText);
    }

    [Fact]
    public async Task 該当が無ければemptyを返す()
    {
        const string owner = "search-api-empty-result";
        await AddAndIndexAsync(owner, "定例会", "昼食の話をした");

        using var client = await _factory.CreateClientAs(owner).WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync("/api/search", new { query = "存在しない語句" });
        var body = await response.Content.ReadFromJsonAsync<SearchResponseBody>();

        Assert.NotNull(body);
        Assert.Equal("empty", body.Status);
        Assert.Empty(body.Results);
    }

    private async Task AddAndIndexAsync(string ownerId, string title, string minutes)
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var meeting = new Meeting
        {
            OwnerId = ownerId,
            Title = title,
            Minutes = minutes,
            HeldAt = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-10)
        };

        db.Meetings.Add(meeting);
        await db.SaveChangesAsync();

        var indexer = scope.ServiceProvider.GetRequiredService<IMeetingSearchIndexer>();
        await indexer.IndexAsync(meeting.Id, CancellationToken.None);
    }
}
