using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace ConAI.Web.Tests;

/// <summary>テスト用の WebSocket 代替。受信フレームを事前に積み、送信内容を記録する。</summary>
public sealed class FakeWebSocket : WebSocket
{
    private sealed record Frame(WebSocketMessageType Type, byte[] Payload);

    private readonly Channel<Frame> _incoming = Channel.CreateUnbounded<Frame>();
    private readonly List<string> _sent = [];
    private readonly Lock _sentGate = new();

    private Frame? _current;
    private int _offset;
    private WebSocketState _state = WebSocketState.Open;
    private WebSocketCloseStatus? _closeStatus;
    private string? _closeStatusDescription;

    public IReadOnlyList<string> SentMessages
    {
        get
        {
            lock (_sentGate)
            {
                return _sent.ToArray();
            }
        }
    }

    public void EnqueueBinary(byte[] payload) =>
        _incoming.Writer.TryWrite(new Frame(WebSocketMessageType.Binary, payload));

    public void EnqueueClose() =>
        _incoming.Writer.TryWrite(new Frame(WebSocketMessageType.Close, []));

    // 基底クラスは get-only のため、バッキングフィールドへの記録で代える（set オーバーライドは CS0546）。
    public override WebSocketCloseStatus? CloseStatus => _closeStatus;

    public override string? CloseStatusDescription => _closeStatusDescription;

    public override WebSocketState State => _state;

    public override string? SubProtocol { get; }

    public override void Abort() => _state = WebSocketState.Aborted;

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        _closeStatus = closeStatus;
        _closeStatusDescription = statusDescription;
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override void Dispose() => _state = WebSocketState.Closed;

    public override async Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        if (_current is null)
        {
            _current = await _incoming.Reader.ReadAsync(cancellationToken);
            _offset = 0;
        }

        if (_current.Type == WebSocketMessageType.Close)
        {
            _current = null;
            _state = WebSocketState.CloseReceived;
            return new WebSocketReceiveResult(0, WebSocketMessageType.Close, true, WebSocketCloseStatus.NormalClosure, "bye");
        }

        var count = Math.Min(buffer.Count, _current.Payload.Length - _offset);
        Array.Copy(_current.Payload, _offset, buffer.Array!, buffer.Offset, count);
        _offset += count;

        var endOfMessage = _offset >= _current.Payload.Length;
        var type = _current.Type;
        if (endOfMessage)
        {
            _current = null;
        }

        return new WebSocketReceiveResult(count, type, endOfMessage);
    }

    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken)
    {
        lock (_sentGate)
        {
            _sent.Add(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
        }

        return Task.CompletedTask;
    }
}
