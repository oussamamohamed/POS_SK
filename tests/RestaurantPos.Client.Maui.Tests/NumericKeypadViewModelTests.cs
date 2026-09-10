using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class NumericKeypadViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();

    [Fact]
    public async Task AppendDigitAccumulatesDigitsAndValidatesPin()
    {
        // Arrange
        var authMock = new Mock<IOperatorAuthenticationService>();
        authMock.Setup(a => a.AuthenticatePinAsync("1234", default))
                .ReturnsAsync(new OperatorAuthenticationResult(true, Guid.NewGuid(), "Alexandre Dupont", UserRole.Waiter, null));

        var vm = new PinLockViewModel(_envMock.Object, authMock.Object);

        // Act
        await vm.AppendDigitAsync("1");
        await vm.AppendDigitAsync("2");
        await vm.AppendDigitAsync("3");
        await vm.AppendDigitAsync("4");

        // Assert
        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorName.Should().Be("Alexandre Dupont");
        vm.CurrentOperatorRole.Should().Be(UserRole.Waiter);
        vm.ErrorMessage.Should().BeEmpty();
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }

    [Fact]
    public async Task DeleteDigitRemovesLastEnteredDigit()
    {
        // Arrange
        var vm = new PinLockViewModel(_envMock.Object);
        await vm.AppendDigitAsync("1");
        await vm.AppendDigitAsync("2");

        // Act
        vm.DeleteDigit();

        // Assert
        vm.PinInput.Should().Be("1");
    }
}
