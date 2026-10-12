using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class TableEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public TableEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(string role = "Waiter")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task GetFloorPlan_AllowsAnonymous_ReturnsList()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/tables");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tables = await response.Content.ReadFromJsonAsync<List<DiningTableDto>>();
        tables.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateTable_WithValidData_ReturnsCreated()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_NEW_" + Random.Shared.Next(1000, 9999);

        var req = new CreateTableRequest(tableNumber, 4, 150.0, 250.0);
        var response = await client.PostAsJsonAsync("/api/tables", req);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<DiningTableDto>();
        created.Should().NotBeNull();
        created!.TableNumber.Should().Be(tableNumber);
        created.Capacity.Should().Be(4);
    }

    [Fact]
    public async Task CreateTable_WithEmptyTableNumber_ReturnsBadRequest()
    {
        var client = CreateAuthenticatedClient();

        var req = new CreateTableRequest("   ", 4, 0, 0);
        var response = await client.PostAsJsonAsync("/api/tables", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task OpenTable_AndGetActiveOrder_Lifecycle()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_ORD_" + Random.Shared.Next(1000, 9999);

        // 1. Before opening -> 404 NotFound
        var beforeResp = await client.GetAsync($"/api/tables/{tableNumber}/order");
        beforeResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // 2. Open table -> 200 OK with ActiveOrderId
        var openReq = new OpenTableRequest("Serveur Marc", 3, Guid.NewGuid());
        var openResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/open", openReq);
        openResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var openedTable = await openResp.Content.ReadFromJsonAsync<DiningTableDto>();
        openedTable!.ActiveOrderId.Should().NotBeNull();

        // 3. Add items
        var addReq = new AddOrderItemsRequest(
        [
            new OrderItemInputDto(Guid.NewGuid(), "Entrecôte", 2, 22.00m, 10.0m, "HOT_KITCHEN", null)
        ]);
        var addResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/items", addReq);
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Get active order -> 200 OK
        var orderResp = await client.GetAsync($"/api/tables/{tableNumber}/order");
        orderResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await orderResp.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        order!.TableNumber.Should().Be(tableNumber);
        order.Lines.Should().HaveCount(1);
        order.Lines[0].ProductName.Should().Be("Entrecôte");

        // 5. Dispatch order lines
        var dispatchResp = await client.PostAsync($"/api/tables/{tableNumber}/dispatch", null);
        dispatchResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dispatch_WithNonExistentTable_ReturnsNotFound()
    {
        var client = CreateAuthenticatedClient();

        var response = await client.PostAsync("/api/tables/UNKNOWN_TABLE_9999/dispatch", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
