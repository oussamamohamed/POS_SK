using System;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class OrderItemValidationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public OrderItemValidationTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");
        return client;
    }

    [Theory]
    [InlineData(0, 10.0, 0.0)]
    [InlineData(-1, 10.0, 0.0)]
    [InlineData(1, -10.0, 0.0)]
    [InlineData(1, 10.0, -2.0)]
    public async Task AddItems_WithNonPositiveQuantityOrNegativeAmount_ReturnsBadRequest(int quantity, double unitPrice, double extra)
    {
        var client = CreateAuthenticatedClient();
        var tableNumber = "T_NEG_" + Random.Shared.Next(1000, 9999);
        await client.PostAsJsonAsync($"/api/tables/{tableNumber}/open", new OpenTableRequest("Serveur", 2, Guid.NewGuid()));

        var req = new AddOrderItemsRequest(
        [
            new OrderItemInputDto(Guid.NewGuid(), "Ligne", quantity, (decimal)unitPrice, 10.0m, "HOT_KITCHEN", null, ModifiersPriceExtra: (decimal)extra)
        ]);
        var response = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/items", req);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be(Texts.Get(new CultureInfo("en"), "errors.order_item_invalid"));
        var order = await client.GetAsync($"/api/tables/{tableNumber}/order");
        var dto = await order.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        dto!.Lines.Should().BeEmpty();
    }
}
