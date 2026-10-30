using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace RestaurantPos.Api.Tests;

/// <summary>Encaissements du jour : réservés aux responsables, 24 heures, comparaison avec la semaine passée.</summary>
public class TakingsApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public TakingsApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientAs(string? role)
    {
        var client = _factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static string Url()
    {
        var from = DateTimeOffset.UtcNow.Date;
        return $"/api/dashboard/takings?from={Uri.EscapeDataString(new DateTimeOffset(from, TimeSpan.Zero).ToString("O"))}&to={Uri.EscapeDataString(new DateTimeOffset(from.AddDays(1), TimeSpan.Zero).ToString("O"))}&tzOffsetMinutes=60";
    }

    [Fact]
    public async Task Requires_manager_or_admin()
    {
        (await ClientAs(null).GetAsync(Url())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientAs("Waiter").GetAsync(Url())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Returns_the_day_with_24_hours_and_the_comparison_fields()
    {
        var response = await ClientAs("Admin").GetAsync(Url());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("hours").GetArrayLength().Should().Be(24);
        body.TryGetProperty("lastWeekByNowTtc", out _).Should().BeTrue();
        body.TryGetProperty("spendPerCover", out _).Should().BeTrue();
        body.GetProperty("paymentMethods").ValueKind.Should().Be(JsonValueKind.Array);
    }
}
