using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class ProductTaxCalculationTests
{
    [Fact]
    public void OrderCalculateTaxBreakdownCalculatesMultiVatSplitWithoutRoundingErrors()
    {
        // Arrange
        var order = new Order { TableNumber = "Table 12" };

        // Item 1: 2 x Burger (10% VAT) @ 16.50 EUR = 33.00 EUR TTC
        order.Items.Add(new OrderItem
        {
            ProductName = "Burger Maison",
            UnitPrice = Money.FromDecimal(16.50m),
            Quantity = 2,
            TaxRatePercent = 10.0m
        });

        // Item 2: 1 x Bière (20% VAT) @ 6.00 EUR = 6.00 EUR TTC
        order.Items.Add(new OrderItem
        {
            ProductName = "Bière Blonde",
            UnitPrice = Money.FromDecimal(6.00m),
            Quantity = 1,
            TaxRatePercent = 20.0m
        });

        // Item 3: 2 x Eau (5.5% VAT) @ 3.00 EUR = 6.00 EUR TTC
        order.Items.Add(new OrderItem
        {
            ProductName = "Bouteille Eau",
            UnitPrice = Money.FromDecimal(3.00m),
            Quantity = 2,
            TaxRatePercent = 5.5m
        });

        // Act
        var totalTtc = order.CalculateTotalTtc();
        var taxBreakdowns = order.CalculateTaxBreakdown();

        // Assert
        totalTtc.AmountInCents.Should().Be(4500); // 45.00 EUR
        taxBreakdowns.Should().HaveCount(3);

        var vat10 = taxBreakdowns.First(t => t.TaxRatePercent == 10.0m);
        vat10.TaxableBaseCents.Should().Be(3000); // 3300 / 1.10 = 3000
        vat10.TaxAmountCents.Should().Be(300);     // 300 cents (3.00 EUR)

        var vat20 = taxBreakdowns.First(t => t.TaxRatePercent == 20.0m);
        vat20.TaxableBaseCents.Should().Be(500);  // 600 / 1.20 = 500
        vat20.TaxAmountCents.Should().Be(100);    // 100 cents (1.00 EUR)
    }
}
