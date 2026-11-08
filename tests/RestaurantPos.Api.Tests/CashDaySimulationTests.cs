using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

/// <summary>
/// Journée de caisse scriptée (fond, espèces avec rendu, carte, sortie, entrée, comptage avec écart) : l'oracle est écrit en dur,
/// identique dans <c>LocalCashDayTests</c> côté iPad (spec 024, SC-001/SC-002/SC-003).
/// </summary>
public class CashDaySimulationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public CashDaySimulationTests(PosApiApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ScriptedDay_GivesTheOracleExpectedVarianceOneAlertAndAValidChain()
    {
        var operatorId = Guid.NewGuid();
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        admin.DefaultRequestHeaders.Add("X-Test-Operator-Id", operatorId.ToString());
        var term = (await DeviceTestHelper.PairAsync(_factory, admin, "Caisse journée")).TerminalId;
        (await admin.PutAsJsonAsync("/api/alerts/rules/cash_variance", new { threshold = 200, enabled = true })).EnsureSuccessStatusCode();

        var sessionId = (await (await admin.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 10_000 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var n = 0;
        foreach (var (cents, method, tendered) in new[] { (1_950L, PaymentMethod.Cash, 2_000L), (1_950L, PaymentMethod.CreditCard, 1_950L), (1_950L, PaymentMethod.Cash, 1_950L) })
        {
            var id = Guid.NewGuid();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order { Id = id, TableNumber = "J" + ++n, Status = OrderStatus.Open, OperatorId = operatorId, Items = { new OrderItem { OrderId = id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = RestaurantPos.Domain.ValueObjects.Money.FromCents(cents), TaxRatePercent = 10m } } });
            await db.SaveChangesAsync();
            (await admin.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(id, null, cents, [new SyncTenderDto(method, cents, tendered)], null))).EnsureSuccessStatusCode();
        }
        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/movements", new { kind = "out", amountCents = 1_200, reason = "Achat de glace" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/movements", new { kind = "in", amountCents = 500, reason = "Appoint de monnaie" })).EnsureSuccessStatusCode();

        var current = await admin.GetFromJsonAsync<JsonElement>("/api/cash/sessions/current");
        current.GetProperty("expectedCents").GetInt64().Should().Be(13_200); // 100 + 19,50 + 19,50 − 12,00 + 5,00 (le rendu n'est pas du fond)

        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 12_750 })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // écart −4,50 € > seuil 2,00 € : note obligatoire
        var counted = await (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 12_750, varianceNote = "Rendu erroné" })).Content.ReadFromJsonAsync<JsonElement>();
        counted.GetProperty("varianceCents").GetInt64().Should().Be(-450);
        var again = await (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("varianceCents").GetInt64().Should().Be(-450); // comptage idempotent

        var open = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts?state=open");
        open!.Count(a => a.GetProperty("ruleType").GetString() == AlertRuleTypes.CashVariance && a.GetProperty("terminalId").GetString() == term).Should().Be(1);

        (await admin.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest(term, Guid.NewGuid(), "Gérant"))).EnsureSuccessStatusCode();
        var summary = await admin.GetFromJsonAsync<JsonElement>($"/api/dashboard/daily-summary?terminalId={term}");
        summary.GetProperty("totalSalesTtcCents").GetInt64().Should().Be(5_850);
        summary.GetProperty("cash").GetProperty("varianceCents").GetInt64().Should().Be(-450);

        // Le rapport Z imprimé porte le comptage (fond, compté, écart) : informatif, hors signature.
        using (var printScope = _factory.Services.CreateScope())
        {
            var data = await printScope.ServiceProvider.GetRequiredService<RestaurantPos.Infrastructure.Printing.ReportPrintDataService>()
                .BuildAsync(term, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            data.Cash.Should().Be(new RestaurantPos.Application.Common.Interfaces.CashZSummary(10_000, 12_750, -450));
        }

        using var verify = _factory.Services.CreateScope();
        (await new FiscalChainVerificationService(verify.ServiceProvider.GetRequiredService<AppDbContext>()).VerifyAllChainsAsync()).IsValid.Should().BeTrue();
    }
}
