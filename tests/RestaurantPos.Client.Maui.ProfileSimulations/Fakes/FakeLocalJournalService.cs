using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// No-op fake for <see cref="ILocalJournalService"/>.
/// Required by <see cref="RestaurantPos.Client.Maui.ViewModels.PosTerminalViewModel"/>.
/// All operations are no-ops that return minimal valid stubs.
/// </summary>
public sealed class FakeLocalJournalService : ILocalJournalService
{
    public Task<TransactionJournalEntry> RecordTransactionAsync(
        string eventType,
        string idempotencyKey,
        object payload,
        CancellationToken cancellationToken = default)
    {
        var entry = new TransactionJournalEntry
        {
            Id = UuidV7.NewGuid(),
            TerminalId = "SIM-POS01",
            IdempotencyKey = idempotencyKey,
            EventType = eventType,
            PayloadJson = "{}",
            EntryHash = "FAKE_HASH"
        };
        return Task.FromResult(entry);
    }

    public Task<IReadOnlyList<TransactionJournalEntry>> GetPendingJournalEntriesAsync(
        int maxCount = 100,
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<TransactionJournalEntry>>(Array.Empty<TransactionJournalEntry>());
}
