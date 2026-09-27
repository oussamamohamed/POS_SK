using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class RoomBillingServiceTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task PostRoomCharge_ValidOccupiedRoom_CreatesFolioChargeAndUpdatesBalance()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var service = new RoomBillingService(db);

        var room = new HotelRoomResident
        {
            RoomNumber = "204",
            GuestName = "Alexandre Dupont",
            IsOccupied = true,
            CurrentBalance = 50.0m,
            MaxCreditLimit = 500.0m,
            CheckInDateUtc = DateTimeOffset.UtcNow.AddDays(-1),
            CheckOutDateUtc = DateTimeOffset.UtcNow.AddDays(2)
        };
        db.HotelRooms.Add(room);
        await db.SaveChangesAsync();

        var orderId = Guid.NewGuid();
        var amount = Money.FromEuros(35.50m);
        var tip = Money.FromEuros(3.50m);

        // Act
        var charge = await service.PostRoomChargeAsync(
            orderId, "T3", "204", "Alexandre Dupont", amount, tip, "data:image/png;base64,mockSignature");

        // Assert
        charge.Should().NotBeNull();
        charge.RoomNumber.Should().Be("204");
        charge.GuestName.Should().Be("Alexandre Dupont");
        charge.Amount.ToDecimal().Should().Be(35.50m);
        charge.TipAmount.ToDecimal().Should().Be(3.50m);
        charge.SignatureDataUrl.Should().Be("data:image/png;base64,mockSignature");

        var updatedRoom = await db.HotelRooms.FirstOrDefaultAsync(r => r.RoomNumber == "204");
        updatedRoom!.CurrentBalance.Should().Be(89.00m); // 50 + 35.50 + 3.50 = 89.00 EUR
    }

    [Fact]
    public async Task PostRoomCharge_NonExistentOrVacantRoom_ThrowsInvalidOperationException()
    {
        using var db = CreateInMemoryDb();
        var service = new RoomBillingService(db);

        var act = () => service.PostRoomChargeAsync(
            Guid.NewGuid(), "T1", "999", "Inconnu", Money.FromEuros(20m), Money.Zero(), null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*n'est pas activement occupée*");
    }

    [Fact]
    public async Task PostRoomCharge_ExceedingCreditLimit_ThrowsInvalidOperationException()
    {
        using var db = CreateInMemoryDb();
        var service = new RoomBillingService(db);

        var room = new HotelRoomResident
        {
            RoomNumber = "101",
            GuestName = "Client Test",
            IsOccupied = true,
            CurrentBalance = 180.0m,
            MaxCreditLimit = 200.0m
        };
        db.HotelRooms.Add(room);
        await db.SaveChangesAsync();

        // Attempt to charge 50 EUR (180 + 50 = 230 > 200 limit)
        var act = () => service.PostRoomChargeAsync(
            Guid.NewGuid(), "T1", "101", "Client Test", Money.FromEuros(50m), Money.Zero(), null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Dépassement du plafond de crédit*");
    }
}
