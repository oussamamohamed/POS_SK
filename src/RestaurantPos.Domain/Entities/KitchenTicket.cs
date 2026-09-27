using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum TicketStatus
{
    Pending = 0,
    InPreparation = 1,
    Ready = 2,
    Served = 3
}

public enum TicketItemStatus
{
    Pending = 0,
    InPrep = 1,
    Ready = 2
}

public class KitchenTicket
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public required string TableNumber { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public int CoversCount { get; set; } = 1;
    public required string StationId { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Pending;
    public DateTimeOffset DispatchedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PreparedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<KitchenTicketItem> Items { get; init; } = [];
}

public class KitchenTicketItem
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid TicketId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; } = 1;
    public string? ModifiersSummary { get; set; }
    public string? KitchenComment { get; set; }
    public TicketItemStatus Status { get; set; } = TicketItemStatus.Pending;
}

public class PreparationStation
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string SignalRGroup { get; set; }
    public List<string> AssociatedCategoryIds { get; init; } = [];
}
