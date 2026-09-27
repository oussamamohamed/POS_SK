using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class RoomBillingService : IRoomBillingService
{
    private readonly AppDbContext _dbContext;

    public RoomBillingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HotelRoomResident?> GetRoomOccupantAsync(string roomNumber, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(roomNumber)) return null;

        return await _dbContext.HotelRooms
            .FirstOrDefaultAsync(r => r.RoomNumber == roomNumber.Trim() && r.IsOccupied, ct)
            .ConfigureAwait(false);
    }

    public async Task<RoomFolioCharge> PostRoomChargeAsync(
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
        return await _dbContext.ExecuteInTransactionAsync(
            async ct2 =>
            {
                var room = await GetRoomOccupantAsync(roomNumber, ct2).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"La chambre {roomNumber} n'est pas activement occupée.");

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
                    GuestName = guestName.Trim(),
                    Amount = amount,
                    TipAmount = tipAmount,
                    SignatureDataUrl = signatureDataUrl,
                    Notes = notes,
                    ChargedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.RoomFolioCharges.Add(charge);
                await _dbContext.SaveChangesAsync(ct2).ConfigureAwait(false);

                return charge;
            }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HotelRoomResident>> GetAllOccupiedRoomsAsync(CancellationToken ct = default)
    {
        return await _dbContext.HotelRooms
            .Where(r => r.IsOccupied)
            .OrderBy(r => r.RoomNumber)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
