using FluentAssertions;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ProfileSimulations.Fakes;
using RestaurantPos.Client.Maui.ProfileSimulations.Helpers;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Simulations;

/// <summary>
/// Simulation of the Cashier profile (P2).
/// Exercises: PIN auth → full checkout → split bill → permission denial.
/// </summary>
public class CashierProfileSimulation
{
    private readonly FakePlatformEnvironmentService _env = new();
    private readonly FakeOperatorAuthenticationService _auth = new(SimulatedOperator.Cashier());
    private readonly FakeCheckoutPaymentService _checkout = new();

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task AuthenticateCashier_WithValidPin_ShouldSucceed()
    {
        var vm = new PinLockViewModel(_env, _auth);

        foreach (char digit in "2222")
            await vm.AppendDigitAsync(digit.ToString());

        vm.IsAuthenticated.Should().BeTrue();
        vm.CurrentOperatorRole.Should().Be(UserRole.Cashier);
        vm.CurrentOperatorName.Should().Be("Marc Lefebvre");
    }

    // ── Full Payment ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Cashier_FullCardPayment_ShouldCompleteCheckout()
    {
        var vm = new CheckoutViewModel(_env, _checkout);
        vm.Initialize(Guid.NewGuid(), 5000); // 50.00 EUR

        vm.SelectPaymentMethod(PaymentMethod.CreditCard);
        await vm.FinalizeCheckoutAsync();

        vm.IsCompleted.Should().BeTrue();
        vm.RemainingBalanceCents.Should().Be(0);
        vm.ReceiptNumber.Should().NotBeNullOrEmpty();
        _env.HapticCalls.Should().Contain(HapticFeedbackType.Success);
    }

    [Fact]
    public void Cashier_AddCashBill_ShouldComputeChangeCorrectly()
    {
        var vm = new CheckoutViewModel(_env);
        vm.Initialize(Guid.NewGuid(), 3500); // 35.00 EUR

        vm.AddCashFastBill(5000); // 50.00 EUR bill

        vm.RemainingBalanceCents.Should().Be(0);
        vm.ChangeDueCents.Should().Be(1500); // 15.00 EUR change
        vm.AppliedTenders.Should().HaveCount(1);
        vm.AppliedTenders.First().Method.Should().Be(PaymentMethod.Cash);
    }

    // ── Split Bill ────────────────────────────────────────────────────────────

    [Fact]
    public void Cashier_SplitBill3Ways_ShouldProduceThreeEqualPartitions()
    {
        var vm = new SplitBillViewModel(_env, _checkout);
        vm.Initialize(9000, 3); // 90.00 EUR ÷ 3

        vm.Partitions.Should().HaveCount(3);
        vm.Partitions.All(p => p.AmountCents == 3000).Should().BeTrue();
        vm.Partitions.Sum(p => p.AmountCents).Should().Be(9000);
    }

    [Fact]
    public void Cashier_SplitBill_WithRemainder_ShouldAddRemainderToLastPartition()
    {
        var vm = new SplitBillViewModel(_env, _checkout);
        vm.Initialize(1001, 3); // 10.01 EUR ÷ 3

        vm.Partitions.Should().HaveCount(3);
        vm.Partitions.Take(2).All(p => p.AmountCents == 333).Should().BeTrue();
        vm.Partitions.Last().AmountCents.Should().Be(335); // 333 + 333 + 335 = 1001
        vm.Partitions.Sum(p => p.AmountCents).Should().Be(1001);
    }

    [Fact]
    public void Cashier_IncreaseGuestsFor4Way_ShouldRecalculatePartitions()
    {
        var vm = new SplitBillViewModel(_env, _checkout);
        vm.Initialize(8000, 2); // Start with 2 guests
        vm.IncreaseGuests();    // Now 3 guests → but let's go to 4
        vm.IncreaseGuests();    // Now 4 guests

        vm.GuestsCount.Should().Be(4);
        vm.Partitions.Should().HaveCount(4);
        vm.Partitions.Sum(p => p.AmountCents).Should().Be(8000);
    }

    [Fact]
    public void Cashier_ComplexSplit_ShouldBalanceCorrectly()
    {
        // Scenario US3: An odd total (10.01 EUR) with a 5% discount applied, split 3 ways
        var order = new Order
        {
            GlobalDiscountType = DiscountType.Percentage,
            GlobalDiscountValue = 5.0m
        };
        order.Items.Add(new OrderItem
        {
            ProductName = "Plat Spécial",
            UnitPrice = Money.FromCents(1001),
            Quantity = 1
        });

        long discountedTotalCents = order.CalculateTotalTtc().AmountInCents;
        // 1001 * 0.95 = 950.95 -> 951 cents
        discountedTotalCents.Should().Be(951);

        var vm = new SplitBillViewModel(_env, _checkout);
        vm.Initialize(discountedTotalCents, 3);

        vm.Partitions.Should().HaveCount(3);
        vm.Partitions.Sum(p => p.AmountCents).Should().Be(discountedTotalCents);

        // Also test a non-divisible odd split (1001 cents directly split 3 ways)
        vm.Initialize(1001, 3);
        vm.Partitions.Should().HaveCount(3);
        vm.Partitions.Sum(p => p.AmountCents).Should().Be(1001);
        vm.Partitions[0].AmountCents.Should().Be(333);
        vm.Partitions[1].AmountCents.Should().Be(333);
        vm.Partitions[2].AmountCents.Should().Be(335);

        // Verify NF525 zero-loss penny balance across prime odd guest splits
        foreach (int guests in new[] { 2, 3, 5, 7, 11 })
        {
            vm.Initialize(discountedTotalCents, guests);
            vm.Partitions.Should().HaveCount(guests);
            vm.Partitions.Sum(p => p.AmountCents).Should().Be(discountedTotalCents);
        }
    }

    // ── Permission Denial ─────────────────────────────────────────────────────

    [Fact]
    public void Cashier_DomainPermissions_ShouldDenyBackOfficeAccess()
    {
        var user = new User { Name = "Marc", Role = UserRole.Cashier, PinHash = "h", PinSalt = "s" };
        user.CanAccessBackOffice().Should().BeFalse();
        user.CanVoidItems().Should().BeFalse();
        user.CanPrintZReports().Should().BeFalse();
    }
}
