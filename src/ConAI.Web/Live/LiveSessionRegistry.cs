using ConAI.Web.Configuration;
using Microsoft.Extensions.Options;

namespace ConAI.Web.Live;

public interface ILiveSessionRegistry
{
    bool TryAcquire(string userId, Guid meetingId);

    void Release(string userId, Guid meetingId);

    bool IsActive(Guid meetingId);
}

public sealed class LiveSessionRegistry : ILiveSessionRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, string> _ownerByMeeting = [];
    private readonly Dictionary<string, int> _countByUser = new(StringComparer.Ordinal);
    private readonly LiveOptions _options;

    public LiveSessionRegistry(IOptions<LiveOptions> options) => _options = options.Value;

    public bool TryAcquire(string userId, Guid meetingId)
    {
        lock (_gate)
        {
            if (_ownerByMeeting.ContainsKey(meetingId))
            {
                return false;
            }

            _countByUser.TryGetValue(userId, out var count);
            if (count >= _options.MaxSessionsPerUser)
            {
                return false;
            }

            _ownerByMeeting[meetingId] = userId;
            _countByUser[userId] = count + 1;
            return true;
        }
    }

    public void Release(string userId, Guid meetingId)
    {
        lock (_gate)
        {
            if (!_ownerByMeeting.Remove(meetingId, out var owner))
            {
                return;
            }

            if (!_countByUser.TryGetValue(owner, out var count))
            {
                return;
            }

            if (count <= 1)
            {
                _countByUser.Remove(owner);
            }
            else
            {
                _countByUser[owner] = count - 1;
            }
        }
    }

    public bool IsActive(Guid meetingId)
    {
        lock (_gate)
        {
            return _ownerByMeeting.ContainsKey(meetingId);
        }
    }
}
