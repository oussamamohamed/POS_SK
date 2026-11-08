using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class ProductAvailabilityTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public ProductAvailabilityTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<Guid> CreateProductAsync()
    {
        var admin = Client("Admin");
        var categories = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/categories");
        var categoryId = categories![0].GetProperty("id").GetString()!;
        var resp = await admin.PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest("Plat rupture " + Guid.NewGuid().ToString("N")[..6], categoryId, 12m, 10m, null, null, 1, false, null));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static AddOrderItemsRequest Add(Guid productId) =>
        new([new OrderItemInputDto(productId, "Plat", 1, 12m, 10m, "HOT_KITCHEN", null)]);

    private async Task<bool> IsAvailableInCatalogAsync(Guid id)
    {
        var products = await _factory.CreateClient().GetFromJsonAsync<List<JsonElement>>("/api/catalog/products");
        return products!.Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("isAvailable").GetBoolean();
    }

    [Fact]
    public async Task AnyAuthenticatedOperator_CanToggleAvailability()
    {
        var id = await CreateProductAsync();

        var off = await Client("Waiter").PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsAvailableInCatalogAsync(id)).Should().BeFalse();

        var on = await Client("Waiter").PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(true));
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsAvailableInCatalogAsync(id)).Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_CannotToggleAvailability()
    {
        var id = await CreateProductAsync();

        var resp = await _factory.CreateClient().PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownProduct_Returns404()
    {
        var resp = await Client("Waiter").PutAsJsonAsync($"/api/catalog/products/{Guid.NewGuid()}/availability", new SetAvailabilityRequest(false));

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnavailableProduct_IsRefusedOnTable_UntilAvailableAgain()
    {
        var id = await CreateProductAsync();
        var waiter = Client("Waiter");
        var table = "T_RUP_" + Random.Shared.Next(1000, 9999);
        (await waiter.PostAsJsonAsync($"/api/tables/{table}/open", new OpenTableRequest("Marc", 2, Guid.NewGuid()))).EnsureSuccessStatusCode();
        await waiter.PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));

        var refused = await waiter.PostAsJsonAsync($"/api/tables/{table}/items", Add(id));

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await refused.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("product_unavailable");
        body.GetProperty("productId").GetGuid().Should().Be(id);

        await waiter.PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(true));
        (await waiter.PostAsJsonAsync($"/api/tables/{table}/items", Add(id))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LineAlreadyOnOrder_IsKeptWhenProductRunsOut()
    {
        var id = await CreateProductAsync();
        var waiter = Client("Waiter");
        var table = "T_RUP_" + Random.Shared.Next(1000, 9999);
        (await waiter.PostAsJsonAsync($"/api/tables/{table}/open", new OpenTableRequest("Marc", 2, Guid.NewGuid()))).EnsureSuccessStatusCode();
        (await waiter.PostAsJsonAsync($"/api/tables/{table}/items", Add(id))).EnsureSuccessStatusCode();

        await waiter.PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));

        var order = await waiter.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{table}/order");
        order!.Lines.Should().HaveCount(1);
    }

    [Fact]
    public async Task EditingProduct_WithoutAvailability_KeepsTheStockOut()
    {
        var id = await CreateProductAsync();
        var admin = Client("Admin");
        await admin.PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));
        var categories = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/categories");
        var categoryId = categories![0].GetProperty("id").GetString()!;

        var edit = await admin.PutAsJsonAsync($"/api/catalog/products/{id}",
            new UpdateProductRequest("Renommé", categoryId, 13m, 10m, null, null, 1, null, null, false, null));

        edit.StatusCode.Should().Be(HttpStatusCode.OK);
        (await IsAvailableInCatalogAsync(id)).Should().BeFalse();
    }

    [Fact]
    public async Task OfflineSale_OfAProductThatRanOutSince_IsStillAcceptedBySync()
    {
        var id = await CreateProductAsync();
        var admin = Client("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, admin, "Caisse hors ligne");
        Guid orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<RestaurantPos.Infrastructure.Persistence.AppDbContext>();
            var order = new RestaurantPos.Domain.Entities.Order { TableNumber = "SYNC_RUP", Status = RestaurantPos.Domain.Entities.OrderStatus.Open };
            order.Items.Add(new RestaurantPos.Domain.Entities.OrderItem { OrderId = order.Id, ProductId = id, ProductName = "Plat", Quantity = 1, UnitPrice = RestaurantPos.Domain.ValueObjects.Money.FromCents(1200), TaxRatePercent = 10m });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
            orderId = order.Id;
        }
        // Le plat tombe en rupture avant que la caisse hors ligne ne synchronise sa vente (déjà faite et encaissée).
        await admin.PutAsJsonAsync($"/api/catalog/products/{id}/availability", new SetAvailabilityRequest(false));

        var response = await admin.PostAsJsonAsync("/api/sync/receipt",
            new RestaurantPos.Api.Endpoints.SyncReceiptDto(orderId, null, 1200, [new RestaurantPos.Api.Endpoints.SyncTenderDto(RestaurantPos.Domain.Entities.PaymentMethod.Cash, 1200, 1200)], paired.TerminalId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
