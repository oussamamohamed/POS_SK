using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class TakeawayTaxCalculationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public TakeawayTaxCalculationTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        return client;
    }

    [Fact]
    public void Order_DynamicTaxBreakdown_RecalculatesBetweenEatInAndTakeaway()
    {
        // Arrange: 1 Sealed Drink (10% EatIn, 5.5% Takeaway), 1 Hot Dish (10% EatIn, 10% Takeaway), 1 Wine (20% both)
        var order = new Order
        {
            TableNumber = "Comptoir",
            Destination = OrderDestination.EatIn
        };

        var drink = new OrderItem
        {
            ProductName = "Canette Soda 33cl",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(3.00m, "EUR"),
            TaxRatePercent = 10.0m,
            TaxRateTakeawayPercent = 5.5m,
            IsFoodVoucherEligible = true
        };

        var dish = new OrderItem
        {
            ProductName = "Burger Maison",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(12.00m, "EUR"),
            TaxRatePercent = 10.0m,
            TaxRateTakeawayPercent = 10.0m,
            IsFoodVoucherEligible = true
        };

        var wine = new OrderItem
        {
            ProductName = "Verre de Vin Rouge",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(5.00m, "EUR"),
            TaxRatePercent = 20.0m,
            TaxRateTakeawayPercent = 20.0m,
            IsFoodVoucherEligible = false
        };

        order.Items.AddRange([drink, dish, wine]);

        // Total TTC: 3.00 + 12.00 + 5.00 = 20.00 € (2000 cents)
        order.TotalTtc.AmountInCents.Should().Be(2000);

        // Act 1: In EatIn mode
        var breakdownEatIn = order.CalculateTaxBreakdown();
        // 10% group: 3.00 + 12.00 = 15.00 € (1500 cents)
        // 20% group: 5.00 € (500 cents)
        breakdownEatIn.Should().HaveCount(2);

        var vat10EatIn = breakdownEatIn.First(b => b.TaxRatePercent == 10.0m);
        (vat10EatIn.TaxableBaseCents + vat10EatIn.TaxAmountCents).Should().Be(1500);

        var vat20EatIn = breakdownEatIn.First(b => b.TaxRatePercent == 20.0m);
        (vat20EatIn.TaxableBaseCents + vat20EatIn.TaxAmountCents).Should().Be(500);

        // Check HT + VAT = TTC invariant (0-centime rounding error)
        breakdownEatIn.Sum(b => b.TaxableBaseCents + b.TaxAmountCents).Should().Be(order.TotalTtc.AmountInCents);

        // Act 2: Switch to Takeaway mode
        order.Destination = OrderDestination.Takeaway;
        var breakdownTakeaway = order.CalculateTaxBreakdown();

        // Takeaway rates: Drink is 5.5% (300 cents), Dish is 10% (1200 cents), Wine is 20% (500 cents)
        breakdownTakeaway.Should().HaveCount(3);

        var vat55 = breakdownTakeaway.First(b => b.TaxRatePercent == 5.5m);
        (vat55.TaxableBaseCents + vat55.TaxAmountCents).Should().Be(300);

        var vat10Takeaway = breakdownTakeaway.First(b => b.TaxRatePercent == 10.0m);
        (vat10Takeaway.TaxableBaseCents + vat10Takeaway.TaxAmountCents).Should().Be(1200);

        var vat20Takeaway = breakdownTakeaway.First(b => b.TaxRatePercent == 20.0m);
        (vat20Takeaway.TaxableBaseCents + vat20Takeaway.TaxAmountCents).Should().Be(500);

        // Check HT + VAT = TTC invariant
        breakdownTakeaway.Sum(b => b.TaxableBaseCents + b.TaxAmountCents).Should().Be(order.TotalTtc.AmountInCents);
    }

    [Fact]
    public async Task SwitchDestinationEndpoint_DynamicallyUpdatesTotalsAndTaxRate()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Open direct counter order (defaults to Takeaway)
        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_TAX", OrderDestination.Takeaway));
        var initialOrder = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        initialOrder.Should().NotBeNull();

        // 2. Add an item with differentiated Takeaway tax rate (5.5% takeaway, 10% eat-in)
        await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Eau Minérale 50cl", 2, 2.50m, 10.0m, null, null, CourseType.Direct, 0m, 5.5m, true)
        }));

        // 3. Check order in Takeaway mode
        var checkTakeaway = await (await client.GetAsync($"/api/tables/Comptoir/order")).Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        checkTakeaway.Should().NotBeNull();
        checkTakeaway!.Destination.Should().Be(OrderDestination.Takeaway);
        // Total TTC is 5.00 € (500 cents). In 5.5%, HT = round(500 / 1.055) = 474 cents = 4.74 €, VAT = 0.26 €
        checkTakeaway.TotalTtcAmount.Should().Be(5.00m);
        checkTakeaway.TotalHtAmount.Should().Be(4.74m);
        checkTakeaway.TotalVatAmount.Should().Be(0.26m);

        // 4. Switch destination to EatIn
        var switchResponse = await client.PostAsJsonAsync($"/api/orders/{initialOrder!.OrderId}/destination", new SwitchDestinationRequest(OrderDestination.EatIn));
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkEatIn = await switchResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        checkEatIn.Should().NotBeNull();
        checkEatIn!.Destination.Should().Be(OrderDestination.EatIn);
        // In 10%, HT = round(500 / 1.10) = 455 cents = 4.55 €, VAT = 0.45 €
        checkEatIn.TotalTtcAmount.Should().Be(5.00m);
        checkEatIn.TotalHtAmount.Should().Be(4.55m);
        checkEatIn.TotalVatAmount.Should().Be(0.45m);
    }
}
