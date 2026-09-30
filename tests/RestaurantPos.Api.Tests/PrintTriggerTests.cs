using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintTriggerTests
{
    private static async Task<HttpClient> CashierAsync(PosApiApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        await DeviceTestHelper.PairAsync(factory, client);
        return client;
    }

    /// <summary>Garantit une imprimante RECEIPT et une HOT_KITCHEN actives (le seed peut ne pas tourner en Testing).</summary>
    private static void EnsurePrinters(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printers = db.PrinterConfigurations.ToList();
        if (!printers.Any(p => p.IsActive && p.AssignedStationIds.Contains("RECEIPT")))
            db.PrinterConfigurations.Add(new PrinterConfiguration { Name = "Caisse", IpAddress = "10.0.0.1", AssignedStationIds = ["RECEIPT"] });
        if (!printers.Any(p => p.IsActive && p.AssignedStationIds.Contains("HOT_KITCHEN")))
            db.PrinterConfigurations.Add(new PrinterConfiguration { Name = "Cuisine", IpAddress = "10.0.0.2", AssignedStationIds = ["HOT_KITCHEN"] });
        db.SaveChanges();
    }

    private static List<PrintJob> Jobs(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking().ToList();
    }

    [Theory]
    [InlineData(false, PrintJobKind.PickupVoucher)]
    [InlineData(true, PrintJobKind.Receipt)]
    public async Task CounterCheckout_QueuesOneJob_AndReportsPrintQueued(bool fiscal, PrintJobKind expected)
    {
        using var factory = new PosApiApplicationFactory();
        var client = await CashierAsync(factory);
        EnsurePrinters(factory);
        var order = await (await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("T01", OrderDestination.Takeaway))).Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        (await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(
        [
            new(Guid.NewGuid(), "Sandwich Poulet", 1, 6.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        ]))).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/orders/counter/checkout", new CounterCheckoutRequest(
            order!.OrderId, null, OrderDestination.Takeaway, null, null, 0m, fiscal, null, [new CounterPaymentTender(PaymentMethod.CreditCard, 6.50m, 6.50m)]));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printQueued").GetBoolean().Should().BeTrue();
        Jobs(factory).Should().ContainSingle().Which.Kind.Should().Be(expected);
    }

    private static void SeedTable(PosApiApplicationFactory factory, string table, decimal price, string? station = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order
        {
            TableNumber = table,
            Status = OrderStatus.Open,
            Items = { new OrderItem { ProductId = Guid.NewGuid(), ProductName = "Menu Test", Quantity = 1, UnitPrice = Money.FromDecimal(price, "EUR"), TaxRatePercent = 10, PreparationStationId = station } }
        };
        db.Orders.Add(order);
        db.DiningTables.Add(new DiningTable { TableNumber = table, Status = TableStatus.Occupied, ActiveOrderId = order.Id, CoversCount = 2 });
        db.SaveChanges();
    }

    private static async Task<JsonElement> PayAsync(HttpClient client, string table, decimal amount, bool requestReceipt)
    {
        var res = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            Guid.Empty, table, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, amount, amount, 0m)], null, requestReceipt));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task TablePay_ReceiptOnlyWhenRequestedAndFullyPaid()
    {
        using var factory = new PosApiApplicationFactory();
        var client = await CashierAsync(factory);
        EnsurePrinters(factory);
        SeedTable(factory, "T71", 20m);
        SeedTable(factory, "T72", 5m);

        (await PayAsync(client, "T71", 10m, true)).GetProperty("printQueued").GetBoolean().Should().BeFalse();
        Jobs(factory).Should().BeEmpty();

        (await PayAsync(client, "T71", 10m, true)).GetProperty("printQueued").GetBoolean().Should().BeTrue();
        Jobs(factory).Should().ContainSingle().Which.Kind.Should().Be(PrintJobKind.Receipt);

        (await PayAsync(client, "T72", 5m, false)).GetProperty("printQueued").GetBoolean().Should().BeFalse();
        Jobs(factory).Should().HaveCount(1);
    }

    [Fact]
    public async Task Dispatch_QueuesKitchenTicketOnStationPrinter()
    {
        using var factory = new PosApiApplicationFactory();
        var client = await CashierAsync(factory);
        EnsurePrinters(factory);
        SeedTable(factory, "T05", 12m, "HOT_KITCHEN");

        (await client.PostAsync("/api/tables/T05/dispatch", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        int hotPrinters;
        using (var scope = factory.Services.CreateScope())
            hotPrinters = scope.ServiceProvider.GetRequiredService<AppDbContext>().PrinterConfigurations.AsEnumerable().Count(p => p.IsActive && p.AssignedStationIds.Contains("HOT_KITCHEN"));
        var jobs = Jobs(factory);
        jobs.Should().HaveCount(hotPrinters).And.OnlyContain(j => j.Kind == PrintJobKind.KitchenTicket);
    }
}
