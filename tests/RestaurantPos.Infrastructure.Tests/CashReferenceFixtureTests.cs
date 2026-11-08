using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad : le Swift rejoue ces scénarios de caisse (espèces avec rendu, avoirs, mouvements, comptage) et de seuils d'alerte et
/// doit obtenir les mêmes attendus, écarts et alertes. Régénérer avec
/// <c>WRITE_CASH_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter CashReferenceFixtureTests</c> après toute modification de
/// <c>CashSessionService</c> ou <c>AlertService</c>.
/// </summary>
public class CashReferenceFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Guid Op = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Op2 = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    private sealed record Tender(string Method, long Amount, long Tendered);

    private sealed record Receipt(bool Credit, List<Tender> Tenders);

    private sealed record Movement(string Kind, long Amount, string Reason);

    private sealed record CashCase(string Name, long OpeningFloat, List<Receipt> Receipts, List<Movement> Movements, long Counted, bool NoteRequiredThreshold, long Threshold, string? Note,
        long? ExpectedCents, long? VarianceCents, string? Error);

    private sealed record Sale(string Operator, bool Credit, long TotalCents, string? DiscountType, decimal DiscountValue, long SubtotalCents, long CompCents, int LineDiscountPercent = 0);

    private sealed record AlertCase(string Name, string RuleType, long Threshold, List<Sale> Sales, List<string> ExpectedAlerts);

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "cash_reference.json");
    }

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("CashRef_" + Guid.NewGuid().ToString("N")).Options);

    private static async Task<CashCase> RunCashAsync(string name, long floatCents, List<Receipt> receipts, List<Movement> movements, long counted, long threshold, string? note)
    {
        using var db = NewDb();
        var alerts = new AlertService(db);
        await alerts.SetRuleAsync(AlertRuleTypes.CashVariance, threshold, threshold > 0, null);
        var svc = new CashSessionService(db, alerts);
        var session = await svc.OpenAsync("T01", Op, floatCents);
        foreach (var r in receipts)
        {
            var receipt = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = Guid.NewGuid(), VoidedReceiptId = r.Credit ? Guid.NewGuid() : null };
            foreach (var t in r.Tenders)
                receipt.Tenders.Add(new PaymentTender { Method = Enum.Parse<PaymentMethod>(t.Method), Amount = Money.FromCents(t.Amount), Tendered = Money.FromCents(t.Tendered), ChangeGiven = Money.FromCents(t.Method == "Cash" ? Math.Max(0, t.Tendered - t.Amount) : 0) });
            db.FiscalReceipts.Add(receipt);
        }
        await db.SaveChangesAsync();
        foreach (var m in movements) await svc.AddMovementAsync(session.Id, m.Kind, m.Amount, m.Reason, Op);
        try
        {
            var result = (await svc.CountAsync(session.Id, counted, null, note, Op))!;
            return new CashCase(name, floatCents, receipts, movements, counted, threshold > 0, threshold, note, result.ExpectedCents, result.VarianceCents, null);
        }
        catch (CashException ex)
        {
            return new CashCase(name, floatCents, receipts, movements, counted, threshold > 0, threshold, note, null, null, ex.Code);
        }
    }

    private static async Task<AlertCase> RunAlertsAsync(string name, string ruleType, long threshold, List<Sale> sales)
    {
        using var db = NewDb();
        var alerts = new AlertService(db);
        await alerts.SetRuleAsync(ruleType, threshold, true, null);
        var operators = new Dictionary<string, Guid> { ["A"] = Op, ["B"] = Op2 };
        foreach (var s in sales)
        {
            var order = new Order { OperatorId = operators[s.Operator], Status = OrderStatus.Paid, TableNumber = "1" };
            if (s.DiscountType is not null) { order.GlobalDiscountType = Enum.Parse<DiscountType>(s.DiscountType); order.GlobalDiscountValue = s.DiscountValue; }
            order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(s.SubtotalCents), TaxRatePercent = 10m, IsComp = s.CompCents > 0, DiscountPercent = s.LineDiscountPercent });
            db.Orders.Add(order);
            db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = order.Id, TotalTtcAmount = Money.FromCents(s.TotalCents), VoidedReceiptId = s.Credit ? Guid.NewGuid() : null });
        }
        await db.SaveChangesAsync();

        await alerts.EvaluateAsync();
        await alerts.EvaluateAsync(); // rejouer ne crée aucun doublon
        var raised = (await alerts.ListAsync(null, null, null)).Select(a => $"{a.RuleType}|{(a.OperatorId == Op ? "A" : "B")}|{a.PeriodKey}").Order(StringComparer.Ordinal).ToList();
        return new AlertCase(name, ruleType, threshold, sales, raised);
    }

    private static Receipt Cash(long amount, long tendered, bool credit = false) => new(credit, [new Tender("Cash", amount, tendered)]);

    [Fact]
    public async Task Fixture_MatchesTheServices()
    {
        var cases = new List<CashCase>
        {
            await RunCashAsync("espece_avec_rendu", 10_000, [Cash(5_000, 6_000)], [], 15_000, 0, null),
            await RunCashAsync("mixte_carte_et_especes", 10_000, [new Receipt(false, [new Tender("CreditCard", 3_000, 3_000), new Tender("Cash", 1_000, 1_000)])], [], 11_000, 0, null),
            await RunCashAsync("avoir_especes", 20_000, [Cash(5_000, 5_000), Cash(-2_000, -2_000, credit: true)], [], 23_000, 0, null),
            await RunCashAsync("sorties_et_entrees", 10_000, [Cash(5_000, 5_000)], [new("out", 1_200, "Achat de glace"), new("in", 500, "Appoint")], 14_300, 0, null),
            await RunCashAsync("ecart_negatif", 10_000, [Cash(5_000, 5_000)], [new("out", 1_200, "Achat de glace")], 13_000, 0, null),
            await RunCashAsync("ecart_positif", 0, [Cash(1_234, 2_000)], [], 1_500, 0, null),
            await RunCashAsync("sans_fond", 0, [], [], 0, 0, null),
            await RunCashAsync("note_requise", 10_000, [], [], 8_000, 1_000, null),
            await RunCashAsync("note_fournie", 10_000, [], [], 8_000, 1_000, "Rendu erroné"),
            await RunCashAsync("sous_le_seuil_sans_note", 10_000, [], [], 9_500, 1_000, null),
        };
        var alertCases = new List<AlertCase>
        {
            await RunAlertsAsync("annulations_au_dessus_du_seuil", "voids_count", 3, [.. Enumerable.Range(0, 4).Select(_ => new Sale("A", true, -1_000, null, 0, 1_000, 0)), new Sale("B", true, -1_000, null, 0, 1_000, 0)]),
            await RunAlertsAsync("annulations_au_seuil_exact", "voids_count", 3, [.. Enumerable.Range(0, 3).Select(_ => new Sale("A", true, -1_000, null, 0, 1_000, 0))]),
            await RunAlertsAsync("remise_au_dessus", "discount_percent", 30, [new Sale("A", false, 600, "Percentage", 40m, 1_000, 0), new Sale("B", false, 900, "Percentage", 10m, 1_000, 0)]),
            await RunAlertsAsync("remise_par_ligne", "discount_percent", 30, [new Sale("A", false, 500, null, 0, 1_000, 0, 50), new Sale("B", false, 800, null, 0, 1_000, 0, 20)]),
            await RunAlertsAsync("remise_fixe_en_pourcentage", "discount_percent", 30, [new Sale("A", false, 500, "FixedAmount", 5m, 1_000, 0)]),
            await RunAlertsAsync("offerts_cumules", "comps_total", 5_000, [new Sale("A", false, 0, null, 0, 3_000, 3_000), new Sale("A", false, 0, null, 0, 3_000, 3_000), new Sale("B", false, 0, null, 0, 2_000, 2_000)]),
            await RunAlertsAsync("commande_offerte_entiere", "comps_total", 1_000, [new Sale("A", false, 0, "Comp", 0m, 2_500, 0)]),
        };

        var json = JsonSerializer.Serialize(new { cashCases = cases, alertCases }, JsonOptions) + "\n";
        var path = FixturePath();
        if (Environment.GetEnvironmentVariable("WRITE_CASH_FIXTURE") == "1") File.WriteAllText(path, json);
        File.Exists(path).Should().BeTrue("générer la fixture avec WRITE_CASH_FIXTURE=1");
        File.ReadAllText(path).Should().Be(json, "le service a changé : régénérer la fixture et reporter le changement dans Swift");
        cases.Single(c => c.Name == "note_requise").Error.Should().Be("variance_note_required");
        alertCases.Single(c => c.Name == "annulations_au_seuil_exact").ExpectedAlerts.Should().BeEmpty();
    }
}
