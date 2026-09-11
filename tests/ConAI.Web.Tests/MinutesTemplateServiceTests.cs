using ConAI.Web.Data;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class MinutesTemplateServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public MinutesTemplateServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private static (IMinutesTemplateService Templates, IServiceScope Scope) Create(ConAIWebApplicationFactory factory)
    {
        var scope = factory.CreateScope();
        return (scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>(), scope);
    }

    [Fact]
    public async Task 初回の配布で2件入り標準が既定になる()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var list = await templates.ListAsync("seed-owner", CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Contains(list, t => t.Name == "標準" && t.IsDefault);
        Assert.Contains(list, t => t.Name == "簡潔" && !t.IsDefault);
        Assert.Contains("決定事項", list.First(t => t.Name == "標準").Body);
        Assert.Contains("要約", list.First(t => t.Name == "簡潔").Body);
    }

    [Fact]
    public async Task 配布を繰り返してもテンプレートは増えない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        await templates.EnsureSeededAsync("seed-twice", CancellationToken.None);
        await templates.EnsureSeededAsync("seed-twice", CancellationToken.None);
        var list = await templates.ListAsync("seed-twice", CancellationToken.None);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task 一覧は自分のテンプレートだけを名前順に返す()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        await templates.CreateAsync("list-owner", "あ順", "## あ\n", CancellationToken.None);
        await templates.EnsureSeededAsync("list-other", CancellationToken.None);

        var list = await templates.ListAsync("list-owner", CancellationToken.None);

        // SQLite の既定の照合は符号位置順。あ(U+3042) < 標(U+6A19) < 簡(U+7C21)
        Assert.Equal(new[] { "あ順", "標準", "簡潔" }, list.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task 他人のテンプレートは取得も更新も削除もできない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var mine = await templates.ListAsync("owner-mine", CancellationToken.None);
        var id = mine[0].Id;

        Assert.Null(await templates.GetAsync(id, "owner-other", CancellationToken.None));
        Assert.Equal(
            UpdateTemplateResult.NotFound,
            await templates.UpdateAsync(id, "owner-other", "書き換え", "## 本文\n", CancellationToken.None));
        Assert.Null(await templates.DuplicateAsync(id, "owner-other", CancellationToken.None));
        Assert.False(await templates.SetDefaultAsync(id, "owner-other", CancellationToken.None));
        Assert.Equal(DeleteTemplateResult.NotFound, await templates.DeleteAsync(id, "owner-other", CancellationToken.None));
        Assert.False(await templates.OwnsAsync(id, "owner-other", CancellationToken.None));
    }

    [Fact]
    public async Task 既定のテンプレートは削除できない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var standard = await templates.GetDefaultAsync("default-delete", CancellationToken.None);

        var result = await templates.DeleteAsync(standard.Id, "default-delete", CancellationToken.None);

        Assert.Equal(DeleteTemplateResult.IsDefault, result);
    }

    [Fact]
    public async Task テンプレートを削除すると使っていた会議が既定に付け替わる()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();

        var list = await templates.ListAsync("reassign-owner", CancellationToken.None);
        var standard = list.First(t => t.Name == "標準");
        var concise = list.First(t => t.Name == "簡潔");
        var meeting = await meetings.CreateAsync(
            "reassign-owner",
            new Meeting { Title = "使っている会議", MinutesTemplateId = concise.Id },
            CancellationToken.None);

        var result = await templates.DeleteAsync(concise.Id, "reassign-owner", CancellationToken.None);

        Assert.Equal(DeleteTemplateResult.Deleted, result);
        var reloaded = await meetings.GetAsync(meeting.Id, "reassign-owner", CancellationToken.None);
        Assert.Equal(standard.Id, reloaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 既定にすると前の既定が外れる()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var list = await templates.ListAsync("set-default", CancellationToken.None);
        var concise = list.First(t => t.Name == "簡潔");

        Assert.True(await templates.SetDefaultAsync(concise.Id, "set-default", CancellationToken.None));

        var after = await templates.ListAsync("set-default", CancellationToken.None);
        Assert.Equal("簡潔", after.Single(t => t.IsDefault).Name);
    }

    [Fact]
    public async Task 上限を超える名前と本文は断る()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var longName = new string('あ', MinutesTemplateLimits.MaxNameChars + 1);
        var longBody = new string('あ', MinutesTemplateLimits.MaxBodyChars + 1);

        Assert.Null(await templates.CreateAsync("limit-owner", longName, "## 本文\n", CancellationToken.None));
        Assert.Null(await templates.CreateAsync("limit-owner", "名前", longBody, CancellationToken.None));
        Assert.Null(await templates.CreateAsync("limit-owner", "名前", "   ", CancellationToken.None));

        var mine = await templates.ListAsync("limit-owner", CancellationToken.None);
        Assert.Equal(
            UpdateTemplateResult.Invalid,
            await templates.UpdateAsync(mine[0].Id, "limit-owner", "名前", longBody, CancellationToken.None));
    }

    [Fact]
    public async Task 複製すると名前にコピーが付き既定にはならない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var standard = await templates.GetDefaultAsync("copy-owner", CancellationToken.None);

        var copy = await templates.DuplicateAsync(standard.Id, "copy-owner", CancellationToken.None);

        Assert.NotNull(copy);
        Assert.Equal("標準（コピー）", copy!.Name);
        Assert.Equal(standard.Body, copy.Body);
        Assert.False(copy.IsDefault);
    }

    [Fact]
    public async Task 上限に近い名前を複製しても文字が壊れない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        // 94 文字の「あ」＋絵文字（UTF-16 で 2 文字）＋「い」2 文字の、あわせて 98 文字。
        // 素朴に 95 文字で切ると絵文字の上位サロゲートだけが残る作り。
        var name = new string('あ', 94) + "🙂" + new string('い', 2);
        var created = await templates.CreateAsync("copy-surrogate", name, "## 本文\n", CancellationToken.None);

        var copy = await templates.DuplicateAsync(created!.Id, "copy-surrogate", CancellationToken.None);

        Assert.Equal(new string('あ', 94) + "（コピー）", copy!.Name);
        Assert.DoesNotContain(copy.Name, char.IsSurrogate);
    }

    [Fact]
    public async Task 既定が欠けていても付け替え先に削除するテンプレートを選ばない()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();

        // 既定の行が失われた状態を作る。GetDefaultAsync はこのとき名前順の先頭へ落ちるので、
        // その先頭を削除すると、付け替え先が削除するテンプレート自身になりうる。
        var list = await templates.ListAsync("no-default-owner", CancellationToken.None);
        ClearDefault(scope, "no-default-owner");
        var standard = list.First(t => t.Name == "標準");
        var concise = list.First(t => t.Name == "簡潔");
        var meeting = await meetings.CreateAsync(
            "no-default-owner",
            new Meeting { Title = "使っている会議", MinutesTemplateId = standard.Id },
            CancellationToken.None);

        var result = await templates.DeleteAsync(standard.Id, "no-default-owner", CancellationToken.None);

        Assert.Equal(DeleteTemplateResult.Deleted, result);
        var reloaded = await meetings.GetAsync(meeting.Id, "no-default-owner", CancellationToken.None);
        Assert.Equal(concise.Id, reloaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 最後の1件を削除すると会議のテンプレートは空になる()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();

        var list = await templates.ListAsync("last-one-owner", CancellationToken.None);
        ClearDefault(scope, "last-one-owner");
        var standard = list.First(t => t.Name == "標準");
        var concise = list.First(t => t.Name == "簡潔");
        var meeting = await meetings.CreateAsync(
            "last-one-owner",
            new Meeting { Title = "使っている会議", MinutesTemplateId = concise.Id },
            CancellationToken.None);

        Assert.Equal(DeleteTemplateResult.Deleted, await templates.DeleteAsync(standard.Id, "last-one-owner", CancellationToken.None));
        Assert.Equal(DeleteTemplateResult.Deleted, await templates.DeleteAsync(concise.Id, "last-one-owner", CancellationToken.None));

        // 付け替え先が無いので空にする。生成のときに種が蒔き直され、既定へ落ちる。
        var reloaded = await meetings.GetAsync(meeting.Id, "last-one-owner", CancellationToken.None);
        Assert.Null(reloaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 全部消したあとの一覧は種を蒔き直す()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var list = await templates.ListAsync("reseed-owner", CancellationToken.None);
        ClearDefault(scope, "reseed-owner");
        foreach (var template in list)
        {
            Assert.Equal(
                DeleteTemplateResult.Deleted,
                await templates.DeleteAsync(template.Id, "reseed-owner", CancellationToken.None));
        }

        // 配布済みの覚えが残っていると、1 件も無いのに種を蒔かず、画面が空のままになる
        var after = await templates.ListAsync("reseed-owner", CancellationToken.None);

        Assert.Equal(2, after.Count);
        Assert.Contains(after, t => t.Name == "標準" && t.IsDefault);
    }

    [Fact]
    public async Task 付け替え先は削除が実際に使うものと同じ()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();

        // 既定が欠けていると GetDefaultAsync は名前順の先頭（＝これから消す「標準」）へ落ちる。
        // 確認画面が別の問い合わせで名前を出していると、そこだけ実際と食い違う。
        var list = await templates.ListAsync("fallback-owner", CancellationToken.None);
        ClearDefault(scope, "fallback-owner");
        var standard = list.First(t => t.Name == "標準");
        var concise = list.First(t => t.Name == "簡潔");
        var meeting = await meetings.CreateAsync(
            "fallback-owner",
            new Meeting { Title = "使っている会議", MinutesTemplateId = standard.Id },
            CancellationToken.None);

        var fallback = await templates.FindFallbackAsync(standard.Id, "fallback-owner", CancellationToken.None);
        Assert.Equal(concise.Id, fallback!.Id);

        Assert.Equal(
            DeleteTemplateResult.Deleted,
            await templates.DeleteAsync(standard.Id, "fallback-owner", CancellationToken.None));
        var reloaded = await meetings.GetAsync(meeting.Id, "fallback-owner", CancellationToken.None);
        Assert.Equal(fallback.Id, reloaded!.MinutesTemplateId);
    }

    [Fact]
    public async Task 残りが無ければ付け替え先も無い()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var list = await templates.ListAsync("no-fallback-owner", CancellationToken.None);
        ClearDefault(scope, "no-fallback-owner");
        var standard = list.First(t => t.Name == "標準");
        var concise = list.First(t => t.Name == "簡潔");
        Assert.Equal(
            DeleteTemplateResult.Deleted,
            await templates.DeleteAsync(concise.Id, "no-fallback-owner", CancellationToken.None));

        Assert.Null(await templates.FindFallbackAsync(standard.Id, "no-fallback-owner", CancellationToken.None));
    }

    [Fact]
    public async Task 更新は見つからない場合と内容が不正な場合を区別する()
    {
        var (templates, scope) = Create(_factory);
        using var _ = scope;

        var mine = await templates.GetDefaultAsync("update-result", CancellationToken.None);

        // 画面はこの区別で分岐する。見つからないなら 404、内容が不正なら入力欄へ案内を出す。
        Assert.Equal(
            UpdateTemplateResult.NotFound,
            await templates.UpdateAsync(Guid.NewGuid(), "update-result", "名前", "## 本文\n", CancellationToken.None));
        Assert.Equal(
            UpdateTemplateResult.NotFound,
            await templates.UpdateAsync(mine.Id, "update-result-other", "名前", "## 本文\n", CancellationToken.None));
        Assert.Equal(
            UpdateTemplateResult.Invalid,
            await templates.UpdateAsync(mine.Id, "update-result", "  ", "## 本文\n", CancellationToken.None));
        Assert.Equal(
            UpdateTemplateResult.Updated,
            await templates.UpdateAsync(mine.Id, "update-result", "書き換えた名前", "## 本文\n", CancellationToken.None));
    }

    /// <summary>既定の行が失われた状態を作る。運用では起きないが、GetDefaultAsync はこの状態を想定している。</summary>
    private static void ClearDefault(IServiceScope scope, string ownerId)
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        foreach (var template in db.MinutesTemplates.Where(t => t.OwnerId == ownerId).ToList())
        {
            template.IsDefault = false;
        }

        db.SaveChanges();
        db.ChangeTracker.Clear();
    }
}
