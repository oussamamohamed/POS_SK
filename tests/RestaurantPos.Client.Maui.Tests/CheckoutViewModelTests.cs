using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class CheckoutViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();

    [Fact]
    public void AddCashFastBillCalculatesChangeAndReducesRemainingBalance()
    {
        // Arrange
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 3500); // 35.00 EUR total

        // Act: Tap fast 50€ bill button
        vm.AddCashFastBill(5000);

        // Assert
        vm.RemainingBalanceCents.Should().Be(0);
        vm.ChangeDueCents.Should().Be(1500); // 50 - 35 = 15.00 EUR
        vm.AppliedTenders.Should().HaveCount(1);
        vm.AppliedTenders.First().Method.Should().Be(PaymentMethod.Cash);
    }

    [Fact]
    public async Task FinalizeCheckoutAsyncWithCardSettlesFullBalance()
    {
        // Arrange
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 8000); // 80.00 EUR total
        vm.SelectPaymentMethod(PaymentMethod.CreditCard);

        // Act
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        vm.RemainingBalanceCents.Should().Be(0);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }

    [Fact]
    public async Task MultiTender_CashPartialThenCard_ShouldCompleteWithZeroBalance()
    {
        // Arrange: 25.00 EUR total bill
        var vm = new CheckoutViewModel(_envMock.Object);
        vm.Initialize(Guid.NewGuid(), 2500);

        // Act 1: Pay 10.00 EUR cash
        vm.AddCashFastBill(1000);
        vm.RemainingBalanceCents.Should().Be(1500);
        vm.ChangeDueCents.Should().Be(0);

        // Act 2: Pay remainder (15.00 EUR) with Credit Card
        vm.SelectPaymentMethod(PaymentMethod.CreditCard);
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        vm.RemainingBalanceCents.Should().Be(0);
        vm.AppliedTenders.Should().HaveCount(2);
        vm.AppliedTenders[0].Method.Should().Be(PaymentMethod.Cash);
        vm.AppliedTenders[0].AmountCents.Should().Be(1000);
        vm.AppliedTenders[1].Method.Should().Be(PaymentMethod.CreditCard);
        vm.AppliedTenders[1].AmountCents.Should().Be(1500);
    }

    [Fact]
    public async Task FinalizeCheckout_WithRoomCharge_ShouldRecordRoomAndGuestDetails()
    {
        // Arrange
        var roomBillingMock = new Mock<Application.Common.Interfaces.IRoomBillingService>();
        var vm = new CheckoutViewModel(_envMock.Object, null, roomBillingMock.Object);
        var orderId = Guid.NewGuid();
        vm.Initialize(orderId, 4500);
        vm.SelectPaymentMethod(PaymentMethod.RoomCharge);
        vm.RoomNumber = "204";
        vm.GuestName = "Jean Dupont";

        // Act
        await vm.FinalizeCheckoutAsync();

        // Assert
        vm.IsCompleted.Should().BeTrue();
        roomBillingMock.Verify(r => r.PostRoomChargeAsync(
            orderId,
            "T01",
            "204",
            "Jean Dupont",
            It.IsAny<Domain.ValueObjects.Money>(),
            It.IsAny<Domain.ValueObjects.Money>(),
            null,
            null,
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }
}
