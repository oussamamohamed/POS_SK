using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Fixture partagée avec l'iPad (mode autonome) : articles vendus et pourboires des rapports (<see cref="ReportPrintDataService"/>).
/// Trois sections : <c>collation</c> (tri français de <c>StringComparer.Create(fr, ignoreCase: true)</c>, ICU du système),
/// <c>scenarios</c> (données de base + résultat attendu) et <c>day</c> (relevé de chaque Z de la journée simulée, rejouée par l'iPad).
/// Le .NET est la référence : ne jamais éditer le JSON à la main. Régénérer avec
/// <c>WRITE_REPORT_PRINT_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests --filter ReportPrintDataReferenceFixtureTests</c>
/// après toute modification de <c>ReportPrintDataService</c> ou de la journée simulée.
/// </summary>
public class ReportPrintDataReferenceFixtureTests(DayFixture day) : IClassFixture<DayFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly StringComparer French = StringComparer.Create(CultureInfo.GetCultureInfo("fr"), ignoreCase: true);
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(16);

    /// <summary>Noms pièges, dans l'ordre d'entrée : les égalités (casse seule) gardent cet ordre (tri stable).</summary>
    private static readonly string[] CollationNames =
    [
        "Éclair", "eclair", "Eclair", "Ecuelle", "écureuil", "Œuf mayonnaise", "Oeuf cocotte", "oeuf", "Straße", "Strasse", "strass",
        "Café", "cafe", "Café crème", "Cafe-crème", "café au lait", "Côte de bœuf", "Cote", "côté", "coté", "cote", "L'Entrecôte",
        "Lentilles", "l'eau", "Eau", "10 Wings", "2 Burgers", "Article 10", "Article 2", "Ça va", "Cabillaud", "Zoé", "zoe", " Espace",
        "Espace", "Bœuf", "Boeuf bourguignon", "Pâtes", "Pates", "Pâté", "pâte", "Île flottante", "Ile", "CAFÉ", "Æble", "Cafe crème",
        "Cafécrème", "Thé", "the", "Thé vert", "Ébène", "ＣＡＦＥ", "Saſ", "Sas",
    ];

    /// <summary>Paires dont le signe de comparaison distingue une égalité ICU « secondaire » des règles propres au .NET.</summary>
    private static readonly (string A, string B)[] CollationPairs =
    [
        ("Straße", "Strasse"), ("ß", "ss"), ("ẞ", "ß"), ("eclair", "Eclair"), ("Éclair", "eclair"), ("æ", "ae"), ("œ", "oe"), ("Œ", "oe"),
        ("ﬁ", "fi"), ("Ａ", "A"), ("ａ", "A"), ("ǆ", "dž"), ("Ǆ", "ǆ"), ("µ", "μ"), ("ſ", "s"), ("x­y", "xy"), ("é", "é"),
        ("ı", "i"), ("İ", "i"), ("№", "No"), ("½", "1/2"), ("²", "2"), ("ª", "a"), ("Ça", "Ca"), ("l'eau", "leau"), ("a b", "ab"),
        ("Article 10", "Article 2"), ("Cote", "côte"), ("coté", "côte"),
    ];

    [Fact]
    public async Task Fixture_matches_dotnet()
    {
        var json = await BuildFixtureJsonAsync();
        if (Environment.GetEnvironmentVariable("WRITE_REPORT_PRINT_FIXTURE") == "1")
        {
            await File.WriteAllTextAsync(FixturePath(), json);
        }

        File.Exists(FixturePath()).Should().BeTrue("fixture absente : la générer avec WRITE_REPORT_PRINT_FIXTURE=1");
        (await File.ReadAllTextAsync(FixturePath())).ReplaceLineEndings("\n").Should().Be(json,
            "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_REPORT_PRINT_FIXTURE=1");
    }

    private async Task<string> BuildFixtureJsonAsync()
    {
        var scenarios = new List<object>();
        foreach (var scenario in Scenarios())
        {
            scenarios.Add(await RunScenarioAsync(scenario));
        }

        var fixture = new
        {
            collation = new { input = CollationNames, sorted = CollationNames.OrderBy(n => n, French).ToArray() },
            collationPairs = CollationPairs.Select(p => new { a = p.A, b = p.B, sign = Math.Sign(French.Compare(p.A, p.B)) }).ToArray(),
            scenarios,
            day = await DaySectionAsync(),
        };
        return JsonSerializer.Serialize(fixture, JsonOptions).ReplaceLineEndings("\n") + "\n";
    }

    // ---------- journée simulée ----------

    /// <summary>Pour chaque poste, le relevé de chaque Z sur la période signée de la clôture (bornes stockées).</summary>
    private async Task<object> DaySectionAsync()
    {
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = new ReportPrintDataService(db);
        var terminals = new SortedDictionary<string, List<ReportPrintData>>(StringComparer.Ordinal);
        foreach (var (key, id) in day.Run.TerminalIds)
        {
            var closures = await db.DailyFiscalClosures.AsNoTracking().Where(c => c.TerminalId == id).OrderBy(c => c.ClosureSequence).ToListAsync();
            closures.Should().NotBeEmpty($"le poste {key} a une Z");
            var reports = new List<ReportPrintData>();
            foreach (var closure in closures)
            {
                reports.Add(await service.BuildAsync(id, closure.PeriodStartUtc, closure.PeriodEndUtc));
            }

            terminals[key] = reports;
        }

        var all = terminals.Values.SelectMany(r => r).ToList();
        all.Should().Contain(r => r.Tips.Count > 0, "la journée a des pourboires");
        all.Should().Contain(r => r.HasGlobalDiscount, "la journée a des remises globales");
        return new { seed = day.Scenario.Seed, terminals };
    }

    // ---------- scénarios de base ----------

    private sealed record CategoryRow(string Id, string Name);
    private sealed record ProductRow(Guid Id, string Name, string CategoryId);
    private sealed record UserRow(Guid Id, string Name);
    private sealed record ItemRow(Guid ProductId, string ProductName, int Quantity, long UnitPriceCents, long ModifiersExtraCents = 0, decimal DiscountPercent = 0m, bool IsComp = false);
    private sealed record OrderRow(Guid Id, Guid OperatorId, int Status, long TipCents, int? GlobalDiscountType, decimal GlobalDiscountValue, IReadOnlyList<ItemRow> Items);
    private sealed record ReceiptRow(Guid Id, string TerminalId, Guid OrderId, long TtcCents, long CreatedAtUnixMs, Guid? VoidedReceiptId);

    private sealed record Scenario(
        string Name, string TerminalId, long? PeriodStartUnixMs, long PeriodEndUnixMs,
        IReadOnlyList<CategoryRow> Categories, IReadOnlyList<ProductRow> Products, IReadOnlyList<UserRow> Users,
        IReadOnlyList<OrderRow> Orders, IReadOnlyList<ReceiptRow> Receipts);

    private static Guid Id(int n) => Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"{n:D8}-0000-0000-0000-000000000000"));

    private static long Ms(DateTimeOffset at) => at.ToUnixTimeMilliseconds();

    private static OrderRow Paid(int id, Guid op, long tip, params ItemRow[] items) => new(Id(id), op, 3, tip, null, 0m, items);

    private static ReceiptRow Receipt(int id, Guid order, DateTimeOffset at, long ttc = 1000, string terminal = "T01", Guid? voided = null) =>
        new(Id(id), terminal, order, ttc, Ms(at), voided);

    private static IEnumerable<Scenario> Scenarios()
    {
        // 1. Familles (tri français, inconnue en dernier), articles agrégés par nom exact, offert à 0, remises de ligne et suppléments.
        Guid entrecote = Id(101), entrecoteMin = Id(102), tiramisu = Id(103), brownie = Id(104), ile = Id(105), cafe = Id(106), cafeMin = Id(107),
            salade = Id(108), soupe = Id(109), oeufMayo = Id(110), oeufCocotte = Id(111), venteLibre = Id(112), supprime = Id(199);
        CategoryRow[] familles =
        [
            new("C1", "Plats"), new("C2", "desserts"), new("C3", "Entrées"), new("C4", "Boissons"), new("C5", "Œufs"), new("C6", "entremets"), new("C7", "Écaillés"),
        ];
        ProductRow[] produits =
        [
            new(entrecote, "Entrecôte", "C1"), new(entrecoteMin, "entrecote", "C1"), new(tiramisu, "Tiramisu", "C2"), new(brownie, "Brownie", "C2"),
            new(ile, "Île flottante", "C2"), new(cafe, "Café", "C4"), new(cafeMin, "café", "C4"), new(salade, "Salade d'été", "C3"), new(soupe, "Soupe", "C6"),
            new(oeufMayo, "Œuf mayo", "C5"), new(oeufCocotte, "Oeuf cocotte", "C5"), new(venteLibre, "Vente libre", "CAT-SUPPRIMEE"),
        ];
        var alex = new UserRow(Id(201), "Alex");
        OrderRow o1 = Paid(1, alex.Id, 0,
            new(entrecote, "Entrecôte", 2, 2200), new(brownie, "Brownie", 1, 650, IsComp: true), new(tiramisu, "Tiramisu", 2, 700, ModifiersExtraCents: 50),
            new(cafe, "Café", 1, 250)) with { GlobalDiscountType = 0, GlobalDiscountValue = 10m };
        OrderRow o2 = Paid(2, alex.Id, 0,
            new(entrecoteMin, "entrecote", 1, 2200, DiscountPercent: 15m), new(tiramisu, "Tiramisu", 1, 700), new(cafeMin, "café", 3, 250),
            new(salade, "Salade d'été", 1, 1250), new(oeufMayo, "Œuf mayo", 1, 600), new(oeufCocotte, "Oeuf cocotte", 2, 750),
            new(venteLibre, "Vente libre", 1, 500), new(supprime, "Article supprimé", 1, 300), new(ile, "Île flottante", 1, 800),
            new(soupe, "Soupe", 1, 900, DiscountPercent: 33.33m), new(cafe, "Café", 2, 250));
        yield return new Scenario("familles_tri_offert_remise", "T01", Ms(Start), Ms(End), familles, produits, [alex], [o1, o2],
            [Receipt(1001, o1.Id, Start.AddHours(1)), Receipt(1002, o2.Id, Start.AddHours(2))]);

        // 2. Pourboires par opérateur (deux homonymes, opérateurs inconnus en dernier, pourboire nul omis).
        Guid plat = Id(301);
        UserRow zoeAccent = new(Id(211), "Zoé"), alexMin = new(Id(212), "alex"), emile = new(Id(213), "Émile"), alexMaj = new(Id(214), "Alex"), zoe = new(Id(215), "zoe");
        var item = new ItemRow(plat, "Plat du jour", 1, 1500);
        OrderRow[] tips =
        [
            Paid(11, zoeAccent.Id, 300, item), Paid(12, alexMin.Id, 150, item), Paid(13, zoeAccent.Id, 200, item), Paid(14, Guid.Empty, 100, item),
            Paid(15, Id(299), 250, item), Paid(16, emile.Id, 120, item), Paid(17, alexMaj.Id, 80, item), Paid(18, zoe.Id, 60, item),
            Paid(19, emile.Id, 0, item), Paid(20, alexMin.Id, 0, item),
        ];
        yield return new Scenario("pourboires", "T01", Ms(Start), Ms(End), [], [new(plat, "Plat du jour", "C-ABSENTE")],
            [zoeAccent, alexMin, emile, alexMaj, zoe], tips, tips.Select((o, i) => Receipt(1100 + i, o.Id, Start.AddMinutes(10 * (i + 1)))).ToList());

        // 3. Périmètre : bornes incluses, annulations, avoirs, commandes annulées, autre poste, reçu à zéro, remise globale « offert ».
        Guid p = Id(401);
        ItemRow Line(string name) => new(p, name, 1, 1000);
        OrderRow debut = Paid(21, Guid.Empty, 0, Line("Inclus début")), fin = Paid(22, Guid.Empty, 0, Line("Inclus fin")),
            avant = Paid(23, Guid.Empty, 0, Line("Avant")), apres = Paid(24, Guid.Empty, 0, Line("Après")),
            annule = Paid(25, Guid.Empty, 0, Line("Annulé")), partiel = Paid(26, Guid.Empty, 0, Line("Partiel annulé")),
            cancelled = Paid(27, Guid.Empty, 0, Line("Commande annulée")) with { Status = 4 }, autre = Paid(28, Guid.Empty, 0, Line("Autre poste")),
            zero = Paid(29, Guid.Empty, 0, Line("Ticket à zéro")),
            offertGlobal = Paid(30, Guid.Empty, 0, new ItemRow(p, "Offert global", 1, 1200)) with { GlobalDiscountType = 2 },
            deuxRecus = Paid(31, Guid.Empty, 0, new ItemRow(p, "Deux reçus", 2, 1000));
        yield return new Scenario("perimetre", "T01", Ms(Start), Ms(End), [new("C9", "Divers")], [new(p, "Divers", "C9")], [],
            [debut, fin, avant, apres, annule, partiel, cancelled, autre, zero, offertGlobal, deuxRecus],
            [
                Receipt(1201, avant.Id, Start.AddMilliseconds(-1)), Receipt(1202, debut.Id, Start), Receipt(1203, annule.Id, Start.AddHours(1)),
                Receipt(1204, annule.Id, Start.AddHours(1).AddMinutes(1), ttc: -1000, voided: Id(1203)), Receipt(1205, partiel.Id, Start.AddHours(2)),
                Receipt(1206, partiel.Id, Start.AddHours(2).AddMinutes(1), ttc: 400), Receipt(1207, partiel.Id, Start.AddHours(2).AddMinutes(2), ttc: -1000, voided: Id(1205)),
                Receipt(1208, cancelled.Id, Start.AddHours(3)), Receipt(1209, autre.Id, Start.AddHours(4), terminal: "T02"),
                Receipt(1210, zero.Id, Start.AddHours(5), ttc: 0), Receipt(1211, offertGlobal.Id, Start.AddHours(6), ttc: 1),
                Receipt(1212, deuxRecus.Id, Start.AddHours(7), ttc: 1200), Receipt(1213, deuxRecus.Id, Start.AddHours(7).AddMinutes(1), ttc: 800),
                Receipt(1214, fin.Id, End), Receipt(1215, apres.Id, End.AddMilliseconds(1)),
            ]);

        // 4. Sans début de période (première Z, DateTimeOffset.MinValue) : un reçu ancien entre, la remise fixe compte.
        OrderRow ancien = Paid(41, Guid.Empty, 0, Line("Ancien")) with { GlobalDiscountType = 1, GlobalDiscountValue = 2m };
        OrderRow tard = Paid(42, Guid.Empty, 0, Line("Trop tard"));
        yield return new Scenario("sans_debut", "T01", null, Ms(End), [], [], [], [ancien, tard],
            [Receipt(1301, ancien.Id, Start.AddYears(-3)), Receipt(1302, tard.Id, End.AddSeconds(1))]);

        // 5. Rien à rapporter.
        yield return new Scenario("vide", "T01", Ms(Start), Ms(End), [], [], [], [], []);
    }

    private static async Task<object> RunScenarioAsync(Scenario s)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("ReportFixture_" + s.Name).Options);
        foreach (var c in s.Categories)
        {
            db.Categories.Add(new Category { Id = c.Id, Name = c.Name });
        }

        foreach (var p in s.Products)
        {
            db.Products.Add(new Product { Id = p.Id, Name = p.Name, CategoryId = p.CategoryId, Price = Money.FromCents(100) });
        }

        foreach (var u in s.Users)
        {
            db.Users.Add(new User { Id = u.Id, Name = u.Name, PinHash = "x", PinSalt = "x" });
        }

        foreach (var o in s.Orders)
        {
            var order = new Order
            {
                Id = o.Id, OperatorId = o.OperatorId, Status = (OrderStatus)o.Status, TipAmount = Money.FromCents(o.TipCents),
                GlobalDiscountType = (DiscountType?)o.GlobalDiscountType, GlobalDiscountValue = o.GlobalDiscountValue,
            };
            foreach (var i in o.Items)
            {
                order.Items.Add(new OrderItem
                {
                    ProductId = i.ProductId, ProductName = i.ProductName, Quantity = i.Quantity, UnitPrice = Money.FromCents(i.UnitPriceCents),
                    ModifiersPriceExtra = Money.FromCents(i.ModifiersExtraCents), DiscountPercent = i.DiscountPercent, IsComp = i.IsComp,
                });
            }

            db.Orders.Add(order);
        }

        int sequence = 0;
        foreach (var r in s.Receipts)
        {
            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = r.Id, TerminalId = r.TerminalId, ReceiptNumber = string.Create(CultureInfo.InvariantCulture, $"{r.TerminalId}-{++sequence:D6}"),
                SequenceNumber = sequence, OrderId = r.OrderId, TotalTtcAmount = Money.FromCents(r.TtcCents),
                CreatedAtUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.CreatedAtUnixMs), VoidedReceiptId = r.VoidedReceiptId,
            });
        }

        await db.SaveChangesAsync();
        var start = s.PeriodStartUnixMs is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;
        var expected = await new ReportPrintDataService(db).BuildAsync(s.TerminalId, start, DateTimeOffset.FromUnixTimeMilliseconds(s.PeriodEndUnixMs));
        return new
        {
            name = s.Name, terminalId = s.TerminalId, periodStartUnixMs = s.PeriodStartUnixMs, periodEndUnixMs = s.PeriodEndUnixMs,
            categories = s.Categories, products = s.Products, users = s.Users, orders = s.Orders, receipts = s.Receipts, expected,
        };
    }

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "report_print_data_reference.json");
    }
}
