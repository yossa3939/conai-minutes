using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Notifications;
using ConAI.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class WebhookEndpointServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private const string SlackUrl = "https://hooks.slack.com/services/T00000000/B00000000/pQ7x";

    private readonly ConAIWebApplicationFactory _factory;

    public WebhookEndpointServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private static WebhookEndpointEdit Edit(string name = "開発チーム", string? url = SlackUrl) =>
        new(name, WebhookKind.Slack, url, NotifyOnSuccess: true, NotifyOnFailure: true, IsEnabled: true);

    [Fact]
    public async Task 登録した宛先のURLは平文で保存されない()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var (result, error) = await service.CreateAsync("svc-protect", Edit(), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);
        Assert.Null(error);

        var stored = await db.WebhookEndpoints.AsNoTracking().SingleAsync(e => e.OwnerId == "svc-protect");
        Assert.DoesNotContain("hooks.slack.com", stored.ProtectedUrl);
        Assert.DoesNotContain("pQ7x", stored.ProtectedUrl);
        Assert.Equal("hooks.slack.com/…/pQ7x", stored.UrlHint);

        var protector = scope.ServiceProvider.GetRequiredService<IWebhookUrlProtector>();
        Assert.True(protector.TryUnprotect(stored.ProtectedUrl, out var url));
        Assert.Equal(SlackUrl, url);
    }

    [Fact]
    public async Task 同じURLの二重登録はDuplicateになる()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-dup", Edit(), CancellationToken.None);
        var (result, error) = await service.CreateAsync("svc-dup", Edit("別名"), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Duplicate, result);
        Assert.Equal("同じ宛先がすでに登録されています。", error);
    }

    [Fact]
    public async Task 許可されないURLはInvalidUrlになる()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        var (result, error) = await service.CreateAsync(
            "svc-invalid", Edit(url: "https://example.com/hook"), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.InvalidUrl, result);
        Assert.Contains("hooks.slack.com", error);
    }

    [Fact]
    public async Task 上限に達するとLimitReachedになる()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ExtraSettings = new Dictionary<string, string?> { ["Notifications:MaxEndpointsPerUser"] = "1" }
        };
        using var scope = factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-limit", Edit(), CancellationToken.None);
        var (result, error) = await service.CreateAsync(
            "svc-limit",
            Edit("二つ目", "https://chat.googleapis.com/v1/spaces/AAAA/messages") with { Kind = WebhookKind.GoogleChat },
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.LimitReached, result);
        Assert.Contains("1 件までです", error);
    }

    [Fact]
    public async Task 編集でURLを空にすると元のURLを保つ()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();
        var protector = scope.ServiceProvider.GetRequiredService<IWebhookUrlProtector>();

        await service.CreateAsync("svc-keep", Edit(), CancellationToken.None);
        var created = (await service.ListAsync("svc-keep", CancellationToken.None)).Single();

        var (result, _) = await service.UpdateAsync(
            created.Id, "svc-keep", Edit("名前だけ変える", url: null), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);

        var updated = await service.GetAsync(created.Id, "svc-keep", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal("名前だけ変える", updated.Name);
        Assert.True(protector.TryUnprotect(updated.ProtectedUrl, out var url));
        Assert.Equal(SlackUrl, url);
    }

    [Fact]
    public async Task 他人の宛先は取得も削除もできない()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-owner", Edit(), CancellationToken.None);
        var created = (await service.ListAsync("svc-owner", CancellationToken.None)).Single();

        Assert.Null(await service.GetAsync(created.Id, "svc-other", CancellationToken.None));
        Assert.False(await service.DeleteAsync(created.Id, "svc-other", CancellationToken.None));
        Assert.NotNull(await service.GetAsync(created.Id, "svc-owner", CancellationToken.None));
    }

    [Fact]
    public async Task 成功を記録すると無効だった宛先が有効に戻る()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-revive", Edit(), CancellationToken.None);
        var created = (await service.ListAsync("svc-revive", CancellationToken.None)).Single();
        await service.MarkFailedAsync(created.Id, "宛先が見つかりません（HTTP 404）。", disable: true, CancellationToken.None);

        var disabled = await service.GetAsync(created.Id, "svc-revive", CancellationToken.None);
        Assert.False(disabled!.IsEnabled);
        Assert.Equal(WebhookDeliveryStatus.Failed, disabled.LastStatus);

        await service.MarkSucceededAsync(created.Id, CancellationToken.None);

        var revived = await service.GetAsync(created.Id, "svc-revive", CancellationToken.None);
        Assert.True(revived!.IsEnabled);
        Assert.Equal(WebhookDeliveryStatus.Succeeded, revived.LastStatus);
        Assert.Null(revived.LastError);
    }

    [Fact]
    public async Task 購読の絞り込みはイベントごとに変わる()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync(
            "svc-filter",
            new WebhookEndpointEdit("成功だけ", WebhookKind.Slack, SlackUrl, true, false, true),
            CancellationToken.None);
        await service.CreateAsync(
            "svc-filter",
            new WebhookEndpointEdit("失敗だけ", WebhookKind.GoogleChat,
                "https://chat.googleapis.com/v1/spaces/AAAA/messages", false, true, true),
            CancellationToken.None);
        await service.CreateAsync(
            "svc-filter",
            new WebhookEndpointEdit("無効", WebhookKind.Discord,
                "https://discord.com/api/webhooks/1/abc", true, true, false),
            CancellationToken.None);

        var onSuccess = await service.ListSubscribersAsync("svc-filter", NotificationEvent.Succeeded, CancellationToken.None);
        var onFailure = await service.ListSubscribersAsync("svc-filter", NotificationEvent.Failed, CancellationToken.None);
        var onManual = await service.ListSubscribersAsync("svc-filter", NotificationEvent.Manual, CancellationToken.None);

        Assert.Equal(["成功だけ"], onSuccess.Select(e => e.Name));
        Assert.Equal(["失敗だけ"], onFailure.Select(e => e.Name));
        Assert.Equal(["失敗だけ", "成功だけ"], onManual.Select(e => e.Name));
        Assert.True(await service.HasEnabledAsync("svc-filter", CancellationToken.None));
    }

    /// <summary>二重登録の確認を終えたあと、保存の直前に別の要求が同じ URL を入れてしまう状況を作る。</summary>
    private sealed class RacingProtector : IWebhookUrlProtector
    {
        private readonly IWebhookUrlProtector _inner;
        private readonly Action _beforeProtect;
        private bool _done;

        public RacingProtector(IWebhookUrlProtector inner, Action beforeProtect)
        {
            _inner = inner;
            _beforeProtect = beforeProtect;
        }

        public string Protect(string url)
        {
            if (!_done)
            {
                _done = true;
                _beforeProtect();
            }

            return _inner.Protect(url);
        }

        public bool TryUnprotect(string protectedUrl, out string url) => _inner.TryUnprotect(protectedUrl, out url);

        public string Fingerprint(string url) => _inner.Fingerprint(url);
    }

    [Fact]
    public async Task 確認のあとに割り込まれてもDuplicateを返す()
    {
        using var scope = _factory.CreateScope();
        using var otherScope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var otherDb = otherScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IWebhookUrlProtector>();

        var racing = new RacingProtector(protector, () =>
        {
            otherDb.WebhookEndpoints.Add(new WebhookEndpoint
            {
                OwnerId = "svc-race",
                Name = "先に入った宛先",
                Kind = WebhookKind.Slack,
                ProtectedUrl = protector.Protect(SlackUrl),
                UrlHint = WebhookUrlValidator.BuildHint(SlackUrl),
                UrlFingerprint = protector.Fingerprint(SlackUrl)
            });
            otherDb.SaveChanges();
        });

        var service = new WebhookEndpointService(
            db,
            racing,
            scope.ServiceProvider.GetRequiredService<IOptions<NotificationOptions>>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<ILogger<WebhookEndpointService>>());

        var (result, error) = await service.CreateAsync("svc-race", Edit(), CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Duplicate, result);
        Assert.Equal("同じ宛先がすでに登録されています。", error);

        var stored = await otherDb.WebhookEndpoints.AsNoTracking()
            .Where(e => e.OwnerId == "svc-race").ToListAsync();
        Assert.Single(stored);
    }

    /// <summary>一意制約とは関係のない保存の失敗（ディスク不足など）を起こす。</summary>
    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("保存できませんでした。", new IOException("ディスクがいっぱいです。"));
    }

    [Fact]
    public async Task 一意制約以外で保存に失敗したらDuplicateと言わずに投げ直す()
    {
        using var scope = _factory.CreateScope();
        var paths = scope.ServiceProvider.GetRequiredService<StoragePaths>();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={paths.DatabaseFile}")
            .AddInterceptors(new FailingSaveInterceptor())
            .Options;

        await using var db = new ApplicationDbContext(options);
        var service = new WebhookEndpointService(
            db,
            scope.ServiceProvider.GetRequiredService<IWebhookUrlProtector>(),
            scope.ServiceProvider.GetRequiredService<IOptions<NotificationOptions>>(),
            scope.ServiceProvider.GetRequiredService<TimeProvider>(),
            scope.ServiceProvider.GetRequiredService<ILogger<WebhookEndpointService>>());

        // 重複だと言い換えると、DB の障害が「登録済みです」の案内に化けて原因を追えなくなる
        await Assert.ThrowsAsync<DbUpdateException>(
            () => service.CreateAsync("svc-dbfail", Edit(name: "壊れる宛先"), CancellationToken.None));
    }

    [Fact]
    public async Task URLを入れ替えると前の送信結果を消す()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-reset", Edit(), CancellationToken.None);
        var created = (await service.ListAsync("svc-reset", CancellationToken.None)).Single();
        await service.MarkFailedAsync(created.Id, "宛先が見つかりません。", disable: true, CancellationToken.None);

        var (result, _) = await service.UpdateAsync(
            created.Id,
            "svc-reset",
            Edit("作り直した宛先", url: "https://hooks.slack.com/services/T00000000/B11111111/zZ9y"),
            CancellationToken.None);

        Assert.Equal(SaveEndpointResult.Saved, result);

        var updated = await service.GetAsync(created.Id, "svc-reset", CancellationToken.None);
        Assert.NotNull(updated);
        // 前の URL の失敗を、別の宛先の状態として見せない
        Assert.Equal(WebhookDeliveryStatus.None, updated.LastStatus);
        Assert.Null(updated.LastError);
        Assert.Null(updated.LastAttemptedAt);
    }

    [Fact]
    public async Task 名前だけ変えたときは前の送信結果を残す()
    {
        using var scope = _factory.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IWebhookEndpointService>();

        await service.CreateAsync("svc-keep-status", Edit(), CancellationToken.None);
        var created = (await service.ListAsync("svc-keep-status", CancellationToken.None)).Single();
        await service.MarkFailedAsync(created.Id, "宛先が見つかりません。", disable: false, CancellationToken.None);

        await service.UpdateAsync(
            created.Id, "svc-keep-status", Edit("名前だけ", url: null), CancellationToken.None);

        var updated = await service.GetAsync(created.Id, "svc-keep-status", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(WebhookDeliveryStatus.Failed, updated.LastStatus);
        Assert.Equal("宛先が見つかりません。", updated.LastError);
        Assert.NotNull(updated.LastAttemptedAt);
    }
}
