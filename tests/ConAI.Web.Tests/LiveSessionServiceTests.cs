using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using ConAI.Web.Configuration;
using ConAI.Web.Data;
using ConAI.Web.Gemini;
using ConAI.Web.Live;
using ConAI.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ConAI.Web.Tests;

public class LiveSessionServiceTests : IClassFixture<ConAIWebApplicationFactory>
{
    private readonly ConAIWebApplicationFactory _factory;

    public LiveSessionServiceTests(ConAIWebApplicationFactory factory) => _factory = factory;

    private async Task<Guid> CreateAsync(string ownerId)
    {
        using var scope = _factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        var meeting = await meetings.CreateAsync(
            ownerId,
            new Meeting { Title = "Live", LiveMode = true },
            CancellationToken.None);
        return meeting.Id;
    }

    private LiveSessionService CreateService(FakeTimeProvider timeProvider, IGeminiLiveClient? client = null) => new(
        client ?? new FakeGeminiLiveClient(),
        _factory.Services.GetRequiredService<IServiceScopeFactory>(),
        Options.Create(new GeminiOptions
        {
            Provider = "Fake",
            LiveModel = "fake-live",
            LiveTranslateModel = "fake-translate"
        }),
        timeProvider,
        NullLogger<LiveSessionService>.Instance);

    private static IReadOnlyList<string> TextsOf(FakeWebSocket socket, string type) => socket.SentMessages
        .Select(message => JsonDocument.Parse(message).RootElement)
        .Where(element => element.GetProperty("type").GetString() == type)
        .Select(element => element.GetProperty("text").GetString()!)
        .ToArray();

    private async Task<Meeting> ReloadAsync(Guid id)
    {
        using var scope = _factory.CreateScope();
        var meetings = scope.ServiceProvider.GetRequiredService<IMeetingService>();
        return (await meetings.GetForGenerationAsync(id, CancellationToken.None))!;
    }

    /// <summary>偽の時計は自分では進まないので、別のループの仕事が終わるのは実時間で待つ。</summary>
    private static async Task WaitAsync(Func<bool> condition, string awaited)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (!condition())
        {
            if (timeout.IsCancellationRequested)
            {
                Assert.Fail($"{awaited} を待ちましたが、届きませんでした。");
            }

            await Task.Delay(20, CancellationToken.None);
        }
    }

    /// <summary>静穏の窓の分だけ時計を進めながら、セッションが終わるのを待つ。</summary>
    private static async Task AdvanceUntilCompletedAsync(Task run, FakeTimeProvider timeProvider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        while (!run.IsCompleted)
        {
            timeout.Token.ThrowIfCancellationRequested();
            timeProvider.Advance(LiveSessionService.StopQuietPeriod);
            await Task.Delay(20, timeout.Token);
        }

        await run;
    }

    [Fact]
    public async Task 音声を送ると文字起こしがクライアントへ返る()
    {
        var id = await CreateAsync("live-a");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.Contains("テスト文字起こし1。", TextsOf(socket, "Transcript"));
    }

    [Fact]
    public async Task 終了時に文字起こしが会議へ保存される()
    {
        var id = await CreateAsync("live-b");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);
        await AdvanceUntilCompletedAsync(run, timeProvider);

        var meeting = await ReloadAsync(id);
        Assert.Contains("テスト文字起こし1。", meeting.Transcription);
    }

    [Fact]
    public async Task 途中経過は30秒ごとに保存される()
    {
        var id = await CreateAsync("live-c");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!(await ReloadAsync(id)).Transcription.Contains("テスト文字起こし1。"))
        {
            timeout.Token.ThrowIfCancellationRequested();
            timeProvider.Advance(LiveSessionService.SaveInterval);
            await Task.Delay(50, timeout.Token);
        }

        socket.EnqueueClose();
        await AdvanceUntilCompletedAsync(run, timeProvider);
    }

    [Fact]
    public async Task 文字起こしのトークン間の空白は除いてから送信と保存をする()
    {
        var id = await CreateAsync("live-d");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);
        socket.EnqueueClose();

        var client = new ScriptedLiveClient(
            new LiveServerEvent(LiveEventKind.Transcript, "本日 の 会議 を 始め ます 。"),
            new LiveServerEvent(LiveEventKind.TurnComplete));
        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.Contains("本日の会議を始めます。", TextsOf(socket, "Transcript"));
        Assert.Contains("本日の会議を始めます。", (await ReloadAsync(id)).Transcription);
    }

    [Fact]
    public async Task 一メッセージが1MBを超えたらMessageTooBigで閉じる()
    {
        var id = await CreateAsync("live-e");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1024 * 1024 + 1024]);

        var service = CreateService(new FakeTimeProvider());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), timeout.Token);

        Assert.Equal(WebSocketCloseStatus.MessageTooBig, socket.CloseStatus);
    }

    [Fact]
    public async Task GoAwayを受けたら新しいセッションへ切り替えて文字起こしが続く()
    {
        var id = await CreateAsync("live-f");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);

        LiveServerEvent[] first = [new LiveServerEvent(LiveEventKind.Transcript, "切り替え前。"), new LiveServerEvent(LiveEventKind.GoAway)];
        LiveServerEvent[] second = [new LiveServerEvent(LiveEventKind.Transcript, "切り替え後。"), new LiveServerEvent(LiveEventKind.TurnComplete)];
        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, new ScriptedLiveClient(first, second));

        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        await WaitAsync(() => TextsOf(socket, "Transcript").Contains("切り替え後。"), "切り替え後の文字起こし");

        socket.EnqueueClose();
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.Contains("切り替え前。", TextsOf(socket, "Transcript"));
        Assert.Contains("接続を張り直しています。", TextsOf(socket, "Info"));
    }

    [Fact]
    public async Task GoAwayでの切り替えは旧セッションを破棄する()
    {
        var id = await CreateAsync("live-g");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);

        LiveServerEvent[] first = [new LiveServerEvent(LiveEventKind.Transcript, "切り替え前。"), new LiveServerEvent(LiveEventKind.GoAway)];
        LiveServerEvent[] second = [new LiveServerEvent(LiveEventKind.Transcript, "切り替え後。"), new LiveServerEvent(LiveEventKind.TurnComplete)];
        var client = new ScriptedLiveClient(first, second);
        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);

        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        await WaitAsync(() => TextsOf(socket, "Transcript").Contains("切り替え後。"), "切り替え後の文字起こし");

        socket.EnqueueClose();
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.True(client.Sessions[0].Disposed);
    }

    [Fact]
    public async Task GoAwayでの切り替えは再試行枠を消費しない()
    {
        var id = await CreateAsync("live-h");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);

        LiveServerEvent[] first = [new LiveServerEvent(LiveEventKind.Transcript, "一。"), new LiveServerEvent(LiveEventKind.GoAway)];
        LiveServerEvent[] second = [new LiveServerEvent(LiveEventKind.Transcript, "二。"), new LiveServerEvent(LiveEventKind.TurnComplete)];
        LiveServerEvent[] third = [new LiveServerEvent(LiveEventKind.Transcript, "三。"), new LiveServerEvent(LiveEventKind.TurnComplete)];
        var client = new ScriptedLiveClient(first, second, third);
        var service = CreateService(new FakeTimeProvider(), client);

        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!TextsOf(socket, "Error").Contains("接続が切れました。もう一度録音を開始してください。"))
        {
            timeout.Token.ThrowIfCancellationRequested();
            await Task.Delay(50, timeout.Token);
        }

        await run;

        Assert.Equal(3, client.ConnectCount);
    }

    [Fact]
    public async Task 翻訳モードは翻訳用モデルと翻訳先言語で接続する()
    {
        var id = await CreateAsync("live-i");
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[32000]);
        socket.EnqueueClose();

        var client = new ScriptedLiveClient(
            new LiveServerEvent(LiveEventKind.Transcript, "原文。"),
            new LiveServerEvent(LiveEventKind.Translation, "Original."),
            new LiveServerEvent(LiveEventKind.TurnComplete));
        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: true, TargetLanguage: "en"), CancellationToken.None);
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.Equal("fake-translate", client.Connections[0].Model);
        Assert.True(client.Connections[0].Translate);
        Assert.Equal("en", client.Connections[0].TargetLanguage);
        Assert.Contains("Original.", TextsOf(socket, "Translation"));
        Assert.Contains("Original.", (await ReloadAsync(id)).TranslatedTranscription);
    }

    [Fact]
    public async Task 停止時に音声の終了を伝え_その後に届いた文字起こしを保存する()
    {
        var id = await CreateAsync("live-stop-1");
        var client = new ScriptedLiveClient(Array.Empty<LiveServerEvent>())
        {
            // 録音中の台本は空。停止時のドレイン台本にだけ発話と TurnComplete を置く。
            DrainScript =
            [
                new LiveServerEvent(LiveEventKind.Transcript, "最後の発話。"),
                new LiveServerEvent(LiveEventKind.TurnComplete)
            ]
        };
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        // 停止後の発話を受け取ってから、静穏の窓の分だけ時計を進めて終わらせる。
        await WaitAsync(() => TextsOf(socket, "Transcript").Contains("最後の発話。"), "停止後の文字起こし");
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.True(client.Sessions.Single().EndAudioCalled);
        Assert.Contains("最後の発話。", TextsOf(socket, "Transcript"));
        Assert.Contains("最後の発話。", (await ReloadAsync(id)).Transcription);
    }

    [Fact]
    public async Task 停止時の待ちはTurnCompleteが届いただけでは終わらない()
    {
        var id = await CreateAsync("live-stop-4");
        var client = new ScriptedLiveClient(Array.Empty<LiveServerEvent>())
        {
            DrainScript =
            [
                new LiveServerEvent(LiveEventKind.Transcript, "最後の発話。"),
                new LiveServerEvent(LiveEventKind.TurnComplete)
            ]
        };
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        await WaitAsync(() => TextsOf(socket, "TurnComplete").Count == 1, "TurnComplete");

        // Gemini は発話の切れ目ごとに TurnComplete を返すので、届いても続きが無いとは限らない。
        // 静穏の窓は時計を進めない限り明けないため、ここでセッションは終わっていない。
        await Task.Delay(200);
        Assert.False(run.IsCompleted);

        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.Contains("最後の発話。", (await ReloadAsync(id)).Transcription);
    }

    [Fact]
    public async Task 停止時の待ちはTurnCompleteが来なくても届いた分を保存して終える()
    {
        var id = await CreateAsync("live-stop-2");
        var client = new ScriptedLiveClient(Array.Empty<LiveServerEvent>())
        {
            // TurnComplete を返さない。受信が途切れた時点で待ちは終わる。
            DrainScript = [new LiveServerEvent(LiveEventKind.Transcript, "途中の発話。")]
        };
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        await WaitAsync(() => TextsOf(socket, "Transcript").Contains("途中の発話。"), "停止後の文字起こし");
        await AdvanceUntilCompletedAsync(run, timeProvider);

        Assert.True(client.Sessions.Single().EndAudioCalled);
        Assert.Contains("途中の発話。", (await ReloadAsync(id)).Transcription);
        Assert.True(client.Sessions.Single().Disposed);
    }

    [Fact]
    public async Task 停止時の待ちは発話が途切れなくても上限で打ち切る()
    {
        var id = await CreateAsync("live-stop-5");
        var client = new ScriptedLiveClient(Array.Empty<LiveServerEvent>());
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1000]);
        socket.EnqueueClose();

        var timeProvider = new FakeTimeProvider();
        var service = CreateService(timeProvider, client);
        var run = service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None);

        await WaitAsync(() => client.Sessions.Count == 1 && client.Sessions[0].EndAudioCalled, "EndAudioAsync");

        var session = client.Sessions.Single();
        var started = timeProvider.GetUtcNow();
        var delivered = 0;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!run.IsCompleted)
        {
            timeout.Token.ThrowIfCancellationRequested();

            // 静穏の窓が明ける前に次の発話を届ける。上限が無ければ、この待ちは終わらない。
            session.Push(new LiveServerEvent(LiveEventKind.Transcript, "まだ続く。"));
            delivered++;
            await WaitAsync(() => TextsOf(socket, "Transcript").Count >= delivered, "続きの文字起こし");

            timeProvider.Advance(LiveSessionService.StopQuietPeriod);
            await Task.Delay(50, timeout.Token);
        }

        await run;

        Assert.True(
            timeProvider.GetUtcNow() - started >= LiveSessionService.StopDrainTimeout,
            "発話が途切れないのに、上限より早く待ちが終わっている。");
    }

    [Fact]
    public async Task 停止時に音声の終了の通知が失敗しても保存と切断は行う()
    {
        var id = await CreateAsync("live-stop-3");
        var client = new ScriptedLiveClient(
            new LiveServerEvent(LiveEventKind.Transcript, "先の発話。"),
            new LiveServerEvent(LiveEventKind.TurnComplete))
        {
            EndAudioFailure = new InvalidOperationException("boom")
        };
        var socket = new FakeWebSocket();
        socket.EnqueueBinary(new byte[1000]);
        socket.EnqueueClose();

        var service = CreateService(new FakeTimeProvider(), client);
        await service.RunAsync(socket, new LiveSessionContext(id, Translate: false, TargetLanguage: "ja"), CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Contains("先の発話。", (await ReloadAsync(id)).Transcription);
        Assert.True(client.Sessions.Single().Disposed);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
    }

    /// <summary>
    /// 台本に沿ってイベントを返す Live クライアント。台本 1 つの場合は音声を受け取ったら流し、
    /// セッションごとの台本を渡した場合は接続した時点で流し切る（GoAway 後のセッションには音声が来ないため）。
    /// </summary>
    internal sealed class ScriptedLiveClient : IGeminiLiveClient
    {
        private readonly LiveServerEvent[][] _scripts;
        private readonly bool _scriptOnConnect;
        private readonly Lock _gate = new();
        private readonly List<LiveSessionSettings> _connections = [];
        private readonly List<ScriptedSession> _sessions = [];

        /// <summary>1 つの台本を、接続のたびに同じだけ返す。</summary>
        public ScriptedLiveClient(params LiveServerEvent[] events)
            : this([events], scriptOnConnect: false)
        {
        }

        /// <summary>セッションごとに別の台本を返す。台本が尽きたら最後の台本を使い回す。</summary>
        public ScriptedLiveClient(params LiveServerEvent[][] scripts)
            : this(scripts, scriptOnConnect: true)
        {
        }

        private ScriptedLiveClient(LiveServerEvent[][] scripts, bool scriptOnConnect)
        {
            _scripts = scripts;
            _scriptOnConnect = scriptOnConnect;
        }

        public int ConnectCount
        {
            get
            {
                lock (_gate)
                {
                    return _sessions.Count;
                }
            }
        }

        public IReadOnlyList<LiveSessionSettings> Connections
        {
            get
            {
                lock (_gate)
                {
                    return _connections.ToArray();
                }
            }
        }

        public IReadOnlyList<ScriptedSession> Sessions
        {
            get
            {
                lock (_gate)
                {
                    return _sessions.ToArray();
                }
            }
        }

        /// <summary>EndAudioAsync（音声の終了の通知）が呼ばれたときに作ったセッションへ流す台本。</summary>
        public LiveServerEvent[] DrainScript { get; set; } = [];

        /// <summary>EndAudioAsync で投げさせる例外（音声の終了の通知が失敗する場合の再現）。null なら失敗させない。</summary>
        public Exception? EndAudioFailure { get; set; }

        public Task<IGeminiLiveSession> ConnectAsync(
            LiveSessionSettings settings,
            string? resumptionHandle,
            CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                var index = Math.Min(_sessions.Count, _scripts.Length - 1);
                var session = new ScriptedSession(_scripts[index], _scriptOnConnect, DrainScript, EndAudioFailure);
                _connections.Add(settings);
                _sessions.Add(session);
                return Task.FromResult<IGeminiLiveSession>(session);
            }
        }

        internal sealed class ScriptedSession : IGeminiLiveSession
        {
            private readonly LiveServerEvent[] _script;
            private readonly LiveServerEvent[] _drainScript;
            private readonly Exception? _endAudioFailure;
            private readonly Channel<LiveServerEvent> _events = Channel.CreateUnbounded<LiveServerEvent>();
            private int _started;

            public bool Disposed { get; private set; }

            public bool EndAudioCalled { get; private set; }

            public ScriptedSession(LiveServerEvent[] script, bool scriptOnConnect, LiveServerEvent[] drainScript, Exception? endAudioFailure)
            {
                _script = script;
                _drainScript = drainScript;
                _endAudioFailure = endAudioFailure;

                if (scriptOnConnect)
                {
                    // 流し切ったらチャネルを閉じて ReceiveAsync が完了するようにする。
                    _started = 1;
                    Play();
                    _events.Writer.TryComplete();
                }
            }

            /// <summary>台本とは別に、テストが選んだ時点でイベントを 1 つ流す。</summary>
            public void Push(LiveServerEvent serverEvent) => _events.Writer.TryWrite(serverEvent);

            public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
            {
                if (Interlocked.Exchange(ref _started, 1) == 0)
                {
                    Play();
                }

                return Task.CompletedTask;
            }

            public Task EndAudioAsync(CancellationToken cancellationToken)
            {
                EndAudioCalled = true;

                if (_endAudioFailure is not null)
                {
                    throw _endAudioFailure;
                }

                foreach (var serverEvent in _drainScript)
                {
                    // 台本を使い切ってチャネルが閉じられていても、ここでは失敗を無視する。
                    _events.Writer.TryWrite(serverEvent);
                }

                return Task.CompletedTask;
            }

            private void Play()
            {
                foreach (var serverEvent in _script)
                {
                    _events.Writer.TryWrite(serverEvent);
                }
            }

            public async IAsyncEnumerable<LiveServerEvent> ReceiveAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken)
            {
                await foreach (var serverEvent in _events.Reader.ReadAllAsync(cancellationToken))
                {
                    yield return serverEvent;
                }
            }

            public ValueTask DisposeAsync()
            {
                Disposed = true;
                _events.Writer.TryComplete();
                return ValueTask.CompletedTask;
            }
        }
    }
}
