using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Rejoue une seule fois la journée de la graine par défaut ; les tests en lecture seule la partagent.</summary>
public sealed class DayFixture : IAsyncLifetime
{
    public PosApiApplicationFactory Factory { get; } = new();
    public DayScenario Scenario { get; } = DayScenarioGenerator.Build(DayScenarioGenerator.DefaultSeed);
    public DayRunResult Run { get; private set; } = null!;
    public DayLedger Ledger { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Run = await DayRunner.RunAsync(Factory, Scenario);
        Ledger = DayLedgerOracle.Compute(Scenario.Operations, Run.TerminalIds);
    }

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Journée simulée complète rejouée par l'API réelle. Chaque test vérifie un point de la spec contre le grand livre indépendant
/// (<see cref="DayLedgerOracle"/>) ; tous les messages d'échec portent la graine, l'index d'opération et le poste.
/// </summary>
public class DaySimulationTests(DayFixture day) : IClassFixture<DayFixture>
{
    // ---------- cohérence du générateur (sans serveur) ----------

    [Fact]
    public void TheGenerator_IsDeterministic_AndRespectsTheShapeOfTheSpec()
    {
        var a = DayScenarioGenerator.Build(DayScenarioGenerator.DefaultSeed);
        var b = DayScenarioGenerator.Build(DayScenarioGenerator.DefaultSeed);
        Json(a).Should().Be(Json(b), "même graine, même journée");
        Json(DayScenarioGenerator.Build(7)).Should().NotBe(Json(a), "une autre graine donne une autre journée");

        a.Terminals.Select(t => t.Key).Should().Equal("A", "B", "C");
        var orders = a.Operations.Where(o => o.Type is "table_order" or "counter_order").ToList();
        orders.Select(o => o.Waiter).Distinct().Should().HaveCountLessThanOrEqualTo(4);
        orders.Count(o => o.Ref!.StartsWith('m')).Should().Be(4, "quatre cas imposés en tête");
        a.Operations.Take(4).Select(o => o.Ref).Should().Equal("m0", "m1", "m2", "m3");

        int firstZ = FirstZ(a);
        a.Operations.Select((o, i) => (o, i)).Where(x => x.o.Type is "table_order" or "counter_order" or "hold_then_void").Should()
            .OnlyContain(x => x.i < firstZ, "toutes les commandes sont soldées ou annulées avant la première Z");
        a.Operations.Count(o => o.Type == "z_closure").Should().Be(3);
        a.Operations.Count(o => o.Type == "x_report").Should().BeGreaterThanOrEqualTo(3 * 3, "un X par poste toutes les ~12 opérations, avant la Z et après");

        foreach (var order in orders)
        {
            long total = DayLedgerOracle.OrderTotalCents(order);
            long paid = order.Payments!.Sum(p => p.Sum(t => t.AmountCents));
            paid.Should().Be(total + order.TipCents, $"commande {order.Ref} soldée : règlements = total + pourboire");
            order.Payments!.Should().HaveCountLessThanOrEqualTo(5);
            if (order.VoidPaymentIndex is { } index)
            {
                index.Should().BeInRange(0, order.Payments!.Count - 1);
            }
        }

        a.Operations.Select(o => o.Type).Should().Contain(["table_order", "counter_order", "hold_then_void", "void_receipt"]);
        orders.Should().Contain(o => o.Discount != null && o.Discount.Type == "Percentage").And.Contain(o => o.Discount != null && o.Discount.Type == "FixedAmount");
        orders.Should().Contain(o => o.Reprint).And.Contain(o => o.VoidPaymentIndex != null).And.Contain(o => o.HoldAndRecall);
        orders.Should().Contain(o => o.TipCents > 0).And.Contain(o => o.Payments!.Count >= 3);
    }

    // ---------- X : égal au grand livre, sans effet de bord ----------

    [Fact]
    public async Task LedgerMatchesX_BeforeAnyClosure_AndXIsReadOnly()
    {
        var full = day.Scenario;
        var partial = full with { Operations = full.Operations.Take(FirstZ(full)).ToList() };
        using var factory = new PosApiApplicationFactory();
        var run = await DayRunner.RunAsync(factory, partial);

        AssertReports(partial, run); // chaque X pris en cours de journée = grand livre des opérations déjà jouées

        var before = await CountsAsync(factory);
        foreach (var (key, id) in run.TerminalIds)
        {
            string because = Ctx(partial.Seed, partial.Operations.Count, key, "X final");
            var expected = LedgerAt(partial, run.TerminalIds, key, partial.Operations.Count);
            var first = await GetXAsync(run, key, id);
            var second = await GetXAsync(run, key, id);
            DayAssert.SummaryEquals(first, expected, because);
            DayAssert.SummariesEqual(second, first, $"{because} : deux X consécutifs");
        }

        (await CountsAsync(factory)).Should().Be(before, "un X ne crée ni reçu, ni clôture, ni écriture du journal");
    }

    // ---------- Z refusée tant qu'une commande est ouverte ----------

    [Fact]
    public async Task ZIsRefusedWhileAnOrderIsOpen_ThenAccepted()
    {
        var scenario = new DayScenario(11,
            [new("A", "Caisse A"), new("B", "Caisse B")],
            [new("table_order", "A", "waiter1", "5", 2, "EatIn", [new("Menu", 2, 2000, 10m)], null, 0, [[new("Cash", 4000)]], Ref: "o0")]);
        using var factory = new PosApiApplicationFactory();
        var run = await DayRunner.RunAsync(factory, scenario);
        var m = run.Manager;
        var clientB = run.Clients["B"];

        // une table reste ouverte sur l'AUTRE poste : la Z de A est refusée quand même
        var opened = await DayRunner.SendAsync(clientB, HttpMethod.Post, "/api/tables/9/open", new OpenTableRequest(m.Name, 2, m.Id), m.Token);
        var orderId = opened.GetProperty("activeOrderId").GetGuid();
        await DayRunner.SendAsync(clientB, HttpMethod.Post, "/api/tables/9/items", new AddOrderItemsRequest(
            [new OrderItemInputDto(Guid.NewGuid(), "Plat", 1, 10m, 10m, null, null)]), m.Token);
        var zRequest = new ZClosureRequest(run.TerminalIds["A"], m.Id, m.Name);

        var (status, body) = await DayRunner.TrySendAsync(run.Clients["A"], HttpMethod.Post, "/api/fiscal/z-closure", zRequest, m.Token);
        status.Should().Be(409, Ctx(11, 1, "A", "Z avec une commande ouverte sur B"));
        body.GetProperty("code").GetString().Should().Be("open_orders");
        body.GetProperty("openOrders").GetArrayLength().Should().Be(1);
        body.GetProperty("openOrders")[0].GetProperty("tableNumber").GetString().Should().Be("9");
        body.GetProperty("openOrders")[0].GetProperty("remainingTtc").GetDecimal().Should().Be(10m);
        (await CountsAsync(factory)).Closures.Should().Be(0, "la Z refusée n'écrit rien");

        // la table est soldée : la Z passe, et elle égale le grand livre
        await DayRunner.SendAsync(clientB, HttpMethod.Post, "/api/checkout/pay", new PaymentSettlementRequest(
            orderId, "9", m.Id, [new TenderItemRequest(PaymentMethod.Cash, 10m, 10m, 0m)]), m.Token);
        var z = await DayRunner.SendAsync(run.Clients["A"], HttpMethod.Post, "/api/fiscal/z-closure", zRequest, m.Token);
        var ledger = DayLedgerOracle.Compute(scenario.Operations, run.TerminalIds);
        DayAssert.SummaryEquals(z, ledger.ByTerminal["T01"], Ctx(11, 2, "A", "Z acceptée"));
    }

    // ---------- Z = grand livre, poste par poste ----------

    [Fact]
    public void ZEqualsTheLedger_PerTerminal()
    {
        AssertReports(day.Scenario, day.Run);
        foreach (var (key, id) in day.Run.TerminalIds)
        {
            var z = day.Run.ZClosures[key].Single();
            var expected = day.Ledger.ByTerminal[id];
            string because = Ctx(day.Scenario.Seed, day.Run.ZClosureOpIndexes[key].Single(), key, "Z");
            DayAssert.SummaryEquals(z, expected, because);

            long vat = z.GetProperty("vatBreakdown").EnumerateObject().Sum(p => DayAssert.Cents(p.Value));
            (DayAssert.Cents(z.GetProperty("totalSalesTtc")) - DayAssert.Cents(z.GetProperty("totalSalesHt"))).Should().Be(vat, $"{because} : TTC - HT = somme des TVA");
            long tenders = z.GetProperty("paymentTotals").EnumerateObject().Sum(p => DayAssert.Cents(p.Value));
            tenders.Should().Be(DayAssert.Cents(z.GetProperty("totalSalesTtc")) + NetTipsCents(day.Scenario, key), $"{because} : somme des moyens = TTC + pourboires conservés");
        }
    }

    // ---------- X juste avant Z = Z ; X après Z repart de zéro ----------

    [Fact]
    public void XJustBeforeZ_EqualsZ_AndXAfterZStartsAtZero()
    {
        foreach (var (key, _) in day.Run.TerminalIds)
        {
            int zIndex = day.Run.ZClosureOpIndexes[key].Single();
            var xs = day.Run.XReports[key];
            var xIndexes = day.Run.XReportOpIndexes[key];

            int before = xIndexes.FindLastIndex(i => i < zIndex);
            before.Should().BeGreaterThanOrEqualTo(0, Ctx(day.Scenario.Seed, zIndex, key, "un X précède la Z"));
            DayAssert.SummariesEqual(xs[before], day.Run.ZClosures[key].Single(), Ctx(day.Scenario.Seed, xIndexes[before], key, "X juste avant la Z"));

            int after = xIndexes.FindIndex(i => i > zIndex);
            after.Should().BeGreaterThanOrEqualTo(0, Ctx(day.Scenario.Seed, zIndex, key, "un X suit la Z"));
            string because = Ctx(day.Scenario.Seed, xIndexes[after], key, "X après la Z");
            DayAssert.IsEmpty(xs[after], because);
            DayAssert.Cents(xs[after].GetProperty("perpetualGrandTotal")).Should().Be(
                DayAssert.Cents(day.Run.ZClosures[key].Single().GetProperty("perpetualGrandTotal")), $"{because} : le grand total perpétuel est conservé");
        }
    }

    // ---------- les postes s'additionnent ----------

    [Fact]
    public void TerminalsSumToTheRoom_AndPerpetualTotalGrowsByTheDayTtc()
    {
        long sum = 0;
        long ht = 0;
        int receipts = 0;
        foreach (var (key, _) in day.Run.TerminalIds)
        {
            var z = day.Run.ZClosures[key].Single();
            long ttc = DayAssert.Cents(z.GetProperty("totalSalesTtc"));
            string because = Ctx(day.Scenario.Seed, day.Run.ZClosureOpIndexes[key].Single(), key, "Z");
            DayAssert.Cents(z.GetProperty("perpetualGrandTotal")).Should().Be(ttc, $"{because} : première Z du poste, le perpétuel vaut le TTC du jour");
            sum += ttc;
            ht += DayAssert.Cents(z.GetProperty("totalSalesHt"));
            receipts += z.GetProperty("receiptCount").GetInt32();
        }

        string room = Ctx(day.Scenario.Seed, day.Scenario.Operations.Count, "*", "salle");
        sum.Should().Be(day.Ledger.Total.TtcCents, $"{room} : Σ TTC des postes");
        ht.Should().Be(day.Ledger.Total.HtCents, $"{room} : Σ HT des postes");
        receipts.Should().Be(day.Ledger.Total.ReceiptCount, $"{room} : Σ reçus des postes");
    }

    // ---------- deuxième journée ----------

    [Fact]
    public async Task ASecondDayChainsWithoutDoubleCounting()
    {
        var scenario = new DayScenario(21,
            [new("A", "Caisse A"), new("B", "Caisse B")],
            [
                new("table_order", "A", "waiter1", "1", 2, "EatIn", [new("Menu", 2, 2000, 10m)], null, 0, [[new("Cash", 4000, 1000)]], Ref: "d1a"),
                new("counter_order", "B", "waiter2", null, 0, "Takeaway", [new("Sandwich", 1, 1000, 10m, 5.5m)], null, 0, [[new("CreditCard", 1000)]], Ref: "d1b"),
                new("z_closure", "A"), new("z_closure", "B"),
                new("table_order", "A", "waiter1", "2", 3, "EatIn", [new("Plat", 1, 1850, 20m), new("Dessert", 2, 650, 10m)], new("Percentage", 10m), 200,
                    [[new("CreditCard", 2000)], [new("Cash", 1035, 500)]], Ref: "d2a"),
                new("counter_order", "B", "waiter2", null, 0, "Takeaway", [new("Café", 3, 200, 10m, 5.5m)], null, 0, [[new("Cash", 600)]], Ref: "d2b"),
                new("x_report", "A"), new("x_report", "B"),
                new("z_closure", "A"), new("z_closure", "B"),
            ]);
        using var factory = new PosApiApplicationFactory();
        var run = await DayRunner.RunAsync(factory, scenario);
        AssertReports(scenario, run); // X, Z et Z relue de chaque journée = grand livre de CETTE journée seulement

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (key, id) in run.TerminalIds)
        {
            var (z1, z2) = (run.ZClosures[key][0], run.ZClosures[key][1]);
            string because = Ctx(scenario.Seed, run.ZClosureOpIndexes[key][1], key, "Z du jour 2");
            z1.GetProperty("closureSequence").GetInt64().Should().Be(1, because);
            z2.GetProperty("closureSequence").GetInt64().Should().Be(2, because);

            long day1 = DayAssert.Cents(z1.GetProperty("totalSalesTtc"));
            long day2 = DayAssert.Cents(z2.GetProperty("totalSalesTtc"));
            day1.Should().BeGreaterThan(0);
            day2.Should().BeGreaterThan(0);
            DayAssert.Cents(z2.GetProperty("perpetualGrandTotal")).Should().Be(day1 + day2, $"{because} : grand total perpétuel = Σ TTC des deux jours");

            var stored = await db.DailyFiscalClosures.AsNoTracking().Where(c => c.TerminalId == id).OrderBy(c => c.ClosureSequence).ToListAsync();
            stored.Should().HaveCount(2, because);
            stored[1].PreviousSignatureHash.Should().Be(z1.GetProperty("signatureHash").GetString(), $"{because} : la Z 2 chaîne sur la signature de la Z 1");
            stored[0].SignatureHash.Should().Be(z1.GetProperty("signatureHash").GetString(), because);
            stored[1].SignatureHash.Should().Be(z2.GetProperty("signatureHash").GetString(), because);
        }

        await AssertChainsAreValidAsync(run, scenario);
    }

    // ---------- annulation après clôture ----------

    [Fact]
    public async Task VoidAfterClosureIsRefused()
    {
        day.Run.VoidAttempts.Should().NotBeEmpty("la journée tente une annulation tardive par poste");
        foreach (var attempt in day.Run.VoidAttempts)
        {
            string because = Ctx(day.Scenario.Seed, attempt.OpIndex, attempt.Key, $"annulation tardive de {attempt.Ref}");
            attempt.Status.Should().Be(409, because);
            attempt.Code.Should().Be("void_after_closure", because);
        }

        // rien n'a été écrit : aucun reçu d'annulation supplémentaire et les chaînes restent saines
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        int expectedVoids = day.Scenario.Operations.Count(o => o.Type is "table_order" or "counter_order" && o.VoidPaymentIndex != null);
        (await db.FiscalReceipts.CountAsync(r => r.VoidedReceiptId != null)).Should().Be(expectedVoids, "seules les annulations d'avant la Z existent");
        await AssertChainsAreValidAsync(day.Run, day.Scenario);
    }

    // ---------- intégrité des chaînes et journal technique ----------

    [Fact]
    public async Task ChainsAreIntactAndTheJournalHasEveryEvent()
    {
        await AssertChainsAreValidAsync(day.Run, day.Scenario);

        var orders = day.Scenario.Operations.Where(o => o.Type is "table_order" or "counter_order").ToList();
        var expected = new Dictionary<string, int>
        {
            ["Z_CLOSURE"] = day.Scenario.Terminals.Count,
            ["RECEIPT_VOIDED"] = orders.Count(o => o.VoidPaymentIndex != null),
            ["DUPLICATE_PRINTED"] = orders.Where(o => o.Reprint).Sum(o => o.Payments!.Count),
            ["EVENT_HELD_ORDER_VOIDED"] = day.Scenario.Operations.Count(o => o.Type == "hold_then_void"),
        };
        expected.Values.Should().OnlyContain(n => n > 0, "la journée exerce chaque événement");

        var client = day.Run.Clients.Values.First();
        foreach (var (eventType, count) in expected)
        {
            var page = await DayRunner.SendAsync(client, HttpMethod.Get, $"/api/fiscal/journal?eventType={eventType}&pageSize=1", null, day.Run.Manager.Token);
            page.GetProperty("totalCount").GetInt32().Should().Be(count, $"{Ctx(day.Scenario.Seed, day.Scenario.Operations.Count, "*", "journal")} : événements {eventType}");
        }
    }

    // ---------- FEC ----------

    [Fact]
    public async Task TheFecIsBalancedAndItsVatMatchesTheLedger()
    {
        var fec = await ReadFecAsync();
        string because = Ctx(day.Scenario.Seed, day.Scenario.Operations.Count, "*", "FEC");

        // Reçus de vente (hors annulations) : le FEC crédite HT + TVA et débite les règlements, pourboire compris. L'écart est exactement le pourboire.
        // Le grand livre « sans annulation » donne les ventes d'origine.
        var originals = DayLedgerOracle.Compute(
            day.Scenario.Operations.Select(o => o with { VoidPaymentIndex = null }), day.Run.TerminalIds);
        var sales = fec.Where(l => !l.IsVoid).ToList();
        long tips = day.Scenario.Operations.Where(o => o.Type is "table_order" or "counter_order" && o.Payments is { Count: > 0 }).Sum(o => o.TipCents);
        (sales.Sum(l => l.Debit) - sales.Sum(l => l.Credit)).Should().Be(tips, $"{because} : débit - crédit des ventes = pourboires");
        sales.Where(l => l.Account.StartsWith("706", StringComparison.Ordinal)).Sum(l => l.Credit - l.Debit).Should().Be(originals.Total.HtCents, $"{because} : ventes 706 = HT des ventes d'origine");
        VatByAccount(sales).Should().BeEquivalentTo(ExpectedVatByAccount(originals), $"{because} : TVA collectée des ventes d'origine");

        // TVA nette (annulations comprises) = TVA du grand livre : les lignes de TVA des annulations sont bien écrites.
        VatByAccount(fec).Should().BeEquivalentTo(ExpectedVatByAccount(day.Ledger), $"{because} : TVA collectée nette par compte");
        fec.Where(l => l.IsVoid).Should().NotBeEmpty("la journée annule au moins un reçu");
    }

    /// <summary>
    /// DÉFAUT DE PRODUCTION CONNU (FecExportService.ParseVatBreakdown) : pour un reçu d'annulation (montants négatifs), le dernier taux reçoit
    /// <c>Math.Max(0, totalHt - allocatedHt)</c> = 0 : sa ligne de ventes 706 n'est pas écrite, le FEC perd cet HT et son débit-crédit ne
    /// se solde plus. Ce test dit ce qui doit être vrai une fois corrigé ; il est ignoré tant que le code de production n'est pas touché.
    /// </summary>
    [Fact(Skip = "Défaut connu du FEC : l'HT du dernier taux d'un reçu d'annulation n'est pas écrit (Math.Max(0, HT négatif)). À corriger côté production.")]
    public async Task TheFecKeepsTheHtOfVoidReceipts()
    {
        var fec = await ReadFecAsync();
        fec.Where(l => l.Account.StartsWith("706", StringComparison.Ordinal)).Sum(l => l.Credit - l.Debit).Should().Be(
            day.Ledger.Total.HtCents,
            $"{Ctx(day.Scenario.Seed, day.Scenario.Operations.Count, "*", "FEC")} : ventes 706 nettes = HT du grand livre");
    }

    private sealed record FecLine(string Account, string Piece, long Debit, long Credit)
    {
        public bool IsVoid => Piece.Contains("-VOID-", StringComparison.Ordinal);
    }

    private async Task<List<FecLine>> ReadFecAsync()
    {
        var client = day.Run.Clients.Values.First();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/fiscal/fec?from=2000-01-01T00:00:00Z&to=2100-01-01T00:00:00Z");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", day.Run.Manager.Token);
        using var response = await client.SendAsync(request);
        response.IsSuccessStatusCode.Should().BeTrue();
        var lines = (await response.Content.ReadAsStringAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Skip(1).Select(l => l.Split('\t')).Select(c => new FecLine(c[4], c[8], ToCents(c[11]), ToCents(c[12]))).ToList();
        lines.Should().NotBeEmpty();
        return lines;
    }

    private static Dictionary<string, long> VatByAccount(IEnumerable<FecLine> lines) => lines
        .Where(l => l.Account.StartsWith("4457", StringComparison.Ordinal))
        .GroupBy(l => l.Account).ToDictionary(g => g.Key, g => g.Sum(l => l.Credit - l.Debit)).Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static Dictionary<string, long> ExpectedVatByAccount(DayLedger ledger) => ledger.Total.VatByRate
        .GroupBy(kv => VatAccount(kv.Key), kv => kv.Value).ToDictionary(g => g.Key, g => g.Sum()).Where(kv => kv.Value != 0).ToDictionary(kv => kv.Key, kv => kv.Value);

    // ---------- même graine, mêmes totaux ----------

    [Fact]
    public async Task TheSameSeedGivesTheSameTotals()
    {
        using var factory = new PosApiApplicationFactory();
        var again = await DayRunner.RunAsync(factory, DayScenarioGenerator.Build(DayScenarioGenerator.DefaultSeed));
        foreach (var (key, _) in day.Run.TerminalIds)
        {
            DayAssert.SummariesEqual(again.ZClosures[key].Single(), day.Run.ZClosures[key].Single(),
                Ctx(day.Scenario.Seed, day.Run.ZClosureOpIndexes[key].Single(), key, "Z rejouée avec la même graine"));
        }
    }

    // ---------- autres graines ----------

    [Theory]
    [InlineData(1)]
    [InlineData(42)]
    public async Task OtherSeeds_AlsoMatchTheLedger(int seed)
    {
        var scenario = DayScenarioGenerator.Build(seed);
        using var factory = new PosApiApplicationFactory();
        var run = await DayRunner.RunAsync(factory, scenario);
        AssertReports(scenario, run);
        await AssertChainsAreValidAsync(run, scenario);
        run.VoidAttempts.Should().OnlyContain(a => a.Status == 409 && a.Code == "void_after_closure");
    }

    // ---------- JSON partagé avec l'iPad ----------

    [Fact]
    public void TheScenarioFileMatchesTheGenerator()
    {
        var file = DayScenarioGenerator.ToFile(DayScenarioGenerator.Build(DayScenarioGenerator.DefaultSeed));
        string actual = JsonSerializer.Serialize(file, DayScenarioJsonContext.Default.DayScenarioFile).ReplaceLineEndings("\n") + "\n";
        string path = FixturePath();
        if (Environment.GetEnvironmentVariable("WRITE_FISCAL_FIXTURE") == "1")
        {
            File.WriteAllText(path, actual);
        }

        File.ReadAllText(path).ReplaceLineEndings("\n").Should().Be(
            actual,
            "day_scenario.json doit refléter le générateur : WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~DaySimulation");
        file.Total.TtcCents.Should().Be(file.Expected.Values.Sum(e => e.TtcCents), "le total de la salle est la somme des postes");
    }

    // ---------- aides ----------

    /// <summary>Chaque X, chaque Z et chaque Z relue du rejeu égale le grand livre du poste pour la période ouverte à cet instant.</summary>
    private static void AssertReports(DayScenario scenario, DayRunResult run)
    {
        foreach (var (key, _) in run.TerminalIds)
        {
            for (int n = 0; n < run.XReports[key].Count; n++)
            {
                int index = run.XReportOpIndexes[key][n];
                DayAssert.SummaryEquals(run.XReports[key][n], LedgerAt(scenario, run.TerminalIds, key, index), Ctx(scenario.Seed, index, key, "X"));
            }

            for (int n = 0; n < run.ZClosures[key].Count; n++)
            {
                int index = run.ZClosureOpIndexes[key][n];
                var expected = LedgerAt(scenario, run.TerminalIds, key, index);
                DayAssert.SummaryEquals(run.ZClosures[key][n], expected, Ctx(scenario.Seed, index, key, "Z"));
                DayAssert.SummaryEquals(run.LatestClosures[key][n], expected, Ctx(scenario.Seed, index, key, "Z relue"));
            }
        }
    }

    /// <summary>Grand livre d'un poste pour les opérations jouées avant <paramref name="opIndex"/> depuis sa dernière Z.</summary>
    private static TerminalLedger LedgerAt(DayScenario scenario, IReadOnlyDictionary<string, string> ids, string key, int opIndex)
    {
        int start = scenario.Operations.Take(opIndex).ToList().FindLastIndex(o => o.Type == "z_closure" && o.Terminal == key) + 1;
        var ops = scenario.Operations.Skip(start).Take(opIndex - start).Where(o => o.Terminal == key);
        return DayLedgerOracle.Compute(ops, ids).ByTerminal[ids[key]];
    }

    /// <summary>Pourboires encaissés et non annulés d'un poste : la part des règlements qui n'est pas du chiffre d'affaires.</summary>
    private static long NetTipsCents(DayScenario scenario, string key) => scenario.Operations
        .Where(o => o.Terminal == key && o.Type is "table_order" or "counter_order" && o.Payments is { Count: > 0 })
        .Where(o => o.VoidPaymentIndex != o.Payments!.Count - 1)
        .Sum(o => o.TipCents);

    private static async Task AssertChainsAreValidAsync(DayRunResult run, DayScenario scenario)
    {
        var verify = await DayRunner.SendAsync(run.Clients.Values.First(), HttpMethod.Post, "/api/fiscal/verify", null, run.Manager.Token);
        string broken = string.Join(", ", verify.GetProperty("chains").EnumerateArray().Where(c => !c.GetProperty("isValid").GetBoolean()).Select(c => c.GetRawText()));
        verify.GetProperty("isValid").GetBoolean().Should().BeTrue($"{Ctx(scenario.Seed, scenario.Operations.Count, "*", "vérification des chaînes")} : {broken}");
    }

    private static async Task<JsonElement> GetXAsync(DayRunResult run, string key, string terminalId) =>
        await DayRunner.SendAsync(run.Clients[key], HttpMethod.Get, $"/api/fiscal/x-report?terminalId={terminalId}", null, run.Manager.Token);

    private static async Task<(int Receipts, int Closures, int Journal)> CountsAsync(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.FiscalReceipts.CountAsync(), await db.DailyFiscalClosures.CountAsync(), await db.JournalEntries.CountAsync());
    }

    private static int FirstZ(DayScenario scenario) => scenario.Operations.Select((o, i) => (o, i)).First(x => x.o.Type == "z_closure").i;

    private static string Ctx(int seed, int opIndex, string key, string what) => $"{DayRunner.Context(seed, opIndex, key)}, {what}";

    private static string Json(DayScenario scenario) => JsonSerializer.Serialize(scenario, DayScenarioJsonContext.Default.DayScenario);

    private static long ToCents(string euros) => (long)(decimal.Parse(euros, CultureInfo.InvariantCulture) * 100m);

    private static string VatAccount(decimal rate) => rate switch
    {
        >= 19.5m and <= 20.5m => "445720",
        >= 9.5m and <= 10.5m => "445710",
        >= 5.0m and <= 6.0m => "445755",
        _ => "445700",
    };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "day_scenario.json");
    }
}
