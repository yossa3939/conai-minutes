using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Infrastructure;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class GenerationWorkerTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public GenerationWorkerTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Guid> CreateQueuedAsync(string ownerId, string? transcription = "話者A: 本文。")
    {
        using var scope = _factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.CreateAsync(
            ownerId,
            new Meeting { Title = "ワーカー", Transcription = transcription ?? string.Empty },
            CancellationToken.None);

        Assert.True(await meetings.TryMarkQueuedAsync(meeting.Id, ownerId, CancellationToken.None));
        return meeting.Id;
    }

    private async Task<Meeting> WaitForAsync(Guid id, GenerationStatus expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (true)
        {
            using var scope = _factory.CreateScope();
            var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
            var meeting = await meetings.GetForGenerationAsync(id, CancellationToken.None);

            if (meeting is not null && meeting.GenerationStatus == expected)
            {
                return meeting;
            }

            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, timeout.Token);
        }
    }

    [Fact]
    public async Task キューに入れた会議は生成され成功状態になる()
    {
        // 文字起こしが無い会議なら、生成結果の文字起こしが書き込まれる（Media から起こした経路）。
        var id = await CreateQueuedAsync("worker-a", transcription: null);
        var queue = _factory.Services.GetRequiredService<IGenerationQueue>();

        await queue.EnqueueAsync(id, CancellationToken.None);

        var meeting = await WaitForAsync(id, GenerationStatus.Succeeded);
        Assert.Equal(FakeGeminiContentClient.DefaultTranscription, meeting.Transcription);
        Assert.Contains("## 決定事項", meeting.Minutes);
        Assert.Null(meeting.GenerationError);
    }

    [Fact]
    public async Task 文字起こしが既にある会議の生成は文字起こしを上書きしない()
    {
        var id = await CreateQueuedAsync("worker-c");
        var queue = _factory.Services.GetRequiredService<IGenerationQueue>();

        await queue.EnqueueAsync(id, CancellationToken.None);

        var meeting = await WaitForAsync(id, GenerationStatus.Succeeded);

        Assert.Equal("話者A: 本文。", meeting.Transcription);
        Assert.Contains("## 決定事項", meeting.Minutes);
        Assert.Null(meeting.GenerationError);
    }

    [Fact]
    public async Task 生成が例外で終わると失敗状態になる()
    {
        var fake = _factory.Services.GetRequiredService<FakeGeminiContentClient>();
        fake.Handler = (_, _) => throw new InvalidOperationException("模擬障害");

        try
        {
            var id = await CreateQueuedAsync("worker-b");
            var queue = _factory.Services.GetRequiredService<IGenerationQueue>();

            await queue.EnqueueAsync(id, CancellationToken.None);

            var meeting = await WaitForAsync(id, GenerationStatus.Failed);
            Assert.Equal(GenerationWorker.FailureMessage, meeting.GenerationError);
            Assert.DoesNotContain("模擬障害", meeting.GenerationError!);
        }
        finally
        {
            fake.Handler = null;
        }
    }

    [Fact]
    public async Task 一件の生成が失敗しても次の生成は処理される()
    {
        var fake = _factory.Services.GetRequiredService<FakeGeminiContentClient>();
        var calls = 0;
        fake.Handler = (request, _) =>
        {
            calls++;
            if (calls == 1)
            {
                throw new InvalidOperationException("模擬障害");
            }

            // 2 回目以降は FakeGeminiContentClient.GenerateAsync の既定の応答を踏襲する。
            return Task.FromResult(new GenerationResult(
                request.IncludeTranscription ? FakeGeminiContentClient.DefaultTranscription : string.Empty,
                FakeGeminiContentClient.DefaultMinutes,
                request.IncludeTranslatedTranscription ? FakeGeminiContentClient.DefaultTranslatedTranscription : null));
        };

        try
        {
            var first = await CreateQueuedAsync("worker-d");
            var second = await CreateQueuedAsync("worker-e");
            var queue = _factory.Services.GetRequiredService<IGenerationQueue>();

            await queue.EnqueueAsync(first, CancellationToken.None);
            await queue.EnqueueAsync(second, CancellationToken.None);

            var failed = await WaitForAsync(first, GenerationStatus.Failed);
            var succeeded = await WaitForAsync(second, GenerationStatus.Succeeded);

            Assert.Equal(GenerationWorker.FailureMessage, failed.GenerationError);
            Assert.Contains("## 決定事項", succeeded.Minutes);
        }
        finally
        {
            fake.Handler = null;
        }
    }
}
