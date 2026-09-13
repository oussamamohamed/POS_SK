using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class TakeawayPackagingTests
{
    [Fact]
    public void Order_TakeawayPackagingChecklist_IdentifiesRequiredBagsAndContainers()
    {
        var order = new Order
        {
            TableNumber = "Comptoir",
            Destination = OrderDestination.Takeaway,
            PickupNumber = "#A-05"
        };

        var burger = new OrderItem
        {
            ProductName = "Menu Burger Gourmand",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(15.00m, "EUR"),
            TaxRatePercent = 10m,
            TaxRateTakeawayPercent = 10m,
            PreparationStationId = "CUISINE",
            SelectedModifiers = ["Sans Oignon", "Consigne Boîte Inox (+2.00 €)"],
            ModifiersPriceExtra = Money.FromDecimal(2.00m, "EUR")
        };

        var soda = new OrderItem
        {
            ProductName = "Soda 33cl",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(3.00m, "EUR"),
            TaxRatePercent = 10m,
            TaxRateTakeawayPercent = 5.5m,
            PreparationStationId = "BAR"
        };

        order.Items.AddRange([burger, soda]);

        order.Destination.Should().Be(OrderDestination.Takeaway);
        order.PickupNumber.Should().Be("#A-05");

        // Packaging requirements
        int totalItemCount = order.Items.Sum(i => i.Quantity);
        totalItemCount.Should().Be(4);

        bool hasReusableDeposit = order.Items.Any(i => i.SelectedModifiers.Any(m => m.Contains("Consigne", StringComparison.OrdinalIgnoreCase)));
        hasReusableDeposit.Should().BeTrue();

        // Total deposit amount
        decimal depositTotal = order.Items
            .Where(i => i.SelectedModifiers.Any(m => m.Contains("Consigne", StringComparison.OrdinalIgnoreCase)))
            .Sum(i => i.ModifiersPriceExtra.ToDecimal() * i.Quantity);

        depositTotal.Should().Be(4.00m);
    }
}
