using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class RecipeEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public RecipeEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static string Unique(string prefix) => prefix + Guid.NewGuid().ToString("N")[..6];

    private async Task<Guid> IngredientAsync(string name, long mc, int mask = 0, bool prep = false, long? yield = null)
    {
        var resp = await Client("Admin").PostAsJsonAsync("/api/stock/ingredients",
            new { name, unit = "g", unitCostMilliCents = mc, isPreparation = prep, yieldMilli = yield, allergenMask = mask, lowStockMilli = (long?)null });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> ProductAsync()
    {
        var admin = Client("Admin");
        var categories = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/categories");
        var resp = await admin.PostAsJsonAsync("/api/catalog/products",
            new CreateProductRequest(Unique("Plat "), categories![0].GetProperty("id").GetString()!, 9m, 10m, null, null, 1, false, null, 3m));
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Waiter_CannotTouchIngredientsOrRecipes()
    {
        var waiter = Client("Waiter");
        (await waiter.GetAsync("/api/stock/ingredients")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync($"/api/catalog/products/{Guid.NewGuid()}/recipe")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Recipe_ReturnsCostAndDeducedAllergens_AndFillsTheProduct()
    {
        var flour = await IngredientAsync(Unique("Farine "), 1250, mask: 1);
        var cheese = await IngredientAsync(Unique("Mozza "), 1800, mask: 1 << 6);
        var product = await ProductAsync();
        var admin = Client("Admin");

        var put = await admin.PutAsJsonAsync($"/api/catalog/products/{product}/recipe",
            new { lines = new[] { new { ingredientId = flour, quantityMilli = 150_000L }, new { ingredientId = cheese, quantityMilli = 125_000L } } });

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await put.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("costCents").GetInt64().Should().Be(413); // 187,5 + 225 = 412,5
        body.GetProperty("allergenMask").GetInt32().Should().Be(1 | (1 << 6));
        var products = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/products");
        var seen = products!.Find(p => p.GetProperty("id").GetGuid() == product);
        seen.GetProperty("allergenMask").GetInt32().Should().Be(1 | (1 << 6));
        seen.GetProperty("allergensDeclared").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Recipe_KeepsManualAllergens_WhenOverridden()
    {
        var flour = await IngredientAsync(Unique("Farine "), 100, mask: 1);
        var product = await ProductAsync();
        var admin = Client("Admin");
        (await admin.PutAsJsonAsync($"/api/catalog/products/{product}/allergens", new SetAllergensRequest(true, 1 << 3, true))).EnsureSuccessStatusCode();

        await admin.PutAsJsonAsync($"/api/catalog/products/{product}/recipe", new { lines = new[] { new { ingredientId = flour, quantityMilli = 1000L } } });

        var products = await admin.GetFromJsonAsync<List<JsonElement>>("/api/catalog/products");
        products!.Find(p => p.GetProperty("id").GetGuid() == product).GetProperty("allergenMask").GetInt32().Should().Be(1 << 3);
    }

    [Fact]
    public async Task Cycle_Returns400_WithCode()
    {
        var a = await IngredientAsync(Unique("PrepA "), 0, prep: true, yield: 1000);
        var b = await IngredientAsync(Unique("PrepB "), 0, prep: true, yield: 1000);
        // A contient B ; la fiche de B contenant A fermerait la boucle.
        (await SetPrepRecipe(a, b)).StatusCode.Should().Be(HttpStatusCode.OK);
        var resp = await SetPrepRecipe(b, a);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("recipe_cycle");
    }

    private Task<HttpResponseMessage> SetPrepRecipe(Guid prep, Guid component) =>
        Client("Admin").PutAsJsonAsync($"/api/stock/ingredients/{prep}/recipe", new { lines = new[] { new { ingredientId = component, quantityMilli = 1000L } } });

    [Fact]
    public async Task DuplicateName_IsRefused_CaseInsensitively()
    {
        var name = Unique("Sel ");
        await IngredientAsync(name, 10);

        var resp = await Client("Admin").PostAsJsonAsync("/api/stock/ingredients",
            new { name = name.ToUpperInvariant(), unit = "g", unitCostMilliCents = 10L, isPreparation = false, allergenMask = 0 });

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_UsedIngredient_Returns409_ListingProducts()
    {
        var flour = await IngredientAsync(Unique("Farine "), 100);
        var product = await ProductAsync();
        var admin = Client("Admin");
        await admin.PutAsJsonAsync($"/api/catalog/products/{product}/recipe", new { lines = new[] { new { ingredientId = flour, quantityMilli = 1000L } } });

        var resp = await admin.DeleteAsync($"/api/stock/ingredients/{flour}");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("ingredient_in_use");
        body.GetProperty("products").GetArrayLength().Should().Be(1);
    }
}
