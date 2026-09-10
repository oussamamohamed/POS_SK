using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IOrderDiscountService
{
    Task<Order> ApplyGlobalDiscountAsync(Guid orderId, DiscountType type, decimal value, string reason, Guid operatorId, CancellationToken ct = default);
    Task<Order> CompOrderItemAsync(Guid orderId, Guid orderItemId, string reason, Guid operatorId, CancellationToken ct = default);
    Task<Order> RemoveDiscountAsync(Guid orderId, CancellationToken ct = default);
    Task<IReadOnlyList<OrderDiscountAudit>> GetDiscountAuditTrailAsync(Guid orderId, CancellationToken ct = default);
}

public interface IRoomBillingService
{
    Task<HotelRoomResident?> GetRoomOccupantAsync(string roomNumber, CancellationToken ct = default);
    Task<RoomFolioCharge> PostRoomChargeAsync(
        Guid orderId,
        string tableNumber,
        string roomNumber,
        string guestName,
        Money amount,
        Money tipAmount,
        string? signatureDataUrl,
        string? notes = null,
        CancellationToken ct = default
    );
    Task<IReadOnlyList<HotelRoomResident>> GetAllOccupiedRoomsAsync(CancellationToken ct = default);
}
