using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public record DiningTableDto(
    string TableNumber,
    int Capacity,
    TableStatus Status,
    double PositionX,
    double PositionY,
    string? AssignedWaiterName,
    int CoversCount,
    Guid? ActiveOrderId,
    DateTimeOffset? OpenedAtUtc);

public record ActiveTableOrderDto(
    Guid OrderId,
    string TableNumber,
    string? WaiterName,
    int CoversCount,
    DateTimeOffset OpenedAtUtc,
    IReadOnlyList<ActiveOrderLineDto> Lines,
    decimal TotalHtAmount,
    decimal TotalVatAmount,
    decimal TotalTtcAmount,
    DiscountType? GlobalDiscountType = null,
    decimal GlobalDiscountValue = 0.0m,
    string? GlobalDiscountReason = null
);

public record ActiveOrderLineDto(
    Guid LineId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    decimal TaxRatePercent,
    string? PreparationStationId,
    bool IsDispatched,
    IReadOnlyList<string> ModifiersSummary,
    CourseType Course = CourseType.Direct,
    bool IsComp = false,
    decimal DiscountPercent = 0.0m,
    decimal ModifiersPriceExtra = 0.0m
);

public record OrderItemInputDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal TaxRatePercent,
    string? PreparationStationId,
    IReadOnlyList<string>? Modifiers,
    CourseType Course = CourseType.Direct,
    decimal ModifiersPriceExtra = 0.0m
);

public interface ITableManagementService
{
    Task<IReadOnlyList<DiningTableDto>> GetFloorPlanTablesAsync(CancellationToken cancellationToken = default);
    Task<DiningTableDto> CreateTableAsync(string tableNumber, int capacity, double positionX = 0, double positionY = 0, CancellationToken cancellationToken = default);
    Task<DiningTableDto> OpenTableAsync(string tableNumber, int coversCount, Guid operatorId, string operatorName, CancellationToken cancellationToken = default);
    Task<ActiveTableOrderDto?> GetActiveOrderForTableAsync(string tableNumber, CancellationToken cancellationToken = default);
    Task<ActiveTableOrderDto> AddOrUpdateTableOrderItemsAsync(string tableNumber, IReadOnlyList<OrderItemInputDto> items, CancellationToken cancellationToken = default);
    Task<bool> DispatchOrderLinesAsync(string tableNumber, CancellationToken cancellationToken = default);
    Task<bool> TransferTableAsync(string sourceTableNumber, string targetTableNumber, CancellationToken cancellationToken = default);
    Task<bool> MergeTablesAsync(string sourceTableNumber, string targetTableNumber, CancellationToken cancellationToken = default);
    Task<bool> UpdateTableStatusAsync(string tableNumber, TableStatus newStatus, CancellationToken cancellationToken = default);
}
