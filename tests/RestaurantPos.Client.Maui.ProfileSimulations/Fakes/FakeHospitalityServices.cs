using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// Deterministic fake for hospitality operations: <see cref="IOrderDiscountService"/>
/// and <see cref="IRoomBillingService"/>.
/// </summary>
public class FakeHospitalityServices : IOrderDiscountService, IRoomBillingService
{
    public Dictionary<Guid, Order> Orders { get; } = [];
    public Dictionary<string, HotelRoomResident> Rooms { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<OrderDiscountAudit> AuditTrail { get; } = [];
    public List<RoomFolioCharge> RoomCharges { get; } = [];

    public FakeHospitalityServices()
    {
        SeedDefaultRooms();
    }

    private void SeedDefaultRooms()
    {
        Rooms["101"] = new HotelRoomResident
        {
            RoomNumber = "101",
            GuestName = "Jean Dupont",
            IsOccupied = true,
            MaxCreditLimit = 500.0m,
            CurrentBalance = 50.0m
        };

        Rooms["102"] = new HotelRoomResident
        {
            RoomNumber = "102",
            GuestName = "Marie Curie",
            IsOccupied = true,
            MaxCreditLimit = 1000.0m,
            CurrentBalance = 120.0m
        };
    }

    // ── IOrderDiscountService ────────────────────────────────────────────────

    public Task<Order> ApplyGlobalDiscountAsync(
        Guid orderId,
        DiscountType type,
        decimal value,
        string reason,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (value < 0)
            throw new ArgumentException("La valeur de la remise ne peut pas être négative.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif de la remise est obligatoire.");

        if (type == DiscountType.Percentage && value > 100)
            throw new ArgumentException("Le pourcentage de remise ne peut pas dépasser 100%.");

        if (!Orders.TryGetValue(orderId, out var order))
        {
            order = new Order
            {
                Id = orderId,
                TableNumber = "T01",
                Status = OrderStatus.Open
            };
            Orders[orderId] = order;
        }

        long initialTotal = order.Items.Sum(i => i.CalculateTotalTtc().AmountInCents);

        order.GlobalDiscountType = type;
        order.GlobalDiscountValue = value;
        order.GlobalDiscountReason = reason.Trim();

        long newTotal = order.CalculateTotalTtc().AmountInCents;
        long savedCents = Math.Max(0, initialTotal - newTotal);

        var audit = new OrderDiscountAudit
        {
            OrderId = orderId,
            DiscountType = type,
            Value = value,
            AmountSaved = Money.FromCents(savedCents),
            Reason = reason.Trim(),
            AuthorizedByOperatorId = operatorId,
            AppliedAtUtc = DateTimeOffset.UtcNow
        };

        AuditTrail.Add(audit);

        return Task.FromResult(order);
    }

    public Task<Order> CompOrderItemAsync(
        Guid orderId,
        Guid orderItemId,
        string reason,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Le motif de l'article offert est obligatoire.");

        if (!Orders.TryGetValue(orderId, out var order))
        {
            order = new Order
            {
                Id = orderId,
                TableNumber = "T01",
                Status = OrderStatus.Open
            };
            Orders[orderId] = order;
        }

        var item = order.Items.FirstOrDefault(i => i.Id == orderItemId);
        if (item is null)
        {
            item = new OrderItem
            {
                Id = orderItemId,
                OrderId = orderId,
                ProductName = "Article Test",
                Quantity = 1,
                UnitPrice = Money.FromDecimal(10.00m)
            };
            order.Items.Add(item);
        }

        long originalCents = (item.UnitPrice * item.Quantity).AmountInCents;
        item.IsComp = true;
        item.CompReason = reason.Trim();

        var audit = new OrderDiscountAudit
        {
            OrderId = orderId,
            OrderItemId = orderItemId,
            DiscountType = DiscountType.Comp,
            Value = 100m,
            AmountSaved = Money.FromCents(originalCents),
            Reason = reason.Trim(),
            AuthorizedByOperatorId = operatorId,
            AppliedAtUtc = DateTimeOffset.UtcNow
        };

        AuditTrail.Add(audit);

        return Task.FromResult(order);
    }

    public Task<Order> RemoveDiscountAsync(Guid orderId, CancellationToken ct = default)
    {
        if (!Orders.TryGetValue(orderId, out var order))
        {
            order = new Order { Id = orderId };
            Orders[orderId] = order;
        }

        order.GlobalDiscountType = null;
        order.GlobalDiscountValue = 0m;
        order.GlobalDiscountReason = null;

        return Task.FromResult(order);
    }

    public Task<IReadOnlyList<OrderDiscountAudit>> GetDiscountAuditTrailAsync(Guid orderId, CancellationToken ct = default)
    {
        IReadOnlyList<OrderDiscountAudit> result = AuditTrail.Where(a => a.OrderId == orderId).ToList();
        return Task.FromResult(result);
    }

    // ── IRoomBillingService ──────────────────────────────────────────────────

    public Task<HotelRoomResident?> GetRoomOccupantAsync(string roomNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roomNumber))
            return Task.FromResult<HotelRoomResident?>(null);

        Rooms.TryGetValue(roomNumber.Trim(), out var room);
        if (room is not null && room.IsOccupied)
            return Task.FromResult<HotelRoomResident?>(room);

        return Task.FromResult<HotelRoomResident?>(null);
    }

    public Task<RoomFolioCharge> PostRoomChargeAsync(
        Guid orderId,
        string tableNumber,
        string roomNumber,
        string guestName,
        Money amount,
        Money tipAmount,
        string? signatureDataUrl,
        string? notes = null,
        CancellationToken ct = default)
    {
        if (!Rooms.TryGetValue(roomNumber.Trim(), out var room) || !room.IsOccupied)
        {
            throw new InvalidOperationException($"La chambre {roomNumber} n'est pas activement occupée.");
        }

        decimal totalCharge = (amount + tipAmount).ToDecimal();
        if (room.CurrentBalance + totalCharge > room.MaxCreditLimit)
        {
            throw new InvalidOperationException($"Dépassement du plafond de crédit autorisé ({room.MaxCreditLimit} €) pour la chambre {roomNumber}.");
        }

        room.CurrentBalance += totalCharge;

        var charge = new RoomFolioCharge
        {
            OrderId = orderId,
            RoomNumber = roomNumber.Trim(),
            GuestName = guestName,
            Amount = amount,
            TipAmount = tipAmount,
            SignatureDataUrl = signatureDataUrl,
            Notes = notes,
            ChargedAtUtc = DateTimeOffset.UtcNow
        };

        RoomCharges.Add(charge);

        return Task.FromResult(charge);
    }

    public Task<IReadOnlyList<HotelRoomResident>> GetAllOccupiedRoomsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<HotelRoomResident> result = Rooms.Values.Where(r => r.IsOccupied).ToList();
        return Task.FromResult(result);
    }
}
