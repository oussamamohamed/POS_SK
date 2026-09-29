using System;
using System.Collections.Concurrent;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>IsOnline null = aucun envoi depuis le démarrage.</summary>
public sealed record PrinterState(bool? IsOnline, DateTimeOffset? SinceUtc);

public sealed class PrinterStatusTracker
{
    private static readonly PrinterState Unknown = new(null, null);
    private readonly ConcurrentDictionary<Guid, PrinterState> _states = new();

    public PrinterState Get(Guid printerId) => _states.GetValueOrDefault(printerId, Unknown);

    /// <returns>true si la transition doit être notifiée : en ligne ↔ hors ligne, ou premier échec. Le premier succès après démarrage est silencieux.</returns>
    public bool Record(Guid printerId, bool success, DateTimeOffset nowUtc)
    {
        var previous = Get(printerId);
        if (previous.IsOnline == success) return false;
        _states[printerId] = new PrinterState(success, nowUtc);
        return previous.IsOnline is not null || !success;
    }
}
