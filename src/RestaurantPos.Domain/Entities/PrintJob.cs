using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum PrintJobKind { PickupVoucher = 0, Receipt = 1, KitchenTicket = 2, Report = 3 }

public enum PrintJobStatus { Pending = 0, Sent = 1, Failed = 2, Cancelled = 3 }

/// <summary>Document en attente d'impression, rejoué par le PrintWorker jusqu'à DeadlineAtUtc.</summary>
public class PrintJob
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid PrinterId { get; set; }
    public PrintJobKind Kind { get; set; }
    public required string DocumentJson { get; set; }
    public bool OpenCashDrawer { get; set; }
    public PrintJobStatus Status { get; set; } = PrintJobStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
    public DateTimeOffset DeadlineAtUtc { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? SentAtUtc { get; set; }
}
