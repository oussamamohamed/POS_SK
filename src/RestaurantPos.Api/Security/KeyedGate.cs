using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Api.Security;

/// <summary>Exécute les sections critiques d'une même clé (IP) l'une après l'autre.</summary>
internal static class KeyedGate
{
    // shortcut: un sémaphore par clé, jamais purgé (acceptable en LAN), purger si le serveur est exposé hors LAN.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    public static async Task<T> RunAsync<T>(string key, Func<Task<T>> action, CancellationToken ct)
    {
        var gate = Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }
}
