using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum SyncStatus
{
    Pending = 0,
    InFlight = 1,
    Completed = 2,
    Failed = 3
}

public class OutboxSyncMessage
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public SyncStatus Status { get; set; } = SyncStatus.Pending;
    public int RetryCount { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public required string EventType { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string PayloadJson { get; set; }
    public string? ErrorMessage { get; set; }
}
