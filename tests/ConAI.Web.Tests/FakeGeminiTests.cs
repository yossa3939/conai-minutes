using ConAI.Web.Gemini;
using Microsoft.Extensions.DependencyInjection;

namespace ConAI.Web.Tests;

public class FakeGeminiTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public FakeGeminiTests(ConAIWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public void Fakeプロバイダではfake実装が解決される()
    {
        using var scope = _factory.CreateScope();

        Assert.IsType<FakeGeminiLiveClient>(scope.ServiceProvider.GetRequiredService<IGeminiLiveClient>());
        Assert.IsType<FakeGeminiContentClient>(scope.ServiceProvider.GetRequiredService<IGeminiContentClient>());
    }

    [Fact]
    public async Task 音声を送ると文字起こしイベントが返る()
    {
        var client = new FakeGeminiLiveClient();
        await using var session = await client.ConnectAsync(
            new LiveSessionSettings("fake-model", Translate: false, TargetLanguage: "ja"),
            resumptionHandle: null,
            CancellationToken.None);

        // 1 発話 = 16kHz・16bit モノラルの 1 秒ぶん（32000 バイト）
        await session.SendAudioAsync(new byte[32000], CancellationToken.None);

        var kinds = new List<LiveEventKind>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var serverEvent in session.ReceiveAsync(timeout.Token))
        {
            kinds.Add(serverEvent.Kind);
            if (serverEvent.Kind == LiveEventKind.TurnComplete)
            {
                break;
            }
        }

        Assert.Contains(LiveEventKind.Transcript, kinds);
        Assert.DoesNotContain(LiveEventKind.Translation, kinds);
    }

    [Fact]
    public async Task 翻訳有効なら翻訳イベントも返る()
    {
        var client = new FakeGeminiLiveClient();
        await using var session = await client.ConnectAsync(
            new LiveSessionSettings("fake-model", Translate: true, TargetLanguage: "en"),
            resumptionHandle: null,
            CancellationToken.None);

        // 1 発話 = 16kHz・16bit モノラルの 1 秒ぶん（32000 バイト）
        await session.SendAudioAsync(new byte[32000], CancellationToken.None);

        var kinds = new List<LiveEventKind>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var serverEvent in session.ReceiveAsync(timeout.Token))
        {
            kinds.Add(serverEvent.Kind);
            if (serverEvent.Kind == LiveEventKind.TurnComplete)
            {
                break;
            }
        }

        Assert.Contains(LiveEventKind.Translation, kinds);
    }

    [Fact]
    public async Task 送信終了で端数の音声が文字起こしになる()
    {
        var client = new FakeGeminiLiveClient();
        await using var session = await client.ConnectAsync(
            new LiveSessionSettings("fake-model", Translate: false, TargetLanguage: "ja"),
            resumptionHandle: null,
            CancellationToken.None);

        // 1 発話に満たない半分（16000 バイト）を送り、送信終了の合図で文字起こしになることを確かめる。
        await session.SendAudioAsync(new byte[16000], CancellationToken.None);
        await session.EndAudioAsync(CancellationToken.None);

        var events = new List<LiveServerEvent>();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await foreach (var serverEvent in session.ReceiveAsync(timeout.Token))
        {
            events.Add(serverEvent);
            if (serverEvent.Kind == LiveEventKind.TurnComplete)
            {
                break;
            }
        }

        Assert.Contains(events, serverEvent => serverEvent is { Kind: LiveEventKind.Transcript, Text: "テスト文字起こし1。" });
        Assert.Equal(LiveEventKind.TurnComplete, events[^1].Kind);
    }

    [Fact]
    public async Task Fake生成は見出し付きの議事録を返す()
    {
        var client = new FakeGeminiContentClient();

        var result = await client.GenerateAsync(
            new GenerationRequest(
                "fake-model",
                "system",
                "prompt",
                [],
                IncludeTranscription: true,
                IncludeTranslatedTranscription: false),
            CancellationToken.None);

        Assert.Contains("## 決定事項", result.Minutes);
        Assert.False(string.IsNullOrWhiteSpace(result.Transcription));
        Assert.Null(result.TranslatedTranscription);
    }

    [Fact]
    public async Task 既定の答えには送った質問が入る()
    {
        var client = new FakeGeminiContentClient();

        var result = await client.AskAsync(
            new ChatRequest("fake-model", "system", "<<<QUESTION\n決定事項は何ですか\nQUESTION>>>"),
            CancellationToken.None);

        Assert.Contains("決定事項は何ですか", result.Answer);
        Assert.Equal(1, client.AskCallCount);
    }

    [Fact]
    public async Task AskHandler_を差し替えると答えを置き換えられる()
    {
        var client = new FakeGeminiContentClient
        {
            AskHandler = (_, _) => Task.FromResult(new ChatResult("差し替えた答え", []))
        };

        var result = await client.AskAsync(new ChatRequest("m", "s", "p"), CancellationToken.None);

        Assert.Equal("差し替えた答え", result.Answer);
        Assert.Equal("p", client.LastChatRequest?.Prompt);
    }

    [Fact]
    public async Task 議事録生成の呼び出し回数とチャットの呼び出し回数は別に数える()
    {
        var client = new FakeGeminiContentClient();

        await client.AskAsync(new ChatRequest("m", "s", "p"), CancellationToken.None);

        Assert.Equal(1, client.AskCallCount);
        Assert.Equal(0, client.CallCount);
        Assert.Null(client.LastRequest);
    }

    [Fact]
    public async Task 既定の選抜は質問に会議名が含まれる連番を返す()
    {
        var client = new FakeGeminiContentClient();
        var prompt = """
            <<<DIGESTS
            1. 予算会議（2026-08-12 14:00）
               ## 来期予算の配分
               営業部からの増額要求を承認した。

            2. 部門定例（2026-08-13 10:00）
               ## 進捗
               来週まで様子を見る。
            DIGESTS>>>

            <<<QUESTION
            予算会議 で何が決まりましたか
            QUESTION>>>
            """;

        var result = await client.SelectAsync(
            new SelectionRequest("fake-select", "system", prompt), CancellationToken.None);

        Assert.Equal(new[] { 1 }, result.MeetingNumbers);
        Assert.Equal(1, client.SelectCallCount);
        Assert.Equal(prompt, client.LastSelectionRequest?.Prompt);
    }

    [Fact]
    public async Task 会議名が質問に無ければ選抜は空を返す()
    {
        var client = new FakeGeminiContentClient();
        var prompt = """
            <<<DIGESTS
            1. 予算会議（2026-08-12 14:00）
               ## 来期予算の配分
               営業部からの増額要求を承認した。
            DIGESTS>>>

            <<<QUESTION
            昼食の話はどうなりましたか
            QUESTION>>>
            """;

        var result = await client.SelectAsync(
            new SelectionRequest("fake-select", "system", prompt), CancellationToken.None);

        Assert.Empty(result.MeetingNumbers);
    }

    [Fact]
    public async Task SelectHandler_を差し替えると選抜結果を置き換えられる()
    {
        var client = new FakeGeminiContentClient
        {
            SelectHandler = (_, _) => Task.FromResult(new SelectionResult([3, 5]))
        };

        var result = await client.SelectAsync(
            new SelectionRequest("fake-select", "system", "本文"), CancellationToken.None);

        Assert.Equal(new[] { 3, 5 }, result.MeetingNumbers);
    }

    [Fact]
    public async Task 既定の回答は渡された議事録の番号を全件返す()
    {
        var client = new FakeGeminiContentClient();
        var prompt = """
            <<<MINUTES
            ## 議事録 1: 予算会議（2026年8月12日 14:00）

            ## 決定事項
            - 増額要求を承認した

            ## 議事録 2: 部門定例（2026年8月13日 10:00）

            ## 決定事項
            - 来週まで様子を見る
            MINUTES>>>

            <<<QUESTION
            何が決まりましたか
            QUESTION>>>
            """;

        var result = await client.AskAsync(
            new ChatRequest("fake-generate", "system", prompt), CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, result.UsedMeetingNumbers);
        Assert.Contains("何が決まりましたか", result.Answer);
    }
}
