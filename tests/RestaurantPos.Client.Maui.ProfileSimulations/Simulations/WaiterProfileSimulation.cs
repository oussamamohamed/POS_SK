using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// End-to-end simulation of the Waiter profile (P1 — MVP).
/// Exercises: PIN auth → FloorPlan → PosTerminal → kitchen dispatch → permission denial.
/// </summary>
public class WaiterProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeOperatorAuthenticationService _auth = new(SimulatedOperator.Waiter());
    private readonly FakeLocalJournalService _journal = new();

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateWaiter_WithValidPin_ShouldSucceed()
    {
        var vm = new PinLockViewModel(_env, _auth);

        foreach (char digit in "1111")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorRole.Should().Be(UserRole.Waiter);
        vm.CurrentOperatorName.Should().Be("Sophie Durand");
        _env.HapticCalls.Should().Contain(HapticFeedbackType.Success);
    }

    // ── Floor Plan ────────────────────────────────────────────────────────────

    [Fact]
    public void Waiter_FloorPlan_ShouldHaveDefaultTables()
    {
        var vm = new FloorPlanViewModel(_env);
        vm.Tables.Should().NotBeEmpty("floor plan is seeded with default tables");
    }

    // ── POS Terminal ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Waiter_AddTwoDifferentProducts_ShouldHaveTwoCartItems()
    {
        var vm = new PosTerminalViewModel(_env, _journal);

        await vm.AddProductAsync(vm.AvailableProducts[0]);
        await vm.AddProductAsync(vm.AvailableProducts[1]);

        vm.CartItems.Should().HaveCount(2);
        vm.TotalTtc.AmountInCents.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Waiter_AddSameProductTwice_ShouldIncrementQuantity()
    {
        var vm = new PosTerminalViewModel(_env, _journal);
        var product = vm.AvailableProducts.First();

        await vm.AddProductAsync(product);
        await vm.AddProductAsync(product);

        vm.CartItems.Should().HaveCount(1);
        vm.CartItems.First().Quantity.Should().Be(2);
    }

    [Fact]
    public async Task Waiter_SendOrder_ShouldClearCartAndCallTableService()
    {
        var tableServiceMock = new Mock<ITableManagementService>();
        var vm = new PosTerminalViewModel(_env, _journal);
        vm.ActiveTable = "T01";
        vm.CartItems.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Salade César",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(9.50m),
            IsDispatched = false
        });
        vm.RecalculateTotals();

        await vm.SendKitchenAndResetAsync(tableServiceMock.Object);

        vm.CartItems.Should().BeEmpty();
        vm.TotalTtc.ToDecimal().Should().Be(0m);
        tableServiceMock.Verify(
            s => s.AddOrUpdateTableOrderItemsAsync("T01", It.IsAny<IReadOnlyList<OrderItemInputDto>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        tableServiceMock.Verify(
            s => s.DispatchOrderLinesAsync("T01", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Permission Denial ─────────────────────────────────────────────────────

    [Fact]
    public void Waiter_DomainPermissions_ShouldDenyElevatedOperations()
    {
        var user = new User { Name = "Sophie", Role = UserRole.Waiter, PinHash = "h", PinSalt = "s" };
        user.CanVoidItems().Should().BeFalse();
        user.CanPrintZReports().Should().BeFalse();
        user.CanAccessBackOffice().Should().BeFalse();
    }

    // ── Edge Case: Lockout ────────────────────────────────────────────────────

    [Fact]
    public async Task Waiter_IncorrectPin5Times_ShouldLockOut()
    {
        var vm = new PinLockViewModel(_env, _auth);

        // 5 failed attempts with wrong PIN "9999" (exhausting quota)
        for (int attempt = 0; attempt < 5; attempt++)
        {
            vm.ClearPin();
            foreach (char digit in "9999")
                await vm.AppendDigitAsync(digit.ToString());
        }

        // 6th attempt should now hit the lockout error
        vm.ClearPin();
        foreach (char digit in "9999")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeFalse();
        vm.ErrorMessage.Should().Contain("verrouillé");
    }
}

