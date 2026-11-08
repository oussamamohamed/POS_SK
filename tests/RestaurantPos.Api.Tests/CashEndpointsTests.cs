using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class CashEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public CashEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role, string? operatorId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (operatorId is not null) client.DefaultRequestHeaders.Add("X-Test-Operator-Id", operatorId);
        return client;
    }

    private static string NewOperator() => Guid.NewGuid().ToString();

    [Fact]
    public async Task WriteRoutes_RequireADeviceToken()
    {
        var client = Client("Waiter");

        var response = await client.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 100 });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task FullFlow_TerminalComesFromTheDevice_ExpectedIsLive_AndCountIsIdempotent()
    {
        var waiter = Client("Waiter", NewOperator());
        var device = await DeviceTestHelper.PairAsync(_factory, waiter, "Caisse fond");

        var open = await waiter.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 10_000, terminalId = "IGNORE" });
        open.StatusCode.Should().Be(HttpStatusCode.Created);
        var session = await open.Content.ReadFromJsonAsync<JsonElement>();
        session.GetProperty("terminalId").GetString().Should().Be(device.TerminalId);
        var id = session.GetProperty("id").GetGuid();

        (await waiter.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 1 })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/movements", new { kind = "out", amountCents = 1_200, reason = "Achat de glace" })).StatusCode.Should().Be(HttpStatusCode.Created);
        (await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/movements", new { kind = "out", amountCents = 100, reason = "x" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var current = await waiter.GetFromJsonAsync<JsonElement>("/api/cash/sessions/current");
        current.GetProperty("expectedCents").GetInt64().Should().Be(8_800);

        var count = await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/count", new { countedCents = 8_600, denominations = new[] { new { valueCents = 2_000, quantity = 4 }, new { valueCents = 100, quantity = 6 } } });
        count.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await count.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("varianceCents").GetInt64().Should().Be(-200);
        var again = await (await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/count", new { countedCents = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("varianceCents").GetInt64().Should().Be(-200);
        (await waiter.GetAsync("/api/cash/sessions/current")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task AnotherWaiter_CannotSeeOrTouchTheSession_ButTheManagerCan()
    {
        var owner = Client("Waiter", NewOperator());
        var device = await DeviceTestHelper.PairAsync(_factory, owner, "Caisse partagée");
        var id = (await (await owner.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 500 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var other = Client("Waiter", NewOperator());
        other.DefaultRequestHeaders.Add("X-Device-Token", owner.DefaultRequestHeaders.GetValues("X-Device-Token").First());
        (await other.GetAsync("/api/cash/sessions/current")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await other.PostAsJsonAsync($"/api/cash/sessions/{id}/movements", new { kind = "in", amountCents = 100, reason = "Appoint" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var manager = Client("Admin", NewOperator());
        manager.DefaultRequestHeaders.Add("X-Device-Token", owner.DefaultRequestHeaders.GetValues("X-Device-Token").First());
        (await manager.GetAsync("/api/cash/sessions/current")).StatusCode.Should().Be(HttpStatusCode.OK);
        device.TerminalId.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task History_IsManagerOnly()
    {
        (await Client("Waiter").GetAsync("/api/cash/sessions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Client("Admin").GetAsync("/api/cash/sessions")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task EveryStep_IsInTheJournal()
    {
        var waiter = Client("Waiter", NewOperator());
        await DeviceTestHelper.PairAsync(_factory, waiter, "Caisse journal");
        var id = (await (await waiter.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 500 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/movements", new { kind = "out", amountCents = 100, reason = "Achat de glace" });
        await waiter.PostAsJsonAsync($"/api/cash/sessions/{id}/count", new { countedCents = 400 });

        using var scope = _factory.Services.CreateScope();
        var types = scope.ServiceProvider.GetRequiredService<AppDbContext>().JournalEntries.Select(j => j.EventType).ToList();
        types.Should().Contain([JournalEventTypes.CashSessionOpened, JournalEventTypes.CashMovement, JournalEventTypes.CashCounted]);
    }
}
