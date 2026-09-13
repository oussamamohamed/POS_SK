using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class HeldOrderQueueTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public HeldOrderQueueTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        return client;
    }

    [Fact]
    public async Task HoldAndRecall_RestoresFullCartState()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Open direct counter order
        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_QUEUE_1", OrderDestination.Takeaway));
        var initialOrder = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        initialOrder.Should().NotBeNull();

        // 2. Add 2 distinct items
        await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Salade Grecque", 1, 8.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true),
            new(Guid.NewGuid(), "Tarte Citron", 1, 4.00m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));

        // 3. Put on hold
        var holdResponse = await client.PostAsJsonAsync("/api/orders/counter/hold", new HoldCounterOrderRequest(
            initialOrder!.OrderId,
            "POS_QUEUE_1",
            null,
            "Client En Ligne 1"
        ));
        holdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var heldDto = await holdResponse.Content.ReadFromJsonAsync<HeldOrderDto>();
        heldDto.Should().NotBeNull();
        heldDto!.CustomerLabel.Should().Be("Client En Ligne 1");

        // 4. Verify held queue
        var listResponse = await client.GetAsync("/api/orders/counter/held?terminalId=POS_QUEUE_1");
        var list = await listResponse.Content.ReadFromJsonAsync<List<HeldOrderDto>>();
        list.Should().NotBeNull();
        list.Should().Contain(h => h.HoldId == heldDto.HoldId);

        // 5. Recall
        var recallResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto.HoldId}/recall", new { });
        recallResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recalled = await recallResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        recalled.Should().NotBeNull();
        recalled!.Lines.Should().HaveCount(2);
        recalled.Lines.Should().Contain(l => l.ProductName == "Salade Grecque");
        recalled.Lines.Should().Contain(l => l.ProductName == "Tarte Citron");
    }

    [Fact]
    public async Task VoidHeldOrder_FailsWithoutSupervisorPin_SucceedsWithAdminPin()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_QUEUE_2", OrderDestination.Takeaway));
        var order = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Croissant", 2, 1.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));

        var holdResponse = await client.PostAsJsonAsync("/api/orders/counter/hold", new HoldCounterOrderRequest(
            order!.OrderId,
            "POS_QUEUE_2",
            null,
            "Client Parti"
        ));
        var heldDto = await holdResponse.Content.ReadFromJsonAsync<HeldOrderDto>();

        // Rejection with invalid PIN
        var rejectResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto!.HoldId}/void", new VoidHeldOrderRequest("1111", "Client parti sans payer", "POS_QUEUE_2"));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Success with Admin/Manager PIN "9999"
        var successResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto.HoldId}/void", new VoidHeldOrderRequest("9999", "Client parti sans payer", "POS_QUEUE_2"));
        successResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify it is no longer listed in active held orders
        var listResponse = await client.GetAsync("/api/orders/counter/held?terminalId=POS_QUEUE_2");
        var list = await listResponse.Content.ReadFromJsonAsync<List<HeldOrderDto>>();
        list.Should().NotContain(h => h.HoldId == heldDto.HoldId);
    }
}
