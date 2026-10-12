using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
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

public class HospitalityEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public HospitalityEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient(string role = "FloorManager")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static async Task<DiningTableDto> OpenTableWithItemAsync(HttpClient client, string tableNumber, decimal price = 10.00m, CourseType course = CourseType.Direct)
    {
        var openResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/open", new OpenTableRequest("Serveur 1", 2, Guid.NewGuid()));
        openResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var table = await openResp.Content.ReadFromJsonAsync<DiningTableDto>();

        var addResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/items", new AddOrderItemsRequest(
        [
            new OrderItemInputDto(Guid.NewGuid(), "Plat Test", 1, price, 10.0m, "HOT_KITCHEN", null, course)
        ]));
        addResp.StatusCode.Should().Be(HttpStatusCode.OK);

        return table!;
    }

    [Fact]
    public async Task TransferTable_MovesOrderToFreeTable()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_TR_" + Random.Shared.Next(100, 999);
        var targetTable = "T_TR_DST_" + Random.Shared.Next(100, 999);

        await client.PostAsJsonAsync("/api/tables", new CreateTableRequest(targetTable, 4, 0, 0));
        await OpenTableWithItemAsync(client, tableNumber);

        var transferResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/transfer", new TransferTableRequest(targetTable));
        transferResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify target table has the active order now
        var targetOrderResp = await client.GetAsync($"/api/tables/{targetTable}/order");
        targetOrderResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Source table no longer has an active order
        var sourceOrderResp = await client.GetAsync($"/api/tables/{tableNumber}/order");
        sourceOrderResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MergeTables_CombinesOrdersAndFreesSourceTable()
    {
        var client = CreateAuthenticatedClient();
        var table1 = "T_MG1_" + Random.Shared.Next(100, 999);
        var table2 = "T_MG2_" + Random.Shared.Next(100, 999);

        await OpenTableWithItemAsync(client, table1, 10.00m);
        await OpenTableWithItemAsync(client, table2, 15.00m);

        var mergeResp = await client.PostAsJsonAsync($"/api/tables/{table1}/merge", new MergeTablesRequest(table2));
        mergeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // table1 order is moved and freed
        var sourceOrderResp = await client.GetAsync($"/api/tables/{table1}/order");
        sourceOrderResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // table2 has both items
        var targetOrderResp = await client.GetAsync($"/api/tables/{table2}/order");
        targetOrderResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await targetOrderResp.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        order!.Lines.Should().HaveCount(2);
        order.TotalTtcAmount.Should().Be(25.00m);
    }

    [Fact]
    public async Task OrderDiscount_ApplyRemoveAndComp()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_DISC_" + Random.Shared.Next(100, 999);

        var table = await OpenTableWithItemAsync(client, tableNumber, 20.00m);
        var orderResp = await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{tableNumber}/order");
        var orderId = orderResp!.OrderId;
        var itemId = orderResp.Lines[0].LineId;

        // Apply 10% discount -> 18.00 EUR
        var discResp = await client.PostAsJsonAsync($"/api/orders/{orderId}/discount", new ApplyOrderDiscountRequest(
            DiscountType.Percentage, 10m, "Remise fidélité", Guid.NewGuid()
        ));
        discResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var discBody = await discResp.Content.ReadFromJsonAsync<JsonElement>();
        discBody.GetProperty("totalTtc").GetDecimal().Should().Be(18.00m);

        // Remove discount -> 20.00 EUR
        var delResp = await client.DeleteAsync($"/api/orders/{orderId}/discount");
        delResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var delBody = await delResp.Content.ReadFromJsonAsync<JsonElement>();
        delBody.GetProperty("totalTtc").GetDecimal().Should().Be(20.00m);

        // Comp line item -> 0.00 EUR
        var compResp = await client.PostAsJsonAsync($"/api/orders/{orderId}/items/{itemId}/comp", new CompOrderItemRequest(
            "Geste commercial", Guid.NewGuid()
        ));
        compResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var compBody = await compResp.Content.ReadFromJsonAsync<JsonElement>();
        compBody.GetProperty("totalTtc").GetDecimal().Should().Be(0.00m);
    }

    [Fact]
    public async Task FireSuite_CreatesKitchenTicketWithReclameSuite()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_SUITE_" + Random.Shared.Next(100, 999);

        await OpenTableWithItemAsync(client, tableNumber, 15.00m, CourseType.Suite);

        var fireResp = await client.PostAsync($"/api/tables/{tableNumber}/fire-suite", null);
        fireResp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ticket = await db.KitchenTickets.Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.TableNumber == tableNumber);

        ticket.Should().NotBeNull();
        ticket!.Items.Should().Contain(i => i.ProductName.StartsWith("[RÉCLAME SUITE]"));
    }

    [Fact]
    public async Task HotelRooms_GetRoomsAndOccupant()
    {
        var client = CreateAuthenticatedClient();

        // Seeded rooms: 201, 204, 305
        var roomsResp = await client.GetAsync("/api/hotel/rooms");
        roomsResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var rooms = await roomsResp.Content.ReadFromJsonAsync<List<JsonElement>>();
        rooms.Should().NotBeNull();
        rooms.Should().NotBeEmpty();

        var roomResp = await client.GetAsync("/api/hotel/rooms/204");
        roomResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var notFoundResp = await client.GetAsync("/api/hotel/rooms/9999");
        notFoundResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task HotelRoomCharge_ChargesResidentAndFreesTable()
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_ROOM_" + Random.Shared.Next(100, 999);

        await OpenTableWithItemAsync(client, tableNumber, 35.00m);
        var orderResp = await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{tableNumber}/order");

        var chargeReq = new RoomChargeRequest(
            orderResp!.OrderId,
            tableNumber,
            "204",
            "Alexandre Dupont",
            35.00m,
            5.00m,
            "data:image/png;base64,signature",
            "Dîner restaurant"
        );

        var chargeResp = await client.PostAsJsonAsync("/api/hotel/room-charge", chargeReq);
        chargeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await chargeResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
        body.GetProperty("totalCharged").GetDecimal().Should().Be(40.00m);

        // Table is now free
        var tableOrderResp = await client.GetAsync($"/api/tables/{tableNumber}/order");
        tableOrderResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
