using ConAI.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class WebhookEndpointStorageTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public WebhookEndpointStorageTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private static WebhookEndpoint NewEndpoint(string ownerId, string fingerprint) => new()
    {
        OwnerId = ownerId,
        Name = "開発チーム",
        Kind = WebhookKind.Slack,
        ProtectedUrl = "protected-value",
        UrlHint = "hooks.slack.com/…/pQ7x",
        UrlFingerprint = fingerprint
    };

    [Fact]
    public async Task 同じ利用者が同じURLを二度登録すると保存に失敗する()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fingerprint = new string('a', 64);

        db.WebhookEndpoints.Add(NewEndpoint("store-dup", fingerprint));
        await db.SaveChangesAsync();

        db.WebhookEndpoints.Add(NewEndpoint("store-dup", fingerprint));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 別の利用者なら同じURLを登録できる()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var fingerprint = new string('b', 64);

        db.WebhookEndpoints.Add(NewEndpoint("store-user-1", fingerprint));
        db.WebhookEndpoints.Add(NewEndpoint("store-user-2", fingerprint));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.WebhookEndpoints.CountAsync(e => e.UrlFingerprint == fingerprint));
    }

    [Fact]
    public async Task 種別と最終結果は読み戻せる()
    {
        using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var endpoint = NewEndpoint("store-roundtrip", new string('c', 64));
        endpoint.Kind = WebhookKind.MicrosoftTeams;
        endpoint.LastStatus = WebhookDeliveryStatus.Failed;
        endpoint.LastError = "宛先が見つかりません（HTTP 404）。";
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.WebhookEndpoints.SingleAsync(e => e.Id == endpoint.Id);

        Assert.Equal(WebhookKind.MicrosoftTeams, stored.Kind);
        Assert.Equal(WebhookDeliveryStatus.Failed, stored.LastStatus);
        Assert.Equal("宛先が見つかりません（HTTP 404）。", stored.LastError);
    }
}
