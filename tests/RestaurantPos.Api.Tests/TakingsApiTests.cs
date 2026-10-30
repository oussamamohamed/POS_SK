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
        body.GetProperty("series").GetArrayLength().Should().Be(24);
        body.GetProperty("granularity").GetString().Should().Be("hour");
        foreach (var field in new[] { "compareByNowTtc", "spendPerCover", "tipsTotalTtc", "discountsTtc", "compsTtc", "voidsCount" })
        {
            body.TryGetProperty(field, out _).Should().BeTrue(field);
        }
        foreach (var field in new[] { "paymentMethods", "categories", "topProducts", "vat", "tipsByServer" })
        {
            body.GetProperty(field).ValueKind.Should().Be(JsonValueKind.Array, field);
        }
    }

    [Fact]
    public async Task Rejects_an_inverted_or_oversized_period_and_supports_the_previous_comparison()
    {
        var from = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        string Q(DateTimeOffset a, DateTimeOffset b, string extra = "") => $"/api/dashboard/takings?from={Uri.EscapeDataString(a.ToString("O"))}&to={Uri.EscapeDataString(b.ToString("O"))}{extra}";

        (await ClientAs("Admin").GetAsync(Q(from, from))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ClientAs("Admin").GetAsync(Q(from, from.AddDays(120)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var week = await (await ClientAs("Admin").GetAsync(Q(from.AddDays(-7), from, "&compare=previous"))).Content.ReadFromJsonAsync<JsonElement>();
        week.GetProperty("granularity").GetString().Should().Be("day");
        week.GetProperty("compare").GetString().Should().Be("previous");
        week.GetProperty("series").GetArrayLength().Should().Be(7);
    }

    [Fact]
    public async Task Receipts_and_journal_are_for_managers_only_and_the_journal_covers_today_only()
    {
        var from = new DateTimeOffset(DateTimeOffset.UtcNow.Date, TimeSpan.Zero);
        var receiptsUrl = $"/api/dashboard/receipts?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(from.AddDays(1).ToString("O"))}";
        (await ClientAs(null).GetAsync(receiptsUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientAs("Waiter").GetAsync(receiptsUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ClientAs("Waiter").GetAsync("/api/dashboard/journal")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await ClientAs("Admin").GetAsync(receiptsUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
        var journal = await ClientAs("Admin").GetAsync("/api/dashboard/journal?tzOffsetMinutes=60");
        journal.StatusCode.Should().Be(HttpStatusCode.OK);
        (await journal.Content.ReadFromJsonAsync<JsonElement>()).ValueKind.Should().Be(JsonValueKind.Array);
    }
}
