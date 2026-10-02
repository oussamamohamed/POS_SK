using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class FiscalJournalService : IFiscalJournal
{
    private readonly AppDbContext _dbContext;

    public FiscalJournalService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<TransactionJournalEntry> AppendAsync<T>(
        string eventType,
        T payload,
        string? terminalId = null,
        Guid? operatorId = null,
        CancellationToken cancellationToken = default) where T : class
    {
        string payloadJson = JsonSerializer.Serialize(payload);
        return AppendAsync(eventType, payloadJson, terminalId, operatorId, cancellationToken);
    }

    public async Task<TransactionJournalEntry> AppendAsync(
        string eventType,
        string payloadJson,
        string? terminalId = null,
        Guid? operatorId = null,
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction != null)
        {
            return await AppendInternalAsync(eventType, payloadJson, terminalId, operatorId, cancellationToken).ConfigureAwait(false);
        }

        return await _dbContext.ExecuteInTransactionAsync(
            ct => AppendInternalAsync(eventType, payloadJson, terminalId, operatorId, ct),
            IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<TransactionJournalEntry> AppendInternalAsync(
        string eventType,
        string payloadJson,
        string? terminalId,
        Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var lastEntry = await _dbContext.JournalEntries
            .Where(j => j.ChainSequence != null)
            .OrderByDescending(j => j.ChainSequence)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        long nextSequence = (lastEntry?.ChainSequence ?? 0) + 1;
        string prevHash = lastEntry?.EntryHash ?? NF525FiscalAuditService.GenesisHash;
        var now = DateTimeOffset.UtcNow;

        string entryHash = FiscalHashing.ComputeJetHash(
            prevHash,
            nextSequence,
            eventType,
            now,
            terminalId,
            operatorId,
            payloadJson);

        var entry = new TransactionJournalEntry
        {
            Id = UuidV7.NewGuid(),
            LocalSequence = nextSequence,
            TerminalId = terminalId,
            OccurredAtUtc = now,
            IdempotencyKey = UuidV7.NewGuid().ToString(),
            EventType = eventType,
            PayloadJson = payloadJson,
            EntryHash = entryHash,
            ChainSequence = nextSequence,
            PreviousHash = prevHash,
            OperatorId = operatorId
        };

        _dbContext.JournalEntries.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return entry;
    }
}
