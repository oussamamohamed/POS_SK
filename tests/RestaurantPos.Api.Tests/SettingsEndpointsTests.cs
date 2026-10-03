using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
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
    public async Task KitchenLanguage_RoundTrips_AndDefaultsToReceiptLanguage()
    {
        var admin = await ClientAsync("9999");
        var initial = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        initial.GetProperty("kitchenTicketLanguage").GetString().Should().Be(initial.GetProperty("receiptLanguage").GetString());

        (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "ar"))).StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        body.GetProperty("kitchenTicketLanguage").GetString().Should().Be("ar");
    }

    [Fact]
    public async Task Put_WithoutKitchenLanguage_KeepsIt()
    {
        var admin = await ClientAsync("9999");
        await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "ar"));
        (await admin.PutAsJsonAsync("/api/settings", new { receiptLanguage = "en" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        body.GetProperty("receiptLanguage").GetString().Should().Be("en");
        body.GetProperty("kitchenTicketLanguage").GetString().Should().Be("ar");
    }

    [Fact]
    public async Task Put_InvalidKitchenLanguage_Returns400()
    {
        var admin = await ClientAsync("9999");
        (await admin.PutAsJsonAsync("/api/settings", new UpdateRestaurantSettingsRequest("fr", "de"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
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

    [Fact]
    public async Task Put_PartialUpdate_PreservesOtherFields()
    {
        var admin = await ClientAsync("9999");
        var initial = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        var initialSiret = initial.GetProperty("siret").GetString();

        var res = await admin.PutAsJsonAsync("/api/settings", new { companyName = "Le Petit Bistro" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await admin.GetFromJsonAsync<JsonElement>("/api/settings");
        body.GetProperty("companyName").GetString().Should().Be("Le Petit Bistro");
        body.GetProperty("siret").GetString().Should().Be(initialSiret);
    }

    [Fact]
    public async Task Put_InvalidSiret_Returns400()
    {
        var admin = await ClientAsync("9999");
        var res = await admin.PutAsJsonAsync("/api/settings", new { siret = "12345" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_InvalidVat_Returns400()
    {
        var admin = await ClientAsync("9999");
        var res = await admin.PutAsJsonAsync("/api/settings", new { vatNumber = "US123456789" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_InvalidFiscalDate_Returns400()
    {
        var admin = await ClientAsync("9999");
        var resMonth = await admin.PutAsJsonAsync("/api/settings", new { fiscalYearStartMonth = 13 });
        resMonth.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var resDay = await admin.PutAsJsonAsync("/api/settings", new { fiscalYearStartDay = 30 });
        resDay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_FiscalYearLocked_WhenAnnualClosureExists_Returns409()
    {
        var admin = await ClientAsync("9999");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.PeriodClosures.Add(new FiscalPeriodClosure
            {
                TerminalId = "T01",
                PeriodType = FiscalPeriodType.Annual,
                PeriodKey = "2025",
                ClosureSequence = 1,
                PeriodStartUtc = DateTimeOffset.UtcNow.AddYears(-1),
                PeriodEndUtc = DateTimeOffset.UtcNow,
                DailyClosureCount = 365,
                PreviousSignatureHash = "genesis",
                SignatureHash = "sig2025"
            });
            await db.SaveChangesAsync();
        }

        var res = await admin.PutAsJsonAsync("/api/settings", new { fiscalYearStartMonth = 4 });
        res.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("fiscal_year_locked");
    }

    [Fact]
    public async Task Put_ValidSettings_WritesJetEvent()
    {
        var admin = await ClientAsync("9999");
        var res = await admin.PutAsJsonAsync("/api/settings", new { companyName = "Restaurant Test JET" });
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entry = await db.JournalEntries
            .OrderByDescending(j => j.ChainSequence)
            .FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.FiscalSettingsChanged);

        entry.Should().NotBeNull();
        entry!.ChainSequence.Should().HaveValue();
        entry.PayloadJson.Should().Contain("Restaurant Test JET");
    }
}
