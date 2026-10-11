using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class OrderTaxBreakdownTests
{
    private static readonly decimal[] LineDiscounts = [10m, 33.33m, 100m];
    private static readonly decimal[] GlobalPercents = [5m, 10m, 33.33m, 50m, 100m];

    private static OrderItem Line(long unitCents, int qty, decimal rate, decimal? takeaway = null, decimal discountPercent = 0, bool comp = false, long extraCents = 0) => new()
    {
        ProductName = "Produit",
        Quantity = qty,
        UnitPrice = Money.FromCents(unitCents),
        TaxRatePercent = rate,
        TaxRateTakeawayPercent = takeaway,
        DiscountPercent = discountPercent,
        IsComp = comp,
        ModifiersPriceExtra = Money.FromCents(extraCents),
    };

    private static Order Build(OrderDestination destination, DiscountType? discount, decimal discountValue, params OrderItem[] items)
    {
        var order = new Order { TableNumber = "T1", Destination = destination, GlobalDiscountType = discount, GlobalDiscountValue = discountValue };
        order.Items.AddRange(items);
        return order;
    }

    private static void AssertInvariants(Order order, string context)
    {
        var breakdown = order.CalculateTaxBreakdown();
        long total = order.TotalTtc.AmountInCents;
        breakdown.Sum(b => b.TaxableBaseCents + b.TaxAmountCents).Should().Be(total, context);
        order.TotalHt.AmountInCents.Should().Be(breakdown.Sum(b => b.TaxableBaseCents), context);
        (order.TotalTtc.AmountInCents - order.TotalHt.AmountInCents).Should().Be(breakdown.Sum(b => b.TaxAmountCents), context);
        breakdown.Select(b => b.TaxRatePercent).Should().BeInAscendingOrder(context);
        breakdown.Select(b => b.TaxRatePercent).Should().OnlyHaveUniqueItems(context);
        breakdown.Should().OnlyContain(b => b.TaxableBaseCents >= 0 && b.TaxAmountCents >= 0, context);
        order.DiscountAmount.AmountInCents.Should().Be(order.Subtotal.AmountInCents - total, context);
        order.DiscountAmount.AmountInCents.Should().BeGreaterThanOrEqualTo(0, context);
    }

    [Fact]
    public void EatIn_TwoRates_NoDiscount()
    {
        var order = Build(OrderDestination.EatIn, null, 0, Line(1000, 1, 10m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((10m, 909L, 91L), (20m, 1667L, 333L));
        order.Subtotal.AmountInCents.Should().Be(3000);
        order.DiscountAmount.AmountInCents.Should().Be(0);
        order.TotalHt.AmountInCents.Should().Be(2576);
        AssertInvariants(order, nameof(EatIn_TwoRates_NoDiscount));
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 10)]
    [InlineData(DiscountType.FixedAmount, 3)]
    public void GlobalDiscountOf300Cents_IsSpreadOnEveryRate(DiscountType type, int value)
    {
        var order = Build(OrderDestination.EatIn, type, value, Line(1000, 1, 10m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((10m, 818L, 82L), (20m, 1500L, 300L));
        order.TotalTtc.AmountInCents.Should().Be(2700);
        order.DiscountAmount.AmountInCents.Should().Be(300);
        order.TotalHt.AmountInCents.Should().Be(2318);
        AssertInvariants(order, $"{type} {value}");
    }

    [Fact]
    public void Takeaway_UsesTheEffectiveRate_AndOnlyOnTheLinesThatHaveOne()
    {
        var order = Build(OrderDestination.Takeaway, null, 0, Line(1000, 1, 10m, takeaway: 5.5m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((5.5m, 948L, 52L), (20m, 1667L, 333L));
        order.TotalHt.AmountInCents.Should().Be(2615);
        AssertInvariants(order, nameof(Takeaway_UsesTheEffectiveRate_AndOnlyOnTheLinesThatHaveOne));
    }

    [Fact]
    public void EatIn_IgnoresTheTakeawayRate()
    {
        var order = Build(OrderDestination.EatIn, null, 0, Line(1000, 1, 10m, takeaway: 5.5m));
        order.CalculateTaxBreakdown().Single().TaxRatePercent.Should().Be(10m);
    }

    [Fact]
    public void LinesWithTheSameEffectiveRate_AreGroupedEvenIfTheyComeFromDifferentBaseRates()
    {
        var order = Build(OrderDestination.Takeaway, null, 0, Line(1000, 1, 10m, takeaway: 5.5m), Line(500, 1, 5.5m));
        var b = order.CalculateTaxBreakdown();
        b.Should().ContainSingle();
        b[0].TaxRatePercent.Should().Be(5.5m);
        (b[0].TaxableBaseCents + b[0].TaxAmountCents).Should().Be(1500);
    }

    [Fact]
    public void FixedDiscountAboveTheTotal_ClampsEverythingToZero()
    {
        var order = Build(OrderDestination.EatIn, DiscountType.FixedAmount, 15, Line(1000, 1, 10m));
        order.TotalTtc.AmountInCents.Should().Be(0);
        order.DiscountAmount.AmountInCents.Should().Be(1000);
        order.CalculateTaxBreakdown().Single().Should().Match<TaxBreakdownItem>(x => x.TaxableBaseCents == 0 && x.TaxAmountCents == 0);
        AssertInvariants(order, nameof(FixedDiscountAboveTheTotal_ClampsEverythingToZero));
    }

    [Fact]
    public void FullComp_AndHundredPercentDiscount_GiveAZeroOrder()
    {
        var comp = Build(OrderDestination.EatIn, null, 0, Line(500, 2, 10m, comp: true));
        comp.TotalTtc.AmountInCents.Should().Be(0);
        AssertInvariants(comp, "comp");
        var full = Build(OrderDestination.EatIn, DiscountType.Percentage, 100, Line(500, 2, 10m), Line(300, 1, 20m));
        full.TotalTtc.AmountInCents.Should().Be(0);
        full.DiscountAmount.AmountInCents.Should().Be(1300);
        AssertInvariants(full, "100%");
    }

    [Fact]
    public void LineDiscountsModifiersAndQuantities_FeedTheGroups()
    {
        var order = Build(
            OrderDestination.EatIn, DiscountType.Percentage, 33.33m,
            Line(1000, 2, 10m, discountPercent: 10m), Line(500, 1, 10m, comp: true), Line(300, 3, 20m, extraCents: 50), Line(1, 1, 5.5m), Line(199, 7, 2.1m));
        AssertInvariants(order, nameof(LineDiscountsModifiersAndQuantities_FeedTheGroups));
    }

    [Fact]
    public void RandomOrders_KeepEveryInvariant()
    {
        var random = new Random(20261012);
        decimal[] rates = [0m, 2.1m, 5.5m, 10m, 20m];
        for (int i = 0; i < 2000; i++)
        {
            var items = Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
            {
                decimal baseRate = rates[random.Next(rates.Length)];
                decimal? takeaway = random.Next(3) == 0 ? rates[random.Next(rates.Length)] : null;
                return Line(random.Next(1, 4000), random.Next(1, 6), baseRate, takeaway, random.Next(4) == 0 ? LineDiscounts[random.Next(3)] : 0m, random.Next(12) == 0, random.Next(3) == 0 ? random.Next(0, 300) : 0);
            }).ToArray();
            int kind = random.Next(3);
            DiscountType? type = kind == 0 ? null : kind == 1 ? DiscountType.Percentage : DiscountType.FixedAmount;
            decimal value = kind == 1 ? GlobalPercents[random.Next(5)] : kind == 2 ? random.Next(1, 8000) / 100m : 0m;
            var order = Build(random.Next(2) == 0 ? OrderDestination.EatIn : OrderDestination.Takeaway, type, value, items);
            AssertInvariants(order, $"random case {i}");
        }
    }
}
