using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class GridEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public GridEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(string role = "Admin")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task GetAllLayouts_AllowsAnonymous_ReturnsList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/grid-layouts");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var layouts = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        layouts.Should().NotBeNull();
    }

    [Fact]
    public async Task GetLayoutByCategory_ReturnsLayoutWithDefaults()
    {
        var client = _factory.CreateClient();
        var categoryId = "CAT_TEST_" + Random.Shared.Next(1000, 9999);

        var response = await client.GetAsync($"/api/grid-layouts/{categoryId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var layout = await response.Content.ReadFromJsonAsync<GridLayoutDto>();
        layout.Should().NotBeNull();
        layout!.CategoryId.Should().Be(categoryId);
    }

    [Fact]
    public async Task SaveLayoutAndGetPages_RoundTrips()
    {
        var client = CreateAuthenticatedClient();
        var categoryId = "CAT_" + Random.Shared.Next(1000, 9999);

        var saveReq = new UpdateGridLayoutRequest(
            categoryId,
            ColumnsCount: 4,
            RowsCount: 3,
            PageIndex: 0,
            Slots:
            [
                new UpdateGridSlotItem(0, 0, null, "Item A", "#FF0000"),
                new UpdateGridSlotItem(0, 1, null, "Item B", "#00FF00")
            ]
        );

        var saveResp = await client.PostAsJsonAsync("/api/grid-layouts", saveReq);
        saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = await saveResp.Content.ReadFromJsonAsync<GridLayoutDto>();
        saved.Should().NotBeNull();
        saved!.CategoryId.Should().Be(categoryId);
        saved.ColumnsCount.Should().Be(4);
        saved.RowsCount.Should().Be(3);
        saved.Slots.Should().HaveCount(12); // 4 * 3 grid slots
        saved.Slots.Should().Contain(s => s.CustomLabel == "Item A");
        saved.Slots.Should().Contain(s => s.CustomLabel == "Item B");

        // GET by category
        var getResp = await client.GetAsync($"/api/grid-layouts/{categoryId}");
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResp.Content.ReadFromJsonAsync<GridLayoutDto>();
        fetched!.CategoryId.Should().Be(categoryId);

        // GET pages
        var pagesResp = await client.GetAsync($"/api/grid-layouts/{categoryId}/pages");
        pagesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var pages = await pagesResp.Content.ReadFromJsonAsync<List<GridLayoutDto>>();
        pages.Should().NotBeNull();
        pages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task SwapSlots_SwapsSlotPositions()
    {
        var client = CreateAuthenticatedClient();
        var categoryId = "CAT_SWP_" + Random.Shared.Next(1000, 9999);

        var saveReq = new UpdateGridLayoutRequest(
            categoryId,
            ColumnsCount: 4,
            RowsCount: 3,
            PageIndex: 0,
            Slots:
            [
                new UpdateGridSlotItem(0, 0, null, "First", "#111111"),
                new UpdateGridSlotItem(0, 1, null, "Second", "#222222")
            ]
        );

        var saveResp = await client.PostAsJsonAsync("/api/grid-layouts", saveReq);
        var saved = await saveResp.Content.ReadFromJsonAsync<GridLayoutDto>();

        var swapReq = new SwapGridSlotsRequest(saved!.Id, 0, 0, 0, 1);
        var swapResp = await client.PostAsJsonAsync("/api/grid-layouts/swap", swapReq);
        swapResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var swapped = await swapResp.Content.ReadFromJsonAsync<GridLayoutDto>();
        var slot01 = swapped!.Slots.Find(s => s.RowIndex == 0 && s.ColumnIndex == 1);
        slot01!.CustomLabel.Should().Be("First");
    }

    [Fact]
    public async Task SwapSlots_WithInvalidLayoutId_ReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();
        var swapReq = new SwapGridSlotsRequest(Guid.NewGuid(), 0, 0, 0, 1);

        var swapResp = await client.PostAsJsonAsync("/api/grid-layouts/swap", swapReq);

        swapResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateDimensions_UpdatesGridDimensions()
    {
        var client = CreateAuthenticatedClient();
        var categoryId = "CAT_DIM_" + Random.Shared.Next(1000, 9999);

        var saveReq = new UpdateGridLayoutRequest(
            categoryId,
            ColumnsCount: 3,
            RowsCount: 3,
            PageIndex: 0,
            Slots: []
        );
        await client.PostAsJsonAsync("/api/grid-layouts", saveReq);

        var dimReq = new UpdateGridDimensionsRequest(categoryId, ColumnsCount: 5, RowsCount: 4);
        var dimResp = await client.PostAsJsonAsync("/api/grid-layouts/dimensions", dimReq);
        dimResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await dimResp.Content.ReadFromJsonAsync<List<GridLayoutDto>>();
        list.Should().NotBeNull();
        list.Should().Contain(l => l.CategoryId == categoryId && l.ColumnsCount == 5 && l.RowsCount == 4);
    }
}
