using FluentAssertions;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// Simulation of the FloorManager profile (P3).
/// Exercises: PIN auth → elevated domain permissions → cart void → back-office denial.
/// </summary>
public class FloorManagerProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeOperatorAuthenticationService _auth = new(SimulatedOperator.FloorManager());
    private readonly FakeLocalJournalService _journal = new();

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateFloorManager_WithValidPin_ShouldSucceed()
    {
        var vm = new PinLockViewModel(_env, _auth);

        foreach (char digit in "4444")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorRole.Should().Be(UserRole.FloorManager);
        vm.CurrentOperatorName.Should().Be("Isabelle Martin");
    }

    // ── Elevated Domain Permissions ───────────────────────────────────────────

    [Fact]
    public void FloorManager_DomainPermissions_ShouldAllowVoidAndReports()
    {
        var user = new User { Name = "Isabelle", Role = UserRole.FloorManager, PinHash = "h", PinSalt = "s" };
        user.CanVoidItems().Should().BeTrue();
        user.CanPrintZReports().Should().BeTrue();
        user.CanAccessBackOffice().Should().BeFalse("FloorManager cannot access back-office admin screens");
    }

    // ── Cart Void Operations ──────────────────────────────────────────────────

    [Fact]
    public void FloorManager_RemoveItemFromCart_ShouldAdjustTotal()
    {
        var vm = new PosTerminalViewModel(_env, _journal);
        var item1 = new OrderItem { ProductName = "Salade César", Quantity = 1, UnitPrice = Money.FromDecimal(9.50m), IsDispatched = false };
        var item2 = new OrderItem { ProductName = "Burger Rossini", Quantity = 1, UnitPrice = Money.FromDecimal(19.50m), IsDispatched = false };

        vm.CartItems.Add(item1);
        vm.CartItems.Add(item2);
        vm.RecalculateTotals();

        vm.TotalTtc.ToDecimal().Should().Be(29.00m);

        // Floor manager voids item1
        vm.CartItems.Remove(item1);
        vm.RecalculateTotals();

        vm.CartItems.Should().HaveCount(1);
        vm.CartItems.First().ProductName.Should().Be("Burger Rossini");
        vm.TotalTtc.ToDecimal().Should().Be(19.50m);
    }

    [Fact]
    public void FloorManager_ClearPendingItems_ShouldRetainDispatchedItems()
    {
        var vm = new PosTerminalViewModel(_env, _journal);
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Salade César",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(9.50m),
            IsDispatched = true   // already sent to kitchen
        });
        vm.CartItems.Add(new OrderItem
        {
            ProductName = "Dessert",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(7.50m),
            IsDispatched = false  // pending
        });
        vm.RecalculateTotals();

        vm.ClearCart();

        vm.CartItems.Should().HaveCount(1, "dispatched items must not be removed");
        vm.CartItems.First().IsDispatched.Should().BeTrue();
        vm.ConflictAlertBanner.Should().NotBeNullOrEmpty("a warning should be shown about retained dispatched items");
    }

    // ── Permission Denial ─────────────────────────────────────────────────────

    [Fact]
    public void FloorManager_CannotAccessBackOffice_DomainCheck()
    {
        var user = new User { Name = "Isabelle", Role = UserRole.FloorManager, PinHash = "h", PinSalt = "s" };
        user.CanAccessBackOffice().Should().BeFalse();
    }
}
