using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class StockEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public StockEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<Guid> IngredientAsync(string name, long? low = null)
    {
        var resp = await Client("Admin").PostAsJsonAsync("/api/stock/ingredients",
            new { name, unit = "g", unitCostMilliCents = 1000L, isPreparation = false, allergenMask = 0, lowStockMilli = low });
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<long> StockOfAsync(Guid ingredient)
    {
        var all = await Client("Admin").GetFromJsonAsync<List<JsonElement>>("/api/stock/ingredients");
        return all!.Single(i => i.GetProperty("id").GetGuid() == ingredient).GetProperty("stockMilli").GetInt64();
    }

    private async Task<Guid> DishWithRecipeAsync(Guid ingredient, long qty)
    {
        var admin = Client("Admin");
        var cats = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/categories");
        var created = await admin.PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest(Unique("Plat "), cats![0].GetProperty("id").GetString()!, 11m, 10m, null, null, 1, false, null, 1m));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await admin.PutAsJsonAsync($"/api/catalog/products/{id}/recipe", new { lines = new[] { new { ingredientId = ingredient, quantityMilli = qty } } })).EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>Encaisse 3 portions par l'API réelle (poste appairé) et renvoie le client et le reçu.</summary>
    private async Task<(HttpClient Client, Guid ReceiptId)> SellAsync(Guid product, int quantity)
    {
        var client = Client("Admin");
        await DeviceTestHelper.PairAsync(_factory, client);
        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { TableNumber = "S" + Unique(""), Status = OrderStatus.Open };
            order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = product, ProductName = "Plat", Quantity = quantity, UnitPrice = Money.FromCents(1100), TaxRatePercent = 10m });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }
        var pay = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(orderId, null, null, [new TenderItemRequest(PaymentMethod.Cash, 11m * quantity, 11m * quantity, 0m)]));
        pay.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = _factory.Services.CreateScope();
        var receipt = verify.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.OrderId == orderId && r.VoidedReceiptId == null);
        return (client, receipt.Id);
    }

    [Fact]
    public async Task Waiter_CannotUseStockEndpoints()
    {
        var waiter = Client("Waiter");
        (await waiter.GetAsync("/api/stock/alerts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = Guid.NewGuid(), quantityMilli = 1, unitCostMilliCents = 1 })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ReceiptCountLoss_AndFilteredHistory()
    {
        var flour = await IngredientAsync(Unique("Farine "), low: 5_000_000);
        var other = await IngredientAsync(Unique("Sucre "));
        var admin = Client("Admin");

        (await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = flour, quantityMilli = 10_000_000L, unitCostMilliCents = 1200L, note = "livraison" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await admin.PostAsJsonAsync("/api/stock/losses", new { ingredientId = flour, quantityMilli = 1_000_000L, note = "casse" })).StatusCode.Should().Be(HttpStatusCode.Created);
        await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = other, quantityMilli = 1L, unitCostMilliCents = 1L });
        var count = await admin.PostAsJsonAsync("/api/stock/counts", new { ingredientId = flour, countedMilli = 3_000_000L });
        var body = await count.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("varianceMilli").GetInt64().Should().Be(-6_000_000);
        (await StockOfAsync(flour)).Should().Be(3_000_000);
        var history = await admin.GetFromJsonAsync<List<JsonElement>>($"/api/stock/movements?ingredientId={flour}");
        history!.Select(m => m.GetProperty("kind").GetString()).Should().BeEquivalentTo(["receipt", "loss", "count"]);
        var alerts = await admin.GetFromJsonAsync<JsonElement>("/api/stock/alerts");
        alerts.GetProperty("lowStock").EnumerateArray().Should().Contain(a => a.GetProperty("ingredientId").GetGuid() == flour);
        (await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = flour, quantityMilli = 0L, unitCostMilliCents = 1L })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sale_LowersStock_AndVoidRestocksOnlyWhenAsked()
    {
        var beef = await IngredientAsync(Unique("Boeuf "));
        await Client("Admin").PostAsJsonAsync("/api/stock/receipts", new { ingredientId = beef, quantityMilli = 5_000_000L, unitCostMilliCents = 1500L });
        var dish = await DishWithRecipeAsync(beef, 200_000);

        var (client, receiptId) = await SellAsync(dish, 3);
        (await StockOfAsync(beef)).Should().Be(5_000_000 - 600_000);

        var noRestock = await client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", new VoidReceiptRequest { OperatorId = Guid.NewGuid() });
        noRestock.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StockOfAsync(beef)).Should().Be(4_400_000);

        var (client2, receipt2) = await SellAsync(dish, 1);
        (await StockOfAsync(beef)).Should().Be(4_200_000);
        var restock = await client2.PostAsJsonAsync($"/api/checkout/void/{receipt2}", new VoidReceiptRequest { OperatorId = Guid.NewGuid(), Restock = true });
        restock.StatusCode.Should().Be(HttpStatusCode.OK);
        (await StockOfAsync(beef)).Should().Be(4_400_000);
    }

    [Fact]
    public async Task ConcurrentSales_OfTheSameIngredient_KeepAConsistentSum()
    {
        var oil = await IngredientAsync(Unique("Huile "));
        await Client("Admin").PostAsJsonAsync("/api/stock/receipts", new { ingredientId = oil, quantityMilli = 10_000_000L, unitCostMilliCents = 500L });
        var dish = await DishWithRecipeAsync(oil, 100_000);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => SellAsync(dish, 2)));

        (await StockOfAsync(oil)).Should().Be(10_000_000 - 4 * 2 * 100_000);
    }

    [Fact]
    public async Task AutoUnavailable_FollowsTheStock_AndNeverTouchesAManualOutage()
    {
        var cream = await IngredientAsync(Unique("Creme "));
        var dish = await DishWithRecipeAsync(cream, 100_000);
        var admin = Client("Admin");
        await admin.PutAsJsonAsync("/api/stock/settings", new { autoUnavailableOnStockOut = true, printAllergensOnKitchenTicket = false, allergenDisclaimer = (string?)null });
        await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = cream, quantityMilli = 100_000L, unitCostMilliCents = 1000L });

        await SellAsync(dish, 1); // stock à 0

        async Task<JsonElement> ProductAsync() => (await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/products"))!.Single(p => p.GetProperty("id").GetGuid() == dish);
        (await ProductAsync()).GetProperty("isAvailable").GetBoolean().Should().BeFalse();

        var restock = await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = cream, quantityMilli = 500_000L, unitCostMilliCents = 1000L });
        restock.StatusCode.Should().Be(HttpStatusCode.Created, await restock.Content.ReadAsStringAsync());
        (await StockOfAsync(cream)).Should().Be(500_000);
        (await ProductAsync()).GetProperty("isAvailable").GetBoolean().Should().BeTrue();

        // Rupture manuelle : la réception ne la lève pas.
        await admin.PutAsJsonAsync($"/api/catalog/products/{dish}/availability", new SetAvailabilityRequest(false));
        await admin.PostAsJsonAsync("/api/stock/receipts", new { ingredientId = cream, quantityMilli = 1_000L, unitCostMilliCents = 1000L });
        (await ProductAsync()).GetProperty("isAvailable").GetBoolean().Should().BeFalse();
        await admin.PutAsJsonAsync("/api/stock/settings", new { autoUnavailableOnStockOut = false, printAllergensOnKitchenTicket = false, allergenDisclaimer = (string?)null });
    }
}
