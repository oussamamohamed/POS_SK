using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public class HeldOrder
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public Guid OrderId { get; set; }
    public string? CustomerLabel { get; set; }
    public OrderDestination Destination { get; set; } = OrderDestination.Takeaway;
    public int ItemCount { get; set; }
    public Money TotalTtc { get; set; }
    public required string OrderSnapshotJson { get; set; }
    public DateTimeOffset HeldAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public Guid HeldByStaffId { get; set; }
    public bool IsRecalled { get; set; }
    public DateTimeOffset? RecalledAtUtc { get; set; }
    public bool IsVoided { get; set; }
    public DateTimeOffset? VoidedAtUtc { get; set; }
    public string? VoidReason { get; set; }
    public Guid? VoidedByStaffId { get; set; }
}
