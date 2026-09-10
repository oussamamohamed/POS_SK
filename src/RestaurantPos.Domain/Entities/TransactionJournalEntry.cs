using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public class TransactionJournalEntry
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public long LocalSequence { get; set; }
    public required string TerminalId { get; set; }
    public DateTimeOffset OccurredAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public required string IdempotencyKey { get; set; }
    public required string EventType { get; set; }
    public required string PayloadJson { get; set; }
    public required string EntryHash { get; set; }
}
