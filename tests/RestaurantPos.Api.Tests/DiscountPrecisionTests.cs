using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Tests;

/// <summary>Une valeur de remise à plus de 2 décimales est refusée : serveur et clients arrondiraient différemment au centime.</summary>
public class DiscountPrecisionTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DiscountPrecisionTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, Guid OrderId)> OrderAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");
        var table = "T_PREC_" + Random.Shared.Next(1000, 9999);
        await client.PostAsJsonAsync($"/api/tables/{table}/open", new OpenTableRequest("Serveur", 2, Guid.NewGuid()));
        await client.PostAsJsonAsync($"/api/tables/{table}/items", new AddOrderItemsRequest(
            [new OrderItemInputDto(Guid.NewGuid(), "Plat", 1, 20m, 10m, "HOT_KITCHEN", null)]));
        var order = await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{table}/order");
        return (client, order!.OrderId);
    }

    [Theory]
    [InlineData(DiscountType.FixedAmount, 1.005)]
    [InlineData(DiscountType.Percentage, 12.345)]
    public async Task Discount_MoreThanTwoDecimals_Returns400WithLocalizedMessage(DiscountType type, double value)
    {
        var (client, orderId) = await OrderAsync();

        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/discount", new ApplyOrderDiscountRequest(type, (decimal)value, "Test", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be(Texts.Get(new CultureInfo("en"), "errors.discount_value_precision"));
    }

    [Theory]
    [InlineData(DiscountType.FixedAmount, 1.5)]
    [InlineData(DiscountType.Percentage, 12.35)]
    public async Task Discount_TwoDecimalsOrLess_IsAccepted(DiscountType type, double value)
    {
        var (client, orderId) = await OrderAsync();

        var response = await client.PostAsJsonAsync($"/api/orders/{orderId}/discount", new ApplyOrderDiscountRequest(type, (decimal)value, "Test", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
