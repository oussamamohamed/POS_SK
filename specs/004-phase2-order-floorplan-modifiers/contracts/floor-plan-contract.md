# Contract: Dining Room Floor Plan & Table Management

**Feature**: `004-phase2-order-floorplan-modifiers`
**Domain**: Floor Plan & Table Operations

## 1. Table Management Service Interface

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public enum TableStatus
{
    Free = 0,
    Occupied = 1,
    BillRequested = 2,
    Paid = 3
}

public interface ITableManagementService
{
    ValueTask<IReadOnlyList<DiningTableDto>> GetFloorPlanTablesAsync(CancellationToken cancellationToken = default);
    
    ValueTask<DiningTableDto> OpenTableAsync(string tableNumber, int coversCount, Guid operatorId, CancellationToken cancellationToken = default);
    
    ValueTask<bool> TransferTableAsync(string sourceTableNumber, string targetTableNumber, CancellationToken cancellationToken = default);
    
    ValueTask<bool> UpdateTableStatusAsync(string tableNumber, TableStatus newStatus, CancellationToken cancellationToken = default);
}

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
```
