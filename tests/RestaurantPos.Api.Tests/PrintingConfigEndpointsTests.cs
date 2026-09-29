using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintingConfigEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public PrintingConfigEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    private static async Task<string> CreateCategoryAsync(HttpClient admin, string? station)
    {
        var res = await admin.PostAsJsonAsync("/api/catalog/categories", new { name = "Famille " + Guid.NewGuid().ToString("N")[..6], colorHex = "#123456", displayOrder = 9, iconName = (string?)null, preparationStationId = station });
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private static async Task<string?> StationOfAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/catalog/categories"))
            .EnumerateArray().Single(c => c.GetProperty("id").GetString() == id).GetProperty("preparationStationId").GetString();

    private async Task<Guid> AnyPrinterIdAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = await db.PrinterConfigurations.FirstOrDefaultAsync();
        if (printer is not null) return printer.Id;
        printer = new PrinterConfiguration { Name = "Caisse test", IpAddress = "10.0.0.2", AssignedStationIds = ["RECEIPT"] };
        db.PrinterConfigurations.Add(printer);
        await db.SaveChangesAsync();
        return printer.Id;
    }

    [Fact]
    public async Task PutCategory_WithStation_IsReturnedByGet()
    {
        var admin = await ClientAsync("9999");
        var id = await CreateCategoryAsync(admin, null);
        (await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Bar", colorHex = "#123456", displayOrder = 9, isActive = true, preparationStationId = "BAR" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await StationOfAsync(admin, id)).Should().Be("BAR");
    }

    [Fact]
    public async Task PutCategory_WithoutStation_KeepsIt_EmptyClearsIt()
    {
        var admin = await ClientAsync("9999");
        var id = await CreateCategoryAsync(admin, "DESSERT");
        await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Renommée", colorHex = "#123456", displayOrder = 9, isActive = true });
        (await StationOfAsync(admin, id)).Should().Be("DESSERT");
        await admin.PutAsJsonAsync($"/api/catalog/categories/{id}", new { name = "Renommée", colorHex = "#123456", displayOrder = 9, isActive = true, preparationStationId = "" });
        (await StationOfAsync(admin, id)).Should().BeNull();
    }

    [Theory]
    [InlineData("RECEIPT")]
    [InlineData("STATION-HOT")]
    public async Task Category_InvalidStation_Returns400(string station)
    {
        var admin = await ClientAsync("9999");
        var res = await admin.PostAsJsonAsync("/api/catalog/categories", new { name = "X", colorHex = "#123456", displayOrder = 1, preparationStationId = station });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_SetListedAndCleared()
    {
        var admin = await ClientAsync("9999");
        var device = await DeviceTestHelper.PairAsync(_factory, _factory.CreateClient(), "Caisse imprimante");
        var printerId = await AnyPrinterIdAsync();

        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(printerId))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var list = await admin.GetFromJsonAsync<JsonElement>("/api/devices");
        list.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == device.DeviceId).GetProperty("receiptPrinterId").GetGuid().Should().Be(printerId);

        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_UnknownPrinterOrDevice_Returns404()
    {
        var admin = await ClientAsync("9999");
        var device = await DeviceTestHelper.PairAsync(_factory, _factory.CreateClient(), "Caisse 404");
        (await admin.PutAsJsonAsync($"/api/devices/{device.DeviceId}/receipt-printer", new SetReceiptPrinterRequest(Guid.NewGuid()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Device_ReceiptPrinter_WaiterForbidden()
    {
        var waiter = await ClientAsync("2468");
        (await waiter.PutAsJsonAsync($"/api/devices/{Guid.NewGuid()}/receipt-printer", new SetReceiptPrinterRequest(null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
