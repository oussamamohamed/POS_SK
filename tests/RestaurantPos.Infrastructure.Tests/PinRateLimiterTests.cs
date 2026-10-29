using System;
using FluentAssertions;
using RestaurantPos.Infrastructure.Security;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PinRateLimiterTests
{
    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }

    private static void Fail(PinRateLimiterService l, int n)
    {
        for (var i = 0; i < n; i++)
        {
            l.RecordFailedAttempt("k");
        }
    }

    [Fact]
    public void Four_failures_do_not_lock()
    {
        var l = new PinRateLimiterService(new FakeClock());
        Fail(l, 4);
        l.IsLocked("k").Should().BeFalse();
    }

    [Fact]
    public void Escalates_30s_then_5min_then_30min()
    {
        var clock = new FakeClock();
        var l = new PinRateLimiterService(clock);

        Fail(l, 5);
        l.GetRemainingLockout("k").Should().Be(TimeSpan.FromSeconds(30));
        clock.Advance(TimeSpan.FromSeconds(31));
        l.IsLocked("k").Should().BeFalse();

        Fail(l, 5); // 10 échecs au total
        l.GetRemainingLockout("k").Should().Be(TimeSpan.FromMinutes(5));
        clock.Advance(TimeSpan.FromMinutes(6));
        l.IsLocked("k").Should().BeFalse();

        Fail(l, 10); // 20 échecs au total
        l.GetRemainingLockout("k").Should().Be(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Window_expires_after_one_hour()
    {
        var clock = new FakeClock();
        var l = new PinRateLimiterService(clock);

        Fail(l, 4);
        clock.Advance(TimeSpan.FromMinutes(61));
        Fail(l, 4); // compteur repart à 1 : 4 échecs au total dans la nouvelle fenêtre
        l.IsLocked("k").Should().BeFalse();
        Fail(l, 1);
        l.IsLocked("k").Should().BeTrue();
    }

    [Fact]
    public void Reset_clears_counter()
    {
        var l = new PinRateLimiterService(new FakeClock());
        Fail(l, 5);
        l.ResetAttempts("k");
        l.IsLocked("k").Should().BeFalse();
    }
}
