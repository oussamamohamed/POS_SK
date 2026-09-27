using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class SettingsEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public SettingsEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    [Fact]
    public async Task Put_ThenGet_RoundTrips()
    {
        var admin = await ClientAsync("9999");
        (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("ar"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        body.GetProperty("receiptLanguage").GetString().Should().Be("ar");
    }

    [Fact]
    public async Task Put_InvalidLanguage_Returns400_Translated()
    {
        var admin = await ClientAsync("9999");
        admin.DefaultRequestHeaders.Add("Accept-Language", "fr");
        var response = await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("de"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().Be("Langue des tickets non supportée (en, fr, ar).");
    }

    [Fact]
    public async Task Put_AsWaiter_IsForbidden()
    {
        var waiter = await ClientAsync("2468");
        (await waiter.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Get_Anonymous_IsUnauthorized() =>
        (await _factory.CreateClient().GetAsync("/api/settings")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
}
