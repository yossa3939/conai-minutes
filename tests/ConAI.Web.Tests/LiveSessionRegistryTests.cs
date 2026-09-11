using ConAI.Web.Configuration;
using ConAI.Web.Live;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Tests;

public class LiveSessionRegistryTests
{
    private static ILiveSessionRegistry Create(int maxSessionsPerUser = 1) =>
        new LiveSessionRegistry(Options.Create(new LiveOptions { MaxSessionsPerUser = maxSessionsPerUser }));

    [Fact]
    public void 最初の確保は成功する()
    {
        var registry = Create();
        var meetingId = Guid.NewGuid();

        Assert.True(registry.TryAcquire("user-1", meetingId));
        Assert.True(registry.IsActive(meetingId));
    }

    [Fact]
    public void 同じ会議は二重に確保できない()
    {
        var registry = Create(maxSessionsPerUser: 5);
        var meetingId = Guid.NewGuid();

        Assert.True(registry.TryAcquire("user-1", meetingId));
        Assert.False(registry.TryAcquire("user-2", meetingId));
    }

    [Fact]
    public void 利用者あたりの上限を超えると確保できない()
    {
        var registry = Create(maxSessionsPerUser: 1);

        Assert.True(registry.TryAcquire("user-1", Guid.NewGuid()));
        Assert.False(registry.TryAcquire("user-1", Guid.NewGuid()));
    }

    [Fact]
    public void 解放すれば再び確保できる()
    {
        var registry = Create();
        var first = Guid.NewGuid();

        Assert.True(registry.TryAcquire("user-1", first));
        registry.Release("user-1", first);

        Assert.False(registry.IsActive(first));
        Assert.True(registry.TryAcquire("user-1", Guid.NewGuid()));
    }
}
