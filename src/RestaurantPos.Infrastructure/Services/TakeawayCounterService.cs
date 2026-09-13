using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Services;

public class TakeawayCounterService : ITakeawayCounterService
{
    private static readonly ConcurrentDictionary<string, int> Counters = new();
    private static DateOnly _currentDate = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly object DateLock = new();

    public Task<string> GetNextPickupNumberAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        EnsureDailyReset();

        string prefix = DeriveTerminalPrefix(terminalId);
        int sequence = Counters.AddOrUpdate(prefix, 1, (_, current) => current >= 99 ? 1 : current + 1);

        string pickupNumber = $"#{prefix}-{sequence:D2}";
        return Task.FromResult(pickupNumber);
    }

    public Task ResetDailySequencesAsync(CancellationToken cancellationToken = default)
    {
        Counters.Clear();
        return Task.CompletedTask;
    }

    private static void EnsureDailyReset()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (today != _currentDate)
        {
            lock (DateLock)
            {
                if (today != _currentDate)
                {
                    Counters.Clear();
                    _currentDate = today;
                }
            }
        }
    }

    private static string DeriveTerminalPrefix(string terminalId)
    {
        if (string.IsNullOrWhiteSpace(terminalId))
        {
            return "A";
        }

        string clean = terminalId.Trim().ToUpperInvariant();
        if (clean.EndsWith('B') || clean.Contains("POS-B", StringComparison.Ordinal) || clean.Contains("POS02", StringComparison.Ordinal) || clean.Contains('2'))
        {
            return "B";
        }
        if (clean.EndsWith('C') || clean.Contains("POS-C", StringComparison.Ordinal) || clean.Contains("POS03", StringComparison.Ordinal) || clean.Contains('3'))
        {
            return "C";
        }
        if (clean.EndsWith('D') || clean.Contains("POS-D", StringComparison.Ordinal) || clean.Contains("POS04", StringComparison.Ordinal) || clean.Contains('4'))
        {
            return "D";
        }

        return "A";
    }
}
