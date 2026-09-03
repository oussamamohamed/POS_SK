using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public enum CourseType
{
    Direct = 0,
    Suite = 1,
    Dessert = 2,
    OnDemand = 3
}

public enum DiscountType
{
    Percentage = 0,
    FixedAmount = 1,
    Comp = 2
}

public class HotelRoomResident
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string RoomNumber { get; set; }
    public required string GuestName { get; set; }
    public DateTimeOffset CheckInDateUtc { get; set; }
    public DateTimeOffset CheckOutDateUtc { get; set; }
    public bool IsOccupied { get; set; } = true;
    public decimal MaxCreditLimit { get; set; } = 500.0m;
    public decimal CurrentBalance { get; set; }
}

public class RoomFolioCharge
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public required string RoomNumber { get; set; }
    public required string GuestName { get; set; }
    public Money Amount { get; set; } = Money.Zero();
    public Money TipAmount { get; set; } = Money.Zero();
    public string? SignatureDataUrl { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset ChargedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public class TableTransferLog
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string SourceTableNumber { get; set; }
    public required string TargetTableNumber { get; set; }
    public Guid OrderId { get; set; }
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public bool IsMerge { get; set; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}

public class OrderDiscountAudit
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public Guid? OrderItemId { get; set; }
    public DiscountType DiscountType { get; set; }
    public decimal Value { get; set; }
    public Money AmountSaved { get; set; } = Money.Zero();
    public required string Reason { get; set; }
    public Guid AuthorizedByOperatorId { get; set; }
    public DateTimeOffset AppliedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public class GridLayout
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string CategoryId { get; set; }
    public string Name { get; set; } = "Défaut";
    public int ColumnsCount { get; set; } = 4;
    public int RowsCount { get; set; } = 4;
    public int PageIndex { get; set; }
    public int Version { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<GridSlot> Slots { get; set; } = new();
}

public class GridSlot
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid GridLayoutId { get; set; }
    public Guid? ProductId { get; set; }
    public int RowIndex { get; set; }
    public int ColumnIndex { get; set; }
    public int SlotIndex { get; set; }
    public string? CustomLabel { get; set; }
    public string? CustomColorHex { get; set; }
    public bool IsDisabled { get; set; }

    public GridLayout? GridLayout { get; set; }
    public Product? Product { get; set; }
}


