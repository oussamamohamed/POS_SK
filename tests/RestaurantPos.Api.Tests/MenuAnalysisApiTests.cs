using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Api;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Tests;

/// <summary>Coût de revient (champ facultatif de l'article) et endpoint d'analyse du menu : réservés aux responsables.</summary>
public class MenuAnalysisApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public MenuAnalysisApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientAs(string? role)
    {
        var client = _factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task<JsonElement?> FindProduct(HttpClient client, Guid id)
    {
        var all = await (await client.GetAsync("/api/catalog/products")).Content.ReadFromJsonAsync<List<JsonElement>>();
        return all!.Single(p => p.GetProperty("id").GetGuid() == id);
    }

    private static async Task<Guid> CreateWithCost(HttpClient admin, decimal? cost)
    {
        var created = await (await admin.PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest("Plat coûteux " + Guid.NewGuid().ToString("N")[..6], "DESSERTS", 10m, 10m, null, null, 1, false, null, cost)))
            .Content.ReadFromJsonAsync<JsonElement>();
        return created.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Cost_is_served_to_managers_only_and_can_be_kept_or_cleared()
    {
        var admin = ClientAs("Admin");
        var id = await CreateWithCost(admin, 3.50m);

        (await FindProduct(admin, id))!.Value.GetProperty("cost").GetDecimal().Should().Be(3.50m);
        (await FindProduct(ClientAs(null), id))!.Value.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null);

        // PUT sans coût : conservé.
        await admin.PutAsJsonAsync($"/api/catalog/products/{id}",
            new UpdateProductRequest("Plat modifié", "DESSERTS", 10m, 10m, null, null, 1, true, true, false, null));
        (await FindProduct(admin, id))!.Value.GetProperty("cost").GetDecimal().Should().Be(3.50m);

        // ClearCost : effacé.
        await admin.PutAsJsonAsync($"/api/catalog/products/{id}",
            new UpdateProductRequest("Plat modifié", "DESSERTS", 10m, 10m, null, null, 1, true, true, false, null, null, true));
        (await FindProduct(admin, id))!.Value.GetProperty("cost").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Negative_cost_is_rejected_with_400()
    {
        var response = await ClientAs("Admin").PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest("Coût négatif", "DESSERTS", 10m, 10m, null, null, 1, false, null, -1m));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().Be(Texts.Get(new System.Globalization.CultureInfo("en"), "errors.product_cost_negative"));
    }

    [Fact]
    public async Task MenuAnalysis_requires_manager_or_admin()
    {
        (await ClientAs(null).GetAsync("/api/menu-analysis")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientAs("Waiter").GetAsync("/api/menu-analysis")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ok = await ClientAs("Admin").GetAsync("/api/menu-analysis");
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
