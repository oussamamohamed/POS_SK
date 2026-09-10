using System;
using System.Linq;
using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class SplitBillViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();
    private readonly Mock<ICheckoutPaymentService> _checkoutMock = new();

    [Fact]
    public void IncreaseGuestsTriggersRecalculationAndHaptic()
    {
        // Arrange
        _checkoutMock
            .Setup(c => c.CalculateEqualSplitPartitions(6000, 3))
            .Returns([2000, 2000, 2000]);

        _checkoutMock
            .Setup(c => c.CalculateEqualSplitPartitions(6000, 2))
            .Returns([3000, 3000]);

        var vm = new SplitBillViewModel(_envMock.Object, _checkoutMock.Object);
        vm.Initialize(6000, 2);

        // Act
        vm.IncreaseGuests();

        // Assert
        vm.GuestsCount.Should().Be(3);
        vm.Partitions.Should().HaveCount(3);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.Once);
    }
}
