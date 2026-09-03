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
}
