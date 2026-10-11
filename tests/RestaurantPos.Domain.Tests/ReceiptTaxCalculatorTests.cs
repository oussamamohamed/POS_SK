using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class ReceiptTaxCalculatorTests
{
    private static Order TwoRates(OrderDestination destination = OrderDestination.EatIn)
    {
        var order = new Order { TableNumber = "T1", Destination = destination };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(1000), TaxRatePercent = 10m });
        order.Items.Add(new OrderItem { ProductName = "B", Quantity = 1, UnitPrice = Money.FromCents(2000), TaxRatePercent = 20m });
        return order;
    }

    [Fact]
    public void FullPayment_IsTheOrderBreakdown()
    {
        var tax = ReceiptTaxCalculator.Compute(TwoRates(), 3000);
        tax.TtcCents.Should().Be(3000);
        tax.HtCents.Should().Be(2576);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((10m, 91L), (20m, 333L));
    }

    [Fact]
    public void PartialPayment_SpreadsTheAmountOnTheRates_AndStaysExact()
    {
        var tax = ReceiptTaxCalculator.Compute(TwoRates(), 1000); // parts exactes 333,33 / 666,67 -> 333 / 667
        tax.TtcCents.Should().Be(1000);
        (tax.TtcCents - tax.HtCents).Should().Be(tax.VatByRate.Sum(v => v.Value));
        tax.VatByRate.Should().OnlyContain(v => v.Value >= 0);
    }

    [Fact]
    public void ThePaidAmountIsClampedToTheOrderTotal_AndToZero()
    {
        ReceiptTaxCalculator.Compute(TwoRates(), 99_999).TtcCents.Should().Be(3000);
        ReceiptTaxCalculator.Compute(TwoRates(), -10).TtcCents.Should().Be(0);
    }

    [Fact]
    public void ZeroTotalOrder_GivesZeroVatPerRate()
    {
        var order = new Order { TableNumber = "T1" };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(500), TaxRatePercent = 10m, IsComp = true });
        var tax = ReceiptTaxCalculator.Compute(order, 0);
        tax.TtcCents.Should().Be(0);
        tax.HtCents.Should().Be(0);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((10m, 0L));
    }

    [Fact]
    public void TakeawayReceipt_IsDeclaredAtTheReducedRate()
    {
        var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(1000), TaxRatePercent = 10m, TaxRateTakeawayPercent = 5.5m });
        var tax = ReceiptTaxCalculator.Compute(order, 1000);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((5.5m, 52L));
        tax.HtCents.Should().Be(948);
    }
}
