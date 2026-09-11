using System.Net;
using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MeetingPageDesignTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MeetingPageDesignTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Meeting> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return await service.CreateAsync(ownerId, new Meeting { Title = "見た目の確認" }, CancellationToken.None);
    }

    [Fact]
    public async Task 案内ページが新しい部品クラスで組まれている()
    {
        using var client = _factory.CreateClient();

        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/"));

        Assert.Contains("card", html);
        Assert.Contains("page-title", html);
        Assert.Contains("btn-primary", html);
        Assert.DoesNotContain("display-5", html);
        Assert.DoesNotContain("lead", html);
        Assert.DoesNotContain("btn-lg", html);
    }

    [Fact]
    public async Task 会議一覧が新しい部品クラスで組まれている()
    {
        var meeting = await CreateAsync("design-list");
        using (var scope = _factory.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            await service.TryMarkQueuedAsync(meeting.Id, "design-list", CancellationToken.None);
        }

        var client = _factory.CreateClientAs("design-list");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings"));

        Assert.Contains("table-scroll", html);
        Assert.Contains("data-table", html);
        Assert.Contains("badge-waiting", html);
        Assert.Contains("待機中", html);
        Assert.DoesNotContain("table-hover", html);
        Assert.DoesNotContain("text-bg-", html);
        Assert.DoesNotContain("btn-outline-", html);
        Assert.DoesNotContain("d-flex", html);
    }

    [Fact]
    public async Task 会議の作成と削除のページが新しい部品クラスで組まれている()
    {
        var meeting = await CreateAsync("design-form");
        var client = _factory.CreateClientAs("design-form");

        var create = WebUtility.HtmlDecode(await client.GetStringAsync("/Meetings/Create"));

        Assert.Contains("form-label", create);
        Assert.Contains("input-field", create);
        Assert.Contains("checkbox-field", create);
        Assert.Contains("btn-primary", create);
        Assert.DoesNotContain("form-control", create);
        Assert.DoesNotContain("form-select", create);
        Assert.DoesNotContain("form-check", create);
        Assert.DoesNotContain("col-lg-", create);

        var delete = WebUtility.HtmlDecode(await client.GetStringAsync($"/Meetings/Delete/{meeting.Id}"));

        Assert.Contains("btn-destructive", delete);
        Assert.Contains("card", delete);
        Assert.DoesNotContain("btn-danger", delete);
        Assert.DoesNotContain("col-sm-", delete);
    }
}
