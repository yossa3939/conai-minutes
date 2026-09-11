using ConAI.Web.Notifications;
using Microsoft.Extensions.Time.Testing;

namespace ConAI.Web.Tests;

public class WebhookPacerTests
{
    private static readonly Guid EndpointA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid EndpointB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static (WebhookPacer Pacer, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 5, 0, 0, 0, TimeSpan.Zero));
        return (new WebhookPacer(time), time);
    }

    [Fact]
    public void 最初の送信は待たない()
    {
        var (pacer, _) = Create();

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(EndpointA));
    }

    [Fact]
    public void 続けて送ると1秒待つ()
    {
        var (pacer, _) = Create();

        pacer.Reserve(EndpointA);

        Assert.Equal(TimeSpan.FromSeconds(1), pacer.Reserve(EndpointA));
    }

    [Fact]
    public void 続けて3回送ると待ち時間が積み上がる()
    {
        var (pacer, _) = Create();

        pacer.Reserve(EndpointA);
        pacer.Reserve(EndpointA);

        Assert.Equal(TimeSpan.FromSeconds(2), pacer.Reserve(EndpointA));
    }

    [Fact]
    public void 間隔の1秒が経てば待たない()
    {
        var (pacer, time) = Create();

        pacer.Reserve(EndpointA);
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(EndpointA));
    }

    [Fact]
    public void 宛先が違えば待たない()
    {
        var (pacer, _) = Create();

        pacer.Reserve(EndpointA);

        Assert.Equal(TimeSpan.Zero, pacer.Reserve(EndpointB));
    }

    [Fact]
    public async Task 待ち時間が0以下なら即座に返る()
    {
        var delay = new NotificationDelay(new FakeTimeProvider());

        // FakeTimeProvider は自分では進まない。0 を素通しできないと、ここで止まる
        await delay.WaitAsync(TimeSpan.Zero, CancellationToken.None);
        await delay.WaitAsync(TimeSpan.FromSeconds(-1), CancellationToken.None);
    }

    [Fact]
    public async Task 待ち時間があれば時間の経過を待つ()
    {
        var time = new FakeTimeProvider();
        var delay = new NotificationDelay(time);

        var waiting = delay.WaitAsync(TimeSpan.FromSeconds(4), CancellationToken.None);
        Assert.False(waiting.IsCompleted);

        time.Advance(TimeSpan.FromSeconds(4));
        await waiting;
    }
}
