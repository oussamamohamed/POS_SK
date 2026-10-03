using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IFiscalJournal
{
    Task<TransactionJournalEntry> AppendAsync(
        string eventType,
        string payloadJson,
        string? terminalId = null,
        Guid? operatorId = null,
        CancellationToken cancellationToken = default);

    Task<TransactionJournalEntry> AppendAsync<T>(
        string eventType,
        T payload,
        string? terminalId = null,
        Guid? operatorId = null,
        CancellationToken cancellationToken = default) where T : class;
}
