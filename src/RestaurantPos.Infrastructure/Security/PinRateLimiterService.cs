using System;
using System.Collections.Concurrent;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Security;

public sealed class PinRateLimiterService : IPinRateLimiterService
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan WindowDuration = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, ClientAttemptTracker> _trackers = new();

    public bool IsLocked(string clientIdentifier)
    {
        if (string.IsNullOrWhiteSpace(clientIdentifier))
        {
            return false;
        }

        if (_trackers.TryGetValue(clientIdentifier, out var tracker))
        {
            var now = DateTimeOffset.UtcNow;
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

        var now = DateTimeOffset.UtcNow;
        _trackers.AddOrUpdate(
            clientIdentifier,
            key => new ClientAttemptTracker { FailedAttempts = 1, FirstAttemptUtc = now },
            (key, existing) =>
            {
                if (existing.LockedUntilUtc.HasValue && existing.LockedUntilUtc.Value <= now)
                {
                    // Lockout expired, reset
                    return new ClientAttemptTracker { FailedAttempts = 1, FirstAttemptUtc = now };
                }

                if (now - existing.FirstAttemptUtc > WindowDuration)
                {
                    // Window expired, reset
                    return new ClientAttemptTracker { FailedAttempts = 1, FirstAttemptUtc = now };
                }

                var newCount = existing.FailedAttempts + 1;
                DateTimeOffset? lockedUntil = null;
                if (newCount >= MaxFailedAttempts)
                {
                    lockedUntil = now.Add(LockoutDuration);
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
            var now = DateTimeOffset.UtcNow;
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
