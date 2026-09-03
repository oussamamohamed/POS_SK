using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Services;

public class LocalJournalService : ILocalJournalService
{
    private readonly LocalAppDbContext _dbContext;
    private readonly IPlatformEnvironmentService _environmentService;

    public LocalJournalService(
        LocalAppDbContext dbContext,
        IPlatformEnvironmentService environmentService)
    {
        _dbContext = dbContext;
        _environmentService = environmentService;
    }

    public async Task<TransactionJournalEntry> RecordTransactionAsync(
        string eventType,
        string idempotencyKey,
        object payload,
        CancellationToken cancellationToken = default)
    {
        string payloadJson = JsonSerializer.Serialize(payload);
        string entryHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey + payloadJson)));

        long lastSeq = await _dbContext.JournalEntries
            .OrderByDescending(j => j.LocalSequence)
            .Select(j => j.LocalSequence)
            .FirstOrDefaultAsync(cancellationToken);

        var entry = new TransactionJournalEntry
        {
            Id = UuidV7.NewGuid(),
            LocalSequence = lastSeq + 1,
            TerminalId = _environmentService.GetCurrentDeviceProfile().DeviceId,
            OccurredAtUtc = DateTimeOffset.UtcNow,
            IdempotencyKey = idempotencyKey,
            EventType = eventType,
            PayloadJson = payloadJson,
            EntryHash = entryHash
        };

        var outboxMessage = new OutboxSyncMessage
        {
            Id = UuidV7.NewGuid(),
            TerminalId = entry.TerminalId,
            CreatedAtUtc = entry.OccurredAtUtc,
            Status = SyncStatus.Pending,
            EventType = eventType,
            IdempotencyKey = idempotencyKey,
            PayloadJson = payloadJson
        };

        _dbContext.JournalEntries.Add(entry);
        _dbContext.OutboxMessages.Add(outboxMessage);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return entry;
    }

    public async Task<IReadOnlyList<TransactionJournalEntry>> GetPendingJournalEntriesAsync(int maxCount = 100, CancellationToken cancellationToken = default)
    {
        return await _dbContext.JournalEntries
            .OrderBy(j => j.LocalSequence)
            .Take(maxCount)
            .ToListAsync(cancellationToken);
    }
}
