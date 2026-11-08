using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class ProductAllergensTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public ProductAllergensTests(PosApiApplicationFactory factory) => _factory = factory;

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
            new CreateProductRequest("Plat allergènes " + Guid.NewGuid().ToString("N")[..6], categoryId, 9m, 10m, null, null, 1, false, null, 3m));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> ReadProductAsync(Guid id, string? role = null)
    {
        var client = role is null ? _factory.CreateClient() : Client(role);
        var products = await client.GetFromJsonAsync<List<JsonElement>>("/api/catalog/products");
        return products!.Single(p => p.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task NewProduct_HasAllergensNotDeclared()
    {
        var id = await CreateProductAsync();

        var product = await ReadProductAsync(id);

        product.GetProperty("allergensDeclared").GetBoolean().Should().BeFalse();
        product.GetProperty("allergenMask").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Manager_CanSetAllergens_AndEveryoneReadsThem_WithoutTheCost()
    {
        var id = await CreateProductAsync();
        var mask = (1 << 0) | (1 << 6); // gluten + lait

        var put = await Client("Admin").PutAsJsonAsync($"/api/catalog/products/{id}/allergens", new SetAllergensRequest(true, mask, true));

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var waiterView = await ReadProductAsync(id, "Waiter");
        waiterView.GetProperty("allergensDeclared").GetBoolean().Should().BeTrue();
        waiterView.GetProperty("allergenMask").GetInt32().Should().Be(mask);
        waiterView.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Waiter_CannotSetAllergens()
    {
        var id = await CreateProductAsync();

        var put = await Client("Waiter").PutAsJsonAsync($"/api/catalog/products/{id}/allergens", new SetAllergensRequest(true, 1, true));

        put.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(16384)]
    public async Task OutOfRangeMask_IsRejected(int mask)
    {
        var id = await CreateProductAsync();

        var put = await Client("Admin").PutAsJsonAsync($"/api/catalog/products/{id}/allergens", new SetAllergensRequest(true, mask, true));

        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Undeclaring_ClearsTheMask()
    {
        var id = await CreateProductAsync();
        await Client("Admin").PutAsJsonAsync($"/api/catalog/products/{id}/allergens", new SetAllergensRequest(true, 5, true));

        await Client("Admin").PutAsJsonAsync($"/api/catalog/products/{id}/allergens", new SetAllergensRequest(false, 5, false));

        var product = await ReadProductAsync(id);
        product.GetProperty("allergensDeclared").GetBoolean().Should().BeFalse();
        product.GetProperty("allergenMask").GetInt32().Should().Be(0);
    }
}
