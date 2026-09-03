using System;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IPinRateLimiterService
{
    bool IsLocked(string clientIdentifier);
    void RecordFailedAttempt(string clientIdentifier);
    void ResetAttempts(string clientIdentifier);
    TimeSpan GetRemainingLockout(string clientIdentifier);
}
