using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class CounterDirectSaleTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public CounterDirectSaleTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", result!.Token);
        return client;
    }

    [Fact]
    public async Task DirectCounter_InitializesDefaultTakeawayOrder()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openRequest = new DirectCounterOpenRequest("POS_TERM_1", OrderDestination.Takeaway);
        var response = await client.PostAsJsonAsync("/api/orders/counter/direct", openRequest);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var order = await response.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        order.Should().NotBeNull();
        order!.TableNumber.Should().Be("Comptoir");
        order.Destination.Should().Be(OrderDestination.Takeaway);
    }

    [Fact]
    public async Task SwitchDestination_UpdatesOrderDestination()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_TERM_1", OrderDestination.Takeaway));
        var initialOrder = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        initialOrder.Should().NotBeNull();

        var switchResponse = await client.PostAsJsonAsync($"/api/orders/{initialOrder!.OrderId}/destination", new SwitchDestinationRequest(OrderDestination.EatIn));
        switchResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updatedOrder = await switchResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        updatedOrder.Should().NotBeNull();
        updatedOrder!.Destination.Should().Be(OrderDestination.EatIn);
    }

    [Fact]
    public async Task HoldAndRecall_LifecycleWorksAccurately()
    {
        var client = await CreateAuthenticatedClientAsync();

        // 1. Open direct cart and add item
        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_A", OrderDestination.Takeaway));
        var initialOrder = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        var addItemsResponse = await client.PostAsJsonAsync($"/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Sandwich Poulet", 1, 6.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));
        addItemsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Put on Hold
        var holdResponse = await client.PostAsJsonAsync("/api/orders/counter/hold", new HoldCounterOrderRequest(initialOrder!.OrderId, "POS_A", null, "Client Pressé"));
        holdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var heldDto = await holdResponse.Content.ReadFromJsonAsync<HeldOrderDto>();
        heldDto.Should().NotBeNull();
        heldDto!.CustomerLabel.Should().Be("Client Pressé");

        // 3. Verify in active held list
        var listResponse = await client.GetAsync("/api/orders/counter/held?terminalId=POS_A");
        var list = await listResponse.Content.ReadFromJsonAsync<List<HeldOrderDto>>();
        list.Should().NotBeNull();
        list.Should().Contain(h => h.HoldId == heldDto.HoldId);

        // 4. Recall
        var recallResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto.HoldId}/recall", new { });
        recallResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var recalledOrder = await recallResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        recalledOrder.Should().NotBeNull();
        recalledOrder!.TableNumber.Should().Be("Comptoir");
        recalledOrder.Lines.Should().Contain(l => l.ProductName == "Sandwich Poulet");
    }

    [Fact]
    public async Task VoidHeldOrder_RequiresSupervisorPin()
    {
        var client = await CreateAuthenticatedClientAsync();

        var openResponse = await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("POS_B", OrderDestination.Takeaway));
        var order = await openResponse.Content.ReadFromJsonAsync<ActiveTableOrderDto>();

        var addItemsResponse = await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(new List<OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Café Expresso", 1, 2.00m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        }));
        addItemsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var holdResponse = await client.PostAsJsonAsync("/api/orders/counter/hold", new HoldCounterOrderRequest(order!.OrderId, "POS_B", null, "À annuler"));
        holdResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var heldDto = await holdResponse.Content.ReadFromJsonAsync<HeldOrderDto>();
        heldDto.Should().NotBeNull();

        // Bad / non-manager PIN (e.g. invalid PIN)
        var invalidPinResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto!.HoldId}/void", new VoidHeldOrderRequest("0000", "Erreur client", "POS_B"));
        invalidPinResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Valid manager PIN ("9999" is Admin/Manager in seeded data)
        var validPinResponse = await client.PostAsJsonAsync($"/api/orders/counter/held/{heldDto.HoldId}/void", new VoidHeldOrderRequest("9999", "Erreur client", "POS_B"));
        validPinResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
