using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace RestaurantPos.Api.Tests;

public class InsightsEndpointTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public InsightsEndpointTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    [Fact]
    public async Task Insights_AreManagerOnly_AndHaveTheContractShape()
    {
        (await Client("Waiter").GetAsync("/api/dashboard/insights?tzOffsetMinutes=60")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var body = await Client("Admin").GetFromJsonAsync<JsonElement>("/api/dashboard/insights?tzOffsetMinutes=60");

        body.GetProperty("cards").ValueKind.Should().Be(JsonValueKind.Array);
        body.GetProperty("insufficientData").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
        foreach (var card in body.GetProperty("cards").EnumerateArray())
        {
            card.TryGetProperty("metric", out _).Should().BeTrue();
            card.TryGetProperty("currentCents", out _).Should().BeTrue();
            card.TryGetProperty("referenceCents", out _).Should().BeTrue();
            card.TryGetProperty("deltaPercent", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AlertSettings_CarryTheTrendThreshold()
    {
        var admin = Client("Admin");
        (await admin.PutAsJsonAsync("/api/alerts/settings", new { trendThresholdPercent = 22, cashVarianceNoteRequired = false })).StatusCode.Should().Be(HttpStatusCode.OK);

        var settings = await admin.GetFromJsonAsync<JsonElement>("/api/alerts/settings");

        settings.GetProperty("trendThresholdPercent").GetInt32().Should().Be(22);
        settings.GetProperty("cashVarianceNoteRequired").GetBoolean().Should().BeFalse();
        await admin.PutAsJsonAsync("/api/alerts/settings", new { trendThresholdPercent = 15, cashVarianceNoteRequired = true });
    }
}
