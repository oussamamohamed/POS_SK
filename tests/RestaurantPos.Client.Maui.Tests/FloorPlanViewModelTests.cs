using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class FloorPlanViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();

    [Fact]
    public async Task SelectTableAsyncOnFreeTableOpensCoversPrompt()
    {
        // Arrange
        var vm = new FloorPlanViewModel(_envMock.Object);
        var freeTable = vm.Tables.First(t => t.Status == TableStatus.Free);

        // Act
        await vm.SelectTableAsync(freeTable);

        // Assert
        vm.SelectedTable.Should().Be(freeTable);
        vm.IsTablePromptOpen.Should().BeTrue();
        vm.CoversToOpen.Should().Be(freeTable.Capacity);
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.LightTap), Times.Once);
    }

    [Fact]
    public async Task ConfirmOpenTableAsyncSetsTableOccupiedAndClosesPrompt()
    {
        // Arrange
        var vm = new FloorPlanViewModel(_envMock.Object);
        var freeTable = vm.Tables.First(t => t.Status == TableStatus.Free);
        await vm.SelectTableAsync(freeTable);
        vm.CoversToOpen = 3;

        // Act
        await vm.ConfirmOpenTableAsync();

        // Assert
        freeTable.Status.Should().Be(TableStatus.Occupied);
        freeTable.CoversCount.Should().Be(3);
        vm.IsTablePromptOpen.Should().BeFalse();
        _envMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }
}
