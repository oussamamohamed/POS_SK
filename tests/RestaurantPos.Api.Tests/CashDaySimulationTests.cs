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
/// Journée de caisse scriptée (fond, espèces avec rendu, carte, annulations de reçus espèces/carte/fractionnés, annulation de ligne,
/// sorties/entrées, comptage avec écart, alertes <c>cash_variance</c> et <c>voids_count</c>) : l'oracle est écrit en dur,
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
        using (var userScope = _factory.Services.CreateScope())
        {
            var db = userScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = operatorId, Name = "Chef Caisse", Role = UserRole.Admin, PinHash = "x", PinSalt = "x" });
            await db.SaveChangesAsync();
        }
        var term = (await DeviceTestHelper.PairAsync(_factory, admin, "Caisse journée")).TerminalId;
        (await admin.PutAsJsonAsync("/api/alerts/rules/cash_variance", new { threshold = 200, enabled = true })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/alerts/rules/voids_count", new { threshold = 1, enabled = true })).EnsureSuccessStatusCode();

        var sessionId = (await (await admin.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 10_000 })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        async Task<(Guid OrderId, Guid ItemId)> SeedOrderAsync(string table, int qty, bool dispatched = false)
        {
            var id = Guid.NewGuid();
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new OrderItem
            {
                OrderId = id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = qty,
                UnitPrice = RestaurantPos.Domain.ValueObjects.Money.FromCents(1_950), TaxRatePercent = 10m, IsDispatched = dispatched,
            };
            db.Orders.Add(new Order { Id = id, TableNumber = table, Status = dispatched ? OrderStatus.SentToKitchen : OrderStatus.Open, OperatorId = operatorId, Items = { item } });
            await db.SaveChangesAsync();
            return (id, item.Id);
        }

        async Task<Guid> PayAsync(Guid orderId, string table, PaymentMethod method, long amountCents, long tenderedCents)
        {
            var resp = await admin.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
                orderId, table, operatorId, [new TenderItemRequest(method, amountCents / 100m, tenderedCents / 100m, Math.Max(0, tenderedCents - amountCents) / 100m)]));
            resp.EnsureSuccessStatusCode();
            var number = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("receiptNumber").GetString()!;
            using var scope = _factory.Services.CreateScope();
            return scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.ReceiptNumber == number).Id;
        }

        // J1 espèces + rendu, J2 carte, J3 espèces exact
        var (j1, _) = await SeedOrderAsync("J1", 1);
        await PayAsync(j1, "J1", PaymentMethod.Cash, 1_950, 2_000);
        var (j2, _) = await SeedOrderAsync("J2", 1);
        await PayAsync(j2, "J2", PaymentMethod.CreditCard, 1_950, 1_950);
        var (j3, _) = await SeedOrderAsync("J3", 1);
        await PayAsync(j3, "J3", PaymentMethod.Cash, 1_950, 1_950);

        // J4 espèces avec rendu (25 € remis pour 19,50 €) puis annulé -> baisse l'attendu de 19,50 € (jamais 25 €)
        var (j4, _) = await SeedOrderAsync("J4", 1);
        var r4 = await PayAsync(j4, "J4", PaymentMethod.Cash, 1_950, 2_500);
        (await admin.PostAsJsonAsync($"/api/checkout/void/{r4}", new RestaurantPos.Application.DTOs.VoidReceiptRequest(term, operatorId))).EnsureSuccessStatusCode();

        // J5 carte avec remise 50 % (> seuil 30 %) puis annulée en totalité -> 0 effet sur l'attendu espèces, franchit voids_count (> 1) ;
        // la règle discount_percent activée après l'annulation ne doit PAS lever de fausse alerte lors des évaluations suivantes (J6/J7).
        var (j5, _) = await SeedOrderAsync("J5", 1);
        (await admin.PostAsJsonAsync($"/api/orders/{j5}/discount", new ApplyOrderDiscountRequest(DiscountType.Percentage, 50m, "Erreur", operatorId))).EnsureSuccessStatusCode();
        var r5 = await PayAsync(j5, "J5", PaymentMethod.CreditCard, 975, 975);
        (await admin.PostAsJsonAsync($"/api/checkout/void/{r5}", new RestaurantPos.Application.DTOs.VoidReceiptRequest(term, operatorId))).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/alerts/rules/discount_percent", new { threshold = 30, enabled = true })).EnsureSuccessStatusCode();

        // J6 note partagée (15 € espèces avec rendu + 24 € carte) -> annulation part espèces puis re-règlement en carte
        var (j6, _) = await SeedOrderAsync("J6", 2);
        var r6Cash = await PayAsync(j6, "J6", PaymentMethod.Cash, 1_500, 2_000);
        await PayAsync(j6, "J6", PaymentMethod.CreditCard, 2_400, 2_400);
        (await admin.PostAsJsonAsync($"/api/checkout/void/{r6Cash}", new RestaurantPos.Application.DTOs.VoidReceiptRequest(term, operatorId))).EnsureSuccessStatusCode();
        await PayAsync(j6, "J6", PaymentMethod.CreditCard, 1_500, 1_500);

        // J7 annulation d'une ligne envoyée en cuisine avant règlement (2 -> 1 plat) puis encaissement espèces du reste (19,50 €)
        var (j7, j7Item) = await SeedOrderAsync("J7", 2);
        using (var kdsScope = _factory.Services.CreateScope())
        {
            var db = kdsScope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DiningTables.Add(new DiningTable { TableNumber = "J7", ActiveOrderId = j7, Status = TableStatus.Occupied, AssignedWaiterName = "Chef Caisse" });
            await db.SaveChangesAsync();
        }
        (await admin.PostAsync("/api/tables/J7/dispatch", null)).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/orders/{j7}/items/{j7Item}/void", new { quantity = 1, reason = "Erreur" })).EnsureSuccessStatusCode();
        await PayAsync(j7, "J7", PaymentMethod.Cash, 1_950, 1_950);

        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/movements", new { kind = "out", amountCents = 1_200, reason = "Achat de glace" })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/movements", new { kind = "in", amountCents = 500, reason = "Appoint de monnaie" })).EnsureSuccessStatusCode();

        var current = await admin.GetFromJsonAsync<JsonElement>("/api/cash/sessions/current");
        // 100 + 19,50 (J1) + 19,50 (J3) + 0 (J4 annulé) + 0 (J5 carte annulée) + 0 (J6 espèces annulé -> carte) + 19,50 (J7 après annulation ligne) − 12,00 + 5,00 = 151,50 €
        current.GetProperty("expectedCents").GetInt64().Should().Be(15_150);

        (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 14_700 })).StatusCode.Should().Be(HttpStatusCode.BadRequest); // écart −4,50 € > seuil 2,00 € : note obligatoire
        var counted = await (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 14_700, varianceNote = "Rendu erroné" })).Content.ReadFromJsonAsync<JsonElement>();
        counted.GetProperty("varianceCents").GetInt64().Should().Be(-450);
        var again = await (await admin.PostAsJsonAsync($"/api/cash/sessions/{sessionId}/count", new { countedCents = 1 })).Content.ReadFromJsonAsync<JsonElement>();
        again.GetProperty("varianceCents").GetInt64().Should().Be(-450); // comptage idempotent

        var open = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts?state=open");
        var termAlerts = open!.Where(a => a.GetProperty("terminalId").GetString() == term).ToList();
        termAlerts.Count(a => a.GetProperty("ruleType").GetString() == AlertRuleTypes.CashVariance).Should().Be(1);
        termAlerts.Where(a => a.GetProperty("ruleType").GetString() == AlertRuleTypes.DiscountPercent).Should().BeEmpty("J5 annulée ne lève pas d'alerte remise");
        var voidsAlert = termAlerts.Single(a => a.GetProperty("ruleType").GetString() == AlertRuleTypes.VoidsCount);
        JsonDocument.Parse(voidsAlert.GetProperty("detailJson").GetString()!).RootElement.GetProperty("count").GetInt32().Should().Be(3, "trois reçus annulés, une seule alerte dédupliquée");

        (await admin.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest(term, Guid.NewGuid(), "Gérant"))).EnsureSuccessStatusCode();
        var summary = await admin.GetFromJsonAsync<JsonElement>($"/api/dashboard/daily-summary?terminalId={term}");
        summary.GetProperty("totalSalesTtcCents").GetInt64().Should().Be(11_700); // J1+J2+J3+J6+J7 = 1950*4 + 3900
        summary.GetProperty("cash").GetProperty("varianceCents").GetInt64().Should().Be(-450);
        summary.GetProperty("openAlerts").GetArrayLength().Should().Be(2);

        // Le rapport Z imprimé porte le comptage (fond, compté, écart) : informatif, hors signature.
        using (var printScope = _factory.Services.CreateScope())
        {
            var data = await printScope.ServiceProvider.GetRequiredService<RestaurantPos.Infrastructure.Printing.ReportPrintDataService>()
                .BuildAsync(term, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddMinutes(1));
            data.Cash.Should().Be(new RestaurantPos.Application.Common.Interfaces.CashZSummary(10_000, 14_700, -450));
        }

        using var verify = _factory.Services.CreateScope();
        (await new FiscalChainVerificationService(verify.ServiceProvider.GetRequiredService<AppDbContext>()).VerifyAllChainsAsync()).IsValid.Should().BeTrue();
    }
}
