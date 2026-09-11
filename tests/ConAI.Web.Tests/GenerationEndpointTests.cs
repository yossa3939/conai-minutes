using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ConAI.Web.Data;
using ConAI.Web.Endpoints;
using ConAI.Web.Gemini;
using ConAI.Web.Live;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class GenerationEndpointTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public GenerationEndpointTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Guid> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.CreateAsync(
            ownerId,
            new Meeting { Title = "API", Transcription = "話者A: 本文。" },
            CancellationToken.None);
        return meeting.Id;
    }

    [Fact]
    public async Task 生成を依頼すると202が返る()
    {
        var id = await CreateAsync("api-a");
        var client = await _factory.CreateClientAs("api-a").WithCsrfTokenAsync();

        var response = await client.PostAsync($"/api/meetings/{id}/generation", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    public async Task 実行中に再依頼すると409が返る()
    {
        var fake = _factory.Services.GetRequiredService<FakeGeminiContentClient>();
        var gate = new TaskCompletionSource();
        fake.Handler = async (request, _) =>
        {
            await gate.Task;
            return new GenerationResult("本文", FakeGeminiContentClient.DefaultMinutes, null);
        };

        try
        {
            var id = await CreateAsync("api-b");
            var client = await _factory.CreateClientAs("api-b").WithCsrfTokenAsync();

            var first = await client.PostAsync($"/api/meetings/{id}/generation", null);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);

            var second = await client.PostAsync($"/api/meetings/{id}/generation", null);
            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }
        finally
        {
            gate.SetResult();
            fake.Handler = null;
        }
    }

    [Fact]
    public async Task 他人の会議は生成できない()
    {
        var id = await CreateAsync("api-c");
        var client = await _factory.CreateClientAs("api-d").WithCsrfTokenAsync();

        var response = await client.PostAsync($"/api/meetings/{id}/generation", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task 録音中の会議は生成を409で拒む()
    {
        var id = await CreateAsync("api-live");
        var client = await _factory.CreateClientAs("api-live").WithCsrfTokenAsync();

        var registry = _factory.Services.GetRequiredService<ILiveSessionRegistry>();
        Assert.True(registry.TryAcquire("api-live", id));
        try
        {
            var response = await client.PostAsync($"/api/meetings/{id}/generation", null);

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            registry.Release("api-live", id);
        }
    }

    [Fact]
    public async Task 状況取得は状態文字列を返す()
    {
        var id = await CreateAsync("api-e");
        var client = _factory.CreateClientAs("api-e");

        var status = await client.GetFromJsonAsync<GenerationStatusDto>($"/api/meetings/{id}/generation");

        Assert.Equal(nameof(GenerationStatus.None), status!.Status);
    }

    [Fact]
    public async Task 生成状況は文字起こしと議事録も返す()
    {
        Guid id;
        using (var scope = _factory.CreateScope())
        {
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var created = await meetings.CreateAsync(
                "api-status",
                new Meeting
                {
                    Title = "状況",
                    Transcription = "話者A: 本文。",
                    TranslatedTranscription = "Speaker A.",
                    Minutes = "# 議事録"
                },
                CancellationToken.None);
            id = created.Id;
        }

        var client = _factory.CreateClientAs("api-status");
        using var response = await client.GetAsync($"/api/meetings/{id}/generation");
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(nameof(GenerationStatus.None), payload.GetProperty("status").GetString());
        Assert.Equal(string.Empty, payload.GetProperty("error").GetString());
        Assert.Equal("話者A: 本文。", payload.GetProperty("transcription").GetString());
        Assert.Equal("Speaker A.", payload.GetProperty("translatedTranscription").GetString());
        Assert.Equal("# 議事録", payload.GetProperty("minutes").GetString());
    }

    [Fact]
    public async Task 本文で指定したテンプレートが会議に保存されてから待機になる()
    {
        var id = await CreateAsync("api-template");
        Guid templateId;
        using (var scope = _factory.CreateScope())
        {
            var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            var list = await templates.ListAsync("api-template", CancellationToken.None);
            templateId = list.First(t => t.Name == "簡潔").Id;
        }

        var client = await _factory.CreateClientAs("api-template").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{id}/generation",
            new { minutesTemplateId = templateId });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var check = _factory.CreateScope();
        var meetings = check.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.GetAsync(id, "api-template", CancellationToken.None);
        Assert.Equal(templateId, meeting!.MinutesTemplateId);
    }

    [Fact]
    public async Task 他人のテンプレートを指定すると400になり待機にもしない()
    {
        var id = await CreateAsync("api-template-b");
        Guid otherId;
        using (var scope = _factory.CreateScope())
        {
            var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            var list = await templates.ListAsync("api-template-c", CancellationToken.None);
            otherId = list[0].Id;
        }

        var client = await _factory.CreateClientAs("api-template-b").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{id}/generation",
            new { minutesTemplateId = otherId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("選べないテンプレートです。", payload.GetProperty("message").GetString());

        using var check = _factory.CreateScope();
        var meetings = check.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.GetAsync(id, "api-template-b", CancellationToken.None);
        Assert.Null(meeting!.MinutesTemplateId);
        Assert.Equal(GenerationStatus.None, meeting.GenerationStatus);
    }

    [Fact]
    public async Task 録音中に拒んだ要求はテンプレートを書き換えない()
    {
        var id = await CreateAsync("api-template-live");
        Guid templateId;
        using (var scope = _factory.CreateScope())
        {
            var templates = scope.ServiceProvider.GetRequiredService<IMinutesTemplateService>();
            var list = await templates.ListAsync("api-template-live", CancellationToken.None);
            templateId = list.First(t => t.Name == "簡潔").Id;
        }

        var client = await _factory.CreateClientAs("api-template-live").WithCsrfTokenAsync();
        var registry = _factory.Services.GetRequiredService<ILiveSessionRegistry>();
        Assert.True(registry.TryAcquire("api-template-live", id));
        try
        {
            var response = await client.PostAsJsonAsync(
                $"/api/meetings/{id}/generation",
                new { minutesTemplateId = templateId });

            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        }
        finally
        {
            registry.Release("api-template-live", id);
        }

        // 断った要求が会議を書き換えていては、利用者に見えないところでテンプレートが差し替わる
        using var check = _factory.CreateScope();
        var meetings = check.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.GetAsync(id, "api-template-live", CancellationToken.None);
        Assert.Null(meeting!.MinutesTemplateId);
    }

    [Fact]
    public async Task 検査の後に消えたテンプレートは400で断り待機のまま残さない()
    {
        using var factory = new ConAIWebApplicationFactory
        {
            ConfigureServices = services => services.AddScoped<IMinutesTemplateService>(provider =>
                new VanishingTemplates(ActivatorUtilities.CreateInstance<MinutesTemplateService>(provider)))
        };

        Guid id;
        Guid templateId;
        using (var scope = factory.CreateScope())
        {
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await meetings.CreateAsync(
                "api-vanish",
                new Meeting { Title = "消える", Transcription = "話者A: 本文。" },
                CancellationToken.None);
            id = meeting.Id;

            // 差し替えたサービスは 1 回目の OwnsAsync しか通さないので、種は素の登録で蒔いておく
            var templates = ActivatorUtilities.CreateInstance<MinutesTemplateService>(scope.ServiceProvider);
            var list = await templates.ListAsync("api-vanish", CancellationToken.None);
            templateId = list.First(t => t.Name == "簡潔").Id;
        }

        var client = await factory.CreateClientAs("api-vanish").WithCsrfTokenAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/meetings/{id}/generation",
            new { minutesTemplateId = templateId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // 待機の印が残ると、この会議は生成も削除もできなくなる
        using (var check = factory.CreateScope())
        {
            var meetings = check.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await meetings.GetAsync(id, "api-vanish", CancellationToken.None);
            Assert.NotEqual(GenerationStatus.Queued, meeting!.GenerationStatus);
            Assert.NotEqual(GenerationStatus.Running, meeting.GenerationStatus);
        }

        // 印が解けているので、テンプレートを指さない依頼はそのまま通る
        var retry = await client.PostAsync($"/api/meetings/{id}/generation", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    }

    /// <summary>1 回目の OwnsAsync だけ通す。入口の検査を抜けたあとにテンプレートが消えた状況を作る。</summary>
    private sealed class VanishingTemplates : IMinutesTemplateService
    {
        private readonly IMinutesTemplateService _inner;
        private int _owns;

        public VanishingTemplates(IMinutesTemplateService inner) => _inner = inner;

        public Task<bool> OwnsAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _owns) == 1
                ? _inner.OwnsAsync(id, ownerId, cancellationToken)
                : Task.FromResult(false);

        public Task EnsureSeededAsync(string ownerId, CancellationToken cancellationToken) =>
            _inner.EnsureSeededAsync(ownerId, cancellationToken);

        public Task<IReadOnlyList<MinutesTemplate>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
            _inner.ListAsync(ownerId, cancellationToken);

        public Task<MinutesTemplate?> GetAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            _inner.GetAsync(id, ownerId, cancellationToken);

        public Task<MinutesTemplate> GetDefaultAsync(string ownerId, CancellationToken cancellationToken) =>
            _inner.GetDefaultAsync(ownerId, cancellationToken);

        public Task<MinutesTemplate?> CreateAsync(string ownerId, string name, string body, CancellationToken cancellationToken) =>
            _inner.CreateAsync(ownerId, name, body, cancellationToken);

        public Task<UpdateTemplateResult> UpdateAsync(Guid id, string ownerId, string name, string body, CancellationToken cancellationToken) =>
            _inner.UpdateAsync(id, ownerId, name, body, cancellationToken);

        public Task<MinutesTemplate?> DuplicateAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            _inner.DuplicateAsync(id, ownerId, cancellationToken);

        public Task<bool> SetDefaultAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            _inner.SetDefaultAsync(id, ownerId, cancellationToken);

        public Task<DeleteTemplateResult> DeleteAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            _inner.DeleteAsync(id, ownerId, cancellationToken);

        public Task<int> CountMeetingsAsync(Guid id, string ownerId, CancellationToken cancellationToken) =>
            _inner.CountMeetingsAsync(id, ownerId, cancellationToken);

        public Task<MinutesTemplate?> FindFallbackAsync(Guid excludedId, string ownerId, CancellationToken cancellationToken) =>
            _inner.FindFallbackAsync(excludedId, ownerId, cancellationToken);

        public Task<string> ResolveForGenerationAsync(Meeting meeting, CancellationToken cancellationToken) =>
            _inner.ResolveForGenerationAsync(meeting, cancellationToken);
    }

    [Fact]
    public async Task 壊れたJSONの本文は400になる()
    {
        var id = await CreateAsync("api-template-d");
        var client = await _factory.CreateClientAs("api-template-d").WithCsrfTokenAsync();

        using var content = new StringContent("{", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/meetings/{id}/generation", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
