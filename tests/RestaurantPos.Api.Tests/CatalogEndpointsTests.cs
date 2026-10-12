using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class CatalogEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public CatalogEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task GetCategories_AllowsAnonymous_ReturnsList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/catalog/categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var categories = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        categories.Should().NotBeNull();
        categories.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CreateCategory_RBACAndValidation()
    {
        var waiter = CreateClientWithRole("Waiter");
        var admin = CreateClientWithRole("Admin");

        // 1. Waiter is forbidden
        var req = new CreateCategoryRequest("Desserts Spéciaux", "#FFAA00", 10, "cake", "DESSERT");
        var waiterResp = await waiter.PostAsJsonAsync("/api/catalog/categories", req);
        waiterResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Invalid preparation station -> 400
        var invalidStationReq = new CreateCategoryRequest("Desserts Spéciaux", "#FFAA00", 10, "cake", "UNKNOWN_STATION");
        var invalidResp = await admin.PostAsJsonAsync("/api/catalog/categories", invalidStationReq);
        invalidResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Admin succeeds
        var adminResp = await admin.PostAsJsonAsync("/api/catalog/categories", req);
        adminResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await adminResp.Content.ReadFromJsonAsync<JsonElement>();
        created.GetProperty("name").GetString().Should().Be("Desserts Spéciaux");
    }

    [Fact]
    public async Task UpdateCategory_AsAdmin_UpdatesDetails()
    {
        var admin = CreateClientWithRole("Admin");
        var createResp = await admin.PostAsJsonAsync("/api/catalog/categories", new CreateCategoryRequest("Boissons Test", "#00FF00", 2, "glass", "BAR"));
        var created = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetString();

        var updateReq = new UpdateCategoryRequest("Boissons Modifiées", "#008800", 3, "glass", true, "BAR");
        var updateResp = await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", updateReq);

        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("name").GetString().Should().Be("Boissons Modifiées");
    }

    [Fact]
    public async Task GetProducts_AndFilteringByCategory()
    {
        var client = _factory.CreateClient();

        var allResp = await client.GetAsync("/api/catalog/products");
        allResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var allProducts = await allResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        allProducts.Should().NotBeNull();
        allProducts.Should().NotBeEmpty();

        var firstCat = allProducts![0].GetProperty("categoryId").GetString();
        var catResp = await client.GetAsync($"/api/catalog/products?categoryId={firstCat}");
        catResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var catProducts = await catResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        catProducts.Should().NotBeNull();
        catProducts.Should().OnlyContain(p => p.GetProperty("categoryId").GetString() == firstCat);
    }

    [Fact]
    public async Task ProductCrud_RBACAndLifecycle()
    {
        var waiter = CreateClientWithRole("Waiter");
        var admin = CreateClientWithRole("Admin");

        var createReq = new CreateProductRequest(
            "Tarte Citron Meringuée",
            "DESSERTS",
            7.50m,
            10.0m,
            "Tarte artisanale",
            "#FFFF00",
            5,
            false,
            "DESSERT"
        );

        // 1. Waiter cannot create
        var waiterCreate = await waiter.PostAsJsonAsync("/api/catalog/products", createReq);
        waiterCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. Admin creates
        var adminCreate = await admin.PostAsJsonAsync("/api/catalog/products", createReq);
        adminCreate.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await adminCreate.Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();
        created.GetProperty("name").GetString().Should().Be("Tarte Citron Meringuée");
        created.GetProperty("price").GetDecimal().Should().Be(7.50m);

        // 3. Admin updates
        var updateReq = new UpdateProductRequest(
            "Tarte Citron Meringuée Grande",
            "DESSERTS",
            8.50m,
            10.0m,
            "Grande part",
            "#FFFF00",
            5,
            true,
            true,
            false,
            "DESSERT"
        );
        var updateResp = await admin.PutAsJsonAsync($"/api/catalog/products/{id}", updateReq);
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<JsonElement>();
        updated.GetProperty("price").GetDecimal().Should().Be(8.50m);

        // 4. Waiter cannot delete
        var waiterDelete = await waiter.DeleteAsync($"/api/catalog/products/{id}");
        waiterDelete.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Admin deletes (archives)
        var adminDelete = await admin.DeleteAsync($"/api/catalog/products/{id}");
        adminDelete.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Delete non-existent -> 404
        var notFoundDelete = await admin.DeleteAsync($"/api/catalog/products/{Guid.NewGuid()}");
        notFoundDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
