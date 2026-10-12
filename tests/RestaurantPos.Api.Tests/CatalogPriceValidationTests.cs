using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Api;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Tests;

/// <summary>Un prix négatif est refusé en 400 à la création comme à la modification d'un article.</summary>
public class CatalogPriceValidationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public CatalogPriceValidationTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        return client;
    }

    private static string Expected => Texts.Get(new System.Globalization.CultureInfo("en"), "errors.product_price_negative");

    [Fact]
    public async Task CreateProduct_WithNegativePrice_Returns400WithLocalizedMessage()
    {
        var response = await AdminClient().PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest("Prix négatif", "DESSERTS", -1m, 10m, null, null, 1, false, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString().Should().Be(Expected);
    }

    [Fact]
    public async Task UpdateProduct_WithNegativePrice_Returns400_AndKeepsPrice()
    {
        var admin = AdminClient();
        var created = await (await admin.PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest("Prix à garder", "DESSERTS", 5m, 10m, null, null, 1, false, null)))
            .Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var response = await admin.PutAsJsonAsync($"/api/catalog/products/{id}",
            new UpdateProductRequest("Prix à garder", "DESSERTS", -0.01m, 10m, null, null, 1, true, true, false, null));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString().Should().Be(Expected);
        var after = await (await admin.GetAsync("/api/catalog/products")).Content.ReadFromJsonAsync<List<JsonElement>>();
        after!.Single(p => p.GetProperty("id").GetGuid() == id).GetProperty("price").GetDecimal().Should().Be(5m);
    }
}
