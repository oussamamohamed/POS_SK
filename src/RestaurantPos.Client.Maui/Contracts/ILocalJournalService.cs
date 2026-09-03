using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Contracts;

public interface ILocalJournalService
{
    Task<TransactionJournalEntry> RecordTransactionAsync(string eventType, string idempotencyKey, object payload, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransactionJournalEntry>> GetPendingJournalEntriesAsync(int maxCount = 100, CancellationToken cancellationToken = default);
}
