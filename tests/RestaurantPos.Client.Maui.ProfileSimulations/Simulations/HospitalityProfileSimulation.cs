using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// Simulation of advanced hospitality features (P2 — User Story 2):
/// Global discounts, item comping, and hotel room charges.
/// </summary>
public class HospitalityProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeLocalJournalService _journal = new();
    private readonly FakeHospitalityServices _hospitality = new();
    private readonly FakeCheckoutPaymentService _checkout = new();

    [Fact]
    public async Task Manager_ApplyGlobalDiscount_ShouldUpdateTotal()
    {
        // Arrange: POS Terminal initialized with discount service
        var posVm = new PosTerminalViewModel(_env, _journal, _hospitality);
        var espresso = posVm.AvailableProducts.First(p => p.Name == "Café Espresso"); // 2.50 €
        var burger = posVm.AvailableProducts.First(p => p.Name == "Burger Maison & Frites"); // 16.50 €

        await posVm.AddProductAsync(espresso);
        await posVm.AddProductAsync(burger);
        posVm.TotalTtc.AmountInCents.Should().Be(1900); // 19.00 €

        _hospitality.Orders[posVm.ActiveOrder.Id] = posVm.ActiveOrder;

        // Act: Floor Manager applies 10% global discount
        var manager = SimulatedOperator.FloorManager();
        await posVm.ApplyGlobalDiscountAsync(
            DiscountType.Percentage,
            10.0m,
            reason: "Remise fidélité VIP",
            operatorId: manager.OperatorId
        );

        // Assert: 19.00 € - 10% = 17.10 € (1710 cents)
        posVm.TotalTtc.AmountInCents.Should().Be(1710);
        posVm.ActiveOrder.GlobalDiscountType.Should().Be(DiscountType.Percentage);
        posVm.ActiveOrder.GlobalDiscountValue.Should().Be(10.0m);
        posVm.ActiveOrder.GlobalDiscountReason.Should().Be("Remise fidélité VIP");

        // Verify audit record in hospitality fake
        _hospitality.AuditTrail.Should().ContainSingle(a =>
            a.DiscountType == DiscountType.Percentage &&
            a.Value == 10.0m &&
            a.AmountSaved.AmountInCents == 190 &&
            a.AuthorizedByOperatorId == manager.OperatorId
        );
    }

    [Fact]
    public async Task Manager_CompOrderItem_ShouldSetPriceToZero()
    {
        // Arrange: Cart with coffee (2.50 €) and dessert (7.50 €) = 10.00 €
        var posVm = new PosTerminalViewModel(_env, _journal, _hospitality);
        var espresso = posVm.AvailableProducts.First(p => p.Name == "Café Espresso");
        var tiramisu = posVm.AvailableProducts.First(p => p.Name == "Tiramisu Maison");

        await posVm.AddProductAsync(espresso);
        await posVm.AddProductAsync(tiramisu);
        posVm.CartItems.Should().HaveCount(2);
        posVm.TotalTtc.AmountInCents.Should().Be(1000); // 10.00 €

        _hospitality.Orders[posVm.ActiveOrder.Id] = posVm.ActiveOrder;

        var coffeeItem = posVm.CartItems.First(i => i.ProductName == "Café Espresso");

        // Act: Manager comps the coffee item
        var manager = SimulatedOperator.FloorManager();
        await posVm.CompItemAsync(coffeeItem, "Geste commercial d'accueil", manager.OperatorId);

        // Assert: Coffee is marked comp, item remains on cart/receipt, but total decreases by coffee unit price
        coffeeItem.IsComp.Should().BeTrue();
        coffeeItem.CompReason.Should().Be("Geste commercial d'accueil");
        posVm.CartItems.Should().HaveCount(2); // Coffee remains on receipt
        posVm.TotalTtc.AmountInCents.Should().Be(750); // 10.00 € - 2.50 € = 7.50 €

        // Verify audit trail
        _hospitality.AuditTrail.Should().ContainSingle(a =>
            a.DiscountType == DiscountType.Comp &&
            a.OrderItemId == coffeeItem.Id &&
            a.AmountSaved.AmountInCents == 250 &&
            a.AuthorizedByOperatorId == manager.OperatorId
        );
    }

    [Fact]
    public async Task Cashier_PostRoomCharge_ShouldRouteToRoom()
    {
        // Arrange: CheckoutViewModel with checkout payment service and room billing service
        var checkoutVm = new CheckoutViewModel(_env, _checkout, _hospitality);
        var orderId = Guid.NewGuid();
        long totalDueCents = 5000; // 50.00 €

        checkoutVm.Initialize(orderId, totalDueCents);
        checkoutVm.SelectPaymentMethod(PaymentMethod.RoomCharge);
        checkoutVm.RoomNumber = "101";
        checkoutVm.GuestName = "Jean Dupont";

        // Initial room 101 balance is 50.00 €
        _hospitality.Rooms["101"].CurrentBalance.Should().Be(50.0m);

        // Act: Cashier finalizes checkout using room billing
        await checkoutVm.FinalizeCheckoutAsync();

        // Assert: Payment completed, receipt generated
        checkoutVm.IsCompleted.Should().BeTrue();
        checkoutVm.RemainingBalanceCents.Should().Be(0);
        checkoutVm.ReceiptNumber.Should().NotBeNullOrEmpty();

        // Assert: Room charge recorded in billing service
        _hospitality.RoomCharges.Should().ContainSingle(c =>
            c.OrderId == orderId &&
            c.RoomNumber == "101" &&
            c.GuestName == "Jean Dupont" &&
            c.Amount.AmountInCents == 5000
        );

        // Assert: Room balance incremented by 50.00 € (50.00 € + 50.00 € = 100.00 €)
        _hospitality.Rooms["101"].CurrentBalance.Should().Be(100.0m);
    }
}
