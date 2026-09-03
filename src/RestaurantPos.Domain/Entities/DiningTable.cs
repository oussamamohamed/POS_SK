using System;

namespace RestaurantPos.Domain.Entities;

public enum TableStatus
{
    Free = 0,
    Occupied = 1,
    BillRequested = 2,
    Paid = 3
}

public class DiningTable
{
    public required string TableNumber { get; set; }
    public int Capacity { get; set; } = 4;
    public TableStatus Status { get; set; } = TableStatus.Free;
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public string? AssignedWaiterName { get; set; }
    public Guid? AssignedWaiterId { get; set; }
    public int CoversCount { get; set; }
    public Guid? ActiveOrderId { get; set; }
    public DateTimeOffset? OpenedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
