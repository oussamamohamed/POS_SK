using System;
using System.Collections.Concurrent;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Security;

public sealed class PinRateLimiterService : IPinRateLimiterService
{
    private static readonly TimeSpan WindowDuration = TimeSpan.FromHours(1);

    // Escalade : 5 échecs -> 30 s, 10 -> 5 min, 20 -> 30 min (fenêtre d'une heure).
    private static readonly (int Failures, TimeSpan Lockout)[] Tiers =
    [
        (20, TimeSpan.FromMinutes(30)),
        (10, TimeSpan.FromMinutes(5)),
        (5, TimeSpan.FromSeconds(30)),
    ];

    private readonly TimeProvider _clock;

    // shortcut: compteurs perdus au redémarrage, persister si le serveur est exposé hors LAN
    private readonly ConcurrentDictionary<string, ClientAttemptTracker> _trackers = new();

    public PinRateLimiterService(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    public bool IsLocked(string clientIdentifier)
    {
        if (string.IsNullOrWhiteSpace(clientIdentifier))
        {
            return false;
        }

        if (_trackers.TryGetValue(clientIdentifier, out var tracker))
        {
            var now = _clock.GetUtcNow();
            if (tracker.LockedUntilUtc.HasValue && tracker.LockedUntilUtc.Value > now)
            {
                return true;
            }
        }

        return false;
    }

    public void RecordFailedAttempt(string clientIdentifier)
    {
        if (string.IsNullOrWhiteSpace(clientIdentifier))
        {
            return;
        }

        var now = _clock.GetUtcNow();
        _trackers.AddOrUpdate(
            clientIdentifier,
            key => new ClientAttemptTracker { FailedAttempts = 1, FirstAttemptUtc = now },
            (key, existing) =>
            {
                if (now - existing.FirstAttemptUtc > WindowDuration)
                {
                    return new ClientAttemptTracker { FailedAttempts = 1, FirstAttemptUtc = now };
                }

                var newCount = existing.FailedAttempts + 1;
                DateTimeOffset? lockedUntil = null;
                foreach (var (failures, lockout) in Tiers)
                {
                    if (newCount >= failures)
                    {
                        lockedUntil = now.Add(lockout);
                        break;
                    }
                }

                return new ClientAttemptTracker
                {
                    FailedAttempts = newCount,
                    FirstAttemptUtc = existing.FirstAttemptUtc,
                    LockedUntilUtc = lockedUntil
                };
            });
    }

    public void ResetAttempts(string clientIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(clientIdentifier))
        {
            _trackers.TryRemove(clientIdentifier, out _);
        }
    }

    public TimeSpan GetRemainingLockout(string clientIdentifier)
    {
        if (!string.IsNullOrWhiteSpace(clientIdentifier) && _trackers.TryGetValue(clientIdentifier, out var tracker))
        {
            var now = _clock.GetUtcNow();
            if (tracker.LockedUntilUtc.HasValue && tracker.LockedUntilUtc.Value > now)
            {
                return tracker.LockedUntilUtc.Value - now;
            }
        }

        return TimeSpan.Zero;
    }

    private sealed class ClientAttemptTracker
    {
        public int FailedAttempts { get; set; }
        public DateTimeOffset FirstAttemptUtc { get; set; }
        public DateTimeOffset? LockedUntilUtc { get; set; }
    }
}
