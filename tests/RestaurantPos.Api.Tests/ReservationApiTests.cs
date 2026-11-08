using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Api;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Tests;

/// <summary>Réservations : accès réservé aux opérateurs connectés, téléphone réservé aux responsables, refus métier localisés.</summary>
public class ReservationApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public ReservationApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientAs(string? role)
    {
        var client = _factory.CreateClient();
        if (role is not null) client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<string> NewTable(int capacity = 4)
    {
        var number = "R" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        (await ClientAs("Admin").PostAsJsonAsync("/api/tables", new CreateTableRequest(number, capacity, 0, 0))).EnsureSuccessStatusCode();
        return number;
    }

    private static ReservationRequest Booking(string? table, DateTimeOffset? at = null, string? phone = null, bool waitlist = false) =>
        new("Dupont", 2, at ?? DateTimeOffset.UtcNow.AddHours(2), 90, table, "Anniversaire", phone, waitlist);

    [Fact]
    public async Task Requires_an_authenticated_operator()
    {
        (await ClientAs(null).GetAsync("/api/reservations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ClientAs(null).PostAsJsonAsync("/api/reservations", Booking(null, waitlist: true))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Phone_is_returned_to_managers_only()
    {
        var table = await NewTable();
        var created = await ClientAs("Waiter").PostAsJsonAsync("/api/reservations", Booking(table, phone: "+33 6 12 34 56 78"));
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetGuid();
        body.GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("hasPhone").GetBoolean().Should().BeTrue();

        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(2).ToString("O"));
        var asWaiter = await ClientAs("Waiter").GetFromJsonAsync<List<JsonElement>>($"/api/reservations?from={from}&to={to}");
        asWaiter!.Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("phone").ValueKind.Should().Be(JsonValueKind.Null);
        var asAdmin = await ClientAs("Admin").GetFromJsonAsync<List<JsonElement>>($"/api/reservations?from={from}&to={to}");
        asAdmin!.Single(r => r.GetProperty("id").GetGuid() == id).GetProperty("phone").GetString().Should().Be("+33 6 12 34 56 78");
    }

    [Fact]
    public async Task Overlap_on_the_same_table_returns_409_with_a_localized_message()
    {
        var table = await NewTable();
        var at = DateTimeOffset.UtcNow.AddHours(3);
        (await ClientAs("Waiter").PostAsJsonAsync("/api/reservations", Booking(table, at))).EnsureSuccessStatusCode();

        var clash = await ClientAs("Waiter").PostAsJsonAsync("/api/reservations", Booking(table, at.AddMinutes(30)));

        clash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await clash.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()
            .Should().Be(Texts.Get(new System.Globalization.CultureInfo("en"), "errors.reservation_tableconflict"));
    }

    [Fact]
    public async Task Invalid_input_returns_400_and_unknown_reservation_404()
    {
        var bad = await ClientAs("Waiter").PostAsJsonAsync("/api/reservations", Booking(null, waitlist: true) with { GuestName = " " });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var missing = await ClientAs("Waiter").PostAsJsonAsync($"/api/reservations/{Guid.NewGuid()}/status", new ReservationStatusRequest(RestaurantPos.Domain.Entities.ReservationStatus.Cancelled));
        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Seating_opens_the_table_and_marks_the_reservation_seated()
    {
        var table = await NewTable();
        var created = await (await ClientAs("Waiter").PostAsJsonAsync("/api/reservations", Booking(table))).Content.ReadFromJsonAsync<JsonElement>();
        var id = created.GetProperty("id").GetGuid();

        var seat = await ClientAs("Waiter").PostAsJsonAsync($"/api/reservations/{id}/seat", new SeatReservationRequest(null, null, "Sophie"));

        seat.StatusCode.Should().Be(HttpStatusCode.OK);
        (await seat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetInt32().Should().Be(1);
        var tables = await ClientAs("Waiter").GetFromJsonAsync<List<JsonElement>>("/api/tables");
        tables!.Single(t => t.GetProperty("tableNumber").GetString() == table).GetProperty("status").GetInt32().Should().Be(1);
    }
}
