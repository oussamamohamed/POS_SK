using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad (mode autonome) : le Swift reconstruit les tickets (document, octets ESC/POS texte, commandes raster)
/// et doit obtenir exactement les mêmes résultats. Le .NET est la référence : ne jamais éditer le JSON à la main.
/// Régénérer avec <c>TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests</c>
/// après toute modification de <c>TicketDocumentBuilder</c>, <c>EscPosTextRenderer</c>, <c>EscPosCommands</c> ou des textes <c>receipt./report./kitchen_ticket./admin.payment_method_</c>.
/// </summary>
public class TicketDocumentReferenceFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly string[] TextPrefixes = ["receipt.", "report.", "kitchen_ticket.", "admin.payment_method_"];
    private static readonly string[] Languages = ["en", "fr", "ar"];

    private static readonly Guid Burger = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Salad = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Tiramisu = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Coffee = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Absent = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private static readonly ReportPrintData NoData = new([], [], 0, false);

    private const string Signature = "a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90";

    [Fact]
    public void Fixture_is_generated_in_paris_time()
    {
        TimeZoneInfo.Local.Id.Should().BeOneOf("Europe/Paris", "Romance Standard Time");
    }

    [Fact]
    public void Fixture_matches_dotnet()
    {
        var previousTz = Environment.GetEnvironmentVariable("TZ");
        Environment.SetEnvironmentVariable("TZ", "Europe/Paris");
        TimeZoneInfo.ClearCachedData();
        try
        {
            // Échoue (au lieu de sauter) si le fuseau ne peut pas être forcé : l'heure locale des tickets entre dans la référence.
            TimeZoneInfo.Local.Id.Should().BeOneOf("Europe/Paris", "Romance Standard Time", "le fuseau Europe/Paris est requis pour générer ou comparer la fixture");

            var json = BuildFixtureJson();
            if (Environment.GetEnvironmentVariable("WRITE_TICKET_FIXTURE") == "1")
            {
                File.WriteAllText(FixturePath(), json);
            }

            File.Exists(FixturePath()).Should().BeTrue("fixture absente : la générer avec WRITE_TICKET_FIXTURE=1");
            File.ReadAllText(FixturePath()).ReplaceLineEndings("\n").Should().Be(json,
                "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_TICKET_FIXTURE=1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previousTz);
            TimeZoneInfo.ClearCachedData();
        }
    }

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "ticket_document_reference.json");
    }

    private static string BuildFixtureJson()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var version = (string)typeof(TicketDocumentBuilder)
            .GetMethod("GetSoftwareVersion", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;

        var fixture = new
        {
            timeZone = "Europe/Paris",
            softwareVersion = version,
            texts = TextsSection(),
            pc858 = Pc858Section(),
            encode = EncodeSection(),
            wrap = WrapSection(),
            commands = CommandsSection(),
            textRender = TextRenderSection(),
            documents = DocumentsSection()
        };
        return JsonSerializer.Serialize(fixture, JsonOptions).ReplaceLineEndings("\n");
    }

    // MARK: textes, encodage, découpe

    private static Dictionary<string, SortedDictionary<string, string>> TextsSection()
    {
        var keys = Texts.Keys(CultureInfo.GetCultureInfo("en"))
            .Where(k => TextPrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToList();
        keys.Should().NotBeEmpty();
        return Languages.ToDictionary(
            l => l,
            l => new SortedDictionary<string, string>(
                keys.ToDictionary(k => k, k => Texts.Get(CultureInfo.GetCultureInfo(l), k)), StringComparer.Ordinal));
    }

    private static int[] Pc858Section()
    {
        var enc = Encoding.GetEncoding(858);
        return Enumerable.Range(0, 256).Select(i => (int)enc.GetString([(byte)i])[0]).ToArray();
    }

    private static object[] EncodeSection()
    {
        var enc = Encoding.GetEncoding(858, new EncoderReplacementFallback("?"), DecoderFallback.ReplacementFallback);
        string[] samples = ["àâçéèêëîïôùû ÀÉÈ — 0123456789", "€", "😀", "مرحبا", "ＡＢ", "", "a\tb"];
        return samples.Select(t => (object)new { text = t, hex = Hex(enc.GetBytes(t)) }).ToArray();
    }

    private static object[] WrapSection()
    {
        (string Text, int Width)[] samples =
        [
            ("", 10), ("hello", 10), ("aaaaaaaaaaaaaaaaaaaaaaaa", 10), ("un deux trois quatre cinq six", 10),
            ("  espaces   multiples  ", 10), ("😀😀😀😀😀😀", 4)
        ];
        return samples.Select(s => (object)new { text = s.Text, width = s.Width, lines = EscPosTextRenderer.Wrap(s.Text, s.Width) }).ToArray();
    }

    // MARK: commandes raster et rendu texte

    private static MonoImage Image(int width, int height, Func<int, byte> byteAt) =>
        new(width, height, Enumerable.Range(0, width / 8 * height).Select(byteAt).ToArray());

    private static object[] CommandsSection()
    {
        var tall = Image(8, 300, i => (byte)(i % 251));
        (string Name, bool Drawer, MonoImage[] Segments)[] cases =
        [
            ("single_8x2_no_drawer", false, [new MonoImage(8, 2, [0xFF, 0x00])]),
            ("two_segments_drawer", true, [Image(16, 3, i => (byte)(i * 37 + 1)), new MonoImage(8, 1, [0xA5])]),
            ("two_bands_8x300", false, [tall]),
            ("empty_list", false, [])
        ];
        return cases.Select(c => (object)new
        {
            name = c.Name,
            drawer = c.Drawer,
            segments = c.Segments.Select(s => new { width = s.Width, height = s.Height, bitsHex = Hex(s.Bits) }).ToArray(),
            expectedHex = Hex(EscPosCommands.Build(c.Segments, c.Drawer))
        }).ToArray();
    }

    private static object[] TextRenderSection()
    {
        const string Long = "un tres long libelle de produit qui depasse la largeur du ticket";
        (string Name, int Width, bool Drawer, TicketLine[] Lines)[] cases =
        [
            ("align_center_right", 80, false,
                [new TicketText("Centre", TicketAlign.Center), new TicketText("Droite", TicketAlign.End), new TicketText("Gauche")]),
            ("bold_large", 80, false,
                [new TicketText("Gras", Bold: true), new TicketText("Large", Large: true), new TicketText(Long, TicketAlign.Center, Bold: true, Large: true)]),
            ("columns_short", 80, false,
                [new TicketColumns("Total", "12.50"), new TicketColumns("2x Café", "5.00")]),
            ("columns_overlong_58", 58, false,
                [new TicketColumns(Long, "123456.78 EUR de valeur"), new TicketColumns("Court", "0.00")]),
            ("separators_cut_middle_and_end", 80, false,
                [new TicketText("Avant"), new TicketSeparator(), new TicketText("Milieu"), new TicketSeparator(Cut: true), new TicketText("Après"), new TicketSeparator(Cut: true)]),
            ("accents_euro_drawer_58", 58, true,
                [new TicketText("àâçéèêëîïôùû ÀÉÈ — 12,50 €", TicketAlign.Center), new TicketColumns("Café crème", "2.50 €")]),
            ("emoji_80", 80, false,
                [new TicketText("Merci 😀 à bientôt"), new TicketColumns("Dessert 😀", "4.00")]),
            ("empty_document", 80, true, [])
        ];
        return cases.Select(c =>
        {
            var doc = new TicketDocument("fr", false, c.Lines);
            return (object)new
            {
                name = c.Name,
                paperWidthMm = c.Width,
                drawer = c.Drawer,
                document = ToElement(doc),
                expectedHex = Hex(EscPosTextRenderer.Render(doc, c.Width, c.Drawer))
            };
        }).ToArray();
    }

    // MARK: documents

    private sealed record Scenario(string Name, string Kind, string Language, object Input, TicketDocument Expected);

    private static object[] DocumentsSection()
    {
        var all = new List<Scenario>();
        all.AddRange(ReceiptScenarios());
        all.AddRange(FiscalAndPickupScenarios());
        all.AddRange(KitchenScenarios());
        all.AddRange(ReportScenarios());
        all.AddRange(MiscScenarios());
        all.Count.Should().BeGreaterThanOrEqualTo(30);
        all.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        return all.Select(s => (object)new { name = s.Name, kind = s.Kind, language = s.Language, input = s.Input, expected = ToElement(s.Expected) }).ToArray();
    }

    private static List<Scenario> ReceiptScenarios()
    {
        var fullSettings = new RestaurantSettingsDto("fr", "fr", "Chez Léa", "5 Rue des Lilas\n69002 Lyon", "12345678900011", "FR98123456789", "NF525-2024-0042", 1, 1);
        var blankSettings = new RestaurantSettingsDto("fr", "fr", "   ", "", " ", "", "  ", 1, 1);
        var fullOrder = SampleOrder(OrderDestination.EatIn, "12", null, null,
            Item("Burger maison", 2, 1450, Burger, extra: 150, mods: ["Sans oignon", "Cuisson à point"]),
            Item("Salade César", 1, 1100, Salad));
        var takeawayOrder = SampleOrder(OrderDestination.Takeaway, "Comptoir", "A42", "7", Item("Café", 2, 250, Coffee));
        var arOrder = SampleOrder(OrderDestination.EatIn, "3", null, null, Item("Burger maison", 1, 1450, Burger));
        var receipt = SampleReceipt(fullOrder, "T01-000042", "2025-07-15T10:30:45.123Z");

        Scenario Receipt(string name, string lang, FiscalReceipt r, Order o, RestaurantSettingsDto? s, int? dup = null) =>
            new(name, "receipt", lang, new { receipt = InReceipt(r), order = InOrder(o), settings = InSettings(s), duplicateNumber = dup },
                TicketDocumentBuilder.Receipt(r, o, lang, s, dup));

        var takeawayReceipt = SampleReceipt(takeawayOrder, "T02-000007", "2025-01-15T18:05:09.000Z");
        var arReceipt = SampleReceipt(arOrder, "T01-000043", "2025-07-15T11:00:00.000Z");
        return
        [
            Receipt("receipt_fr_full", "fr", receipt, fullOrder, fullSettings),
            Receipt("receipt_en_default_header", "en", takeawayReceipt, takeawayOrder, null),
            Receipt("receipt_ar", "ar", arReceipt, arOrder, fullSettings),
            Receipt("receipt_duplicate_2", "fr", receipt, fullOrder, fullSettings, 2),
            Receipt("receipt_unknown_language", "de", receipt, fullOrder, null),
            Receipt("receipt_empty_settings_strings", "fr", receipt, fullOrder, blankSettings),
            new("fallback_fr", "receiptFallback", "fr", new { receipt = InReceipt(receipt), settings = InSettings(fullSettings), duplicateNumber = (int?)null },
                TicketDocumentBuilder.ReceiptFallback(receipt, "fr", fullSettings)),
            new("fallback_duplicate", "receiptFallback", "en", new { receipt = InReceipt(receipt), settings = InSettings(null), duplicateNumber = (int?)3 },
                TicketDocumentBuilder.ReceiptFallback(receipt, "en", null, 3))
        ];
    }

    private static List<Scenario> FiscalAndPickupScenarios()
    {
        var settings = new RestaurantSettingsDto("fr", "fr", "Chez Léa", "5 Rue des Lilas\n69002 Lyon", "12345678900011", "FR98123456789", "NF525-2024-0042", 1, 1);
        var order = SampleOrder(OrderDestination.Takeaway, "Comptoir", "B17", "9",
            Item("Burger maison", 1, 1450, Burger, extra: 100, mods: ["Double fromage", "Sans sauce"]), Item("Tiramisu", 2, 600, Tiramisu));
        var receipt = SampleReceipt(order, "T01-000050", "2025-10-26T11:15:30.000Z");
        var now = Utc("2025-07-15T10:30:45.500Z");

        Scenario Fiscal(string name, string? buzzer) =>
            new(name, "fiscalReceipt", "fr",
                new { receipt = InReceipt(receipt), order = InOrder(order), settings = InSettings(settings), pickupNumber = "B17", buzzer, duplicateNumber = (int?)null },
                TicketDocumentBuilder.FiscalReceipt(receipt, order, "B17", buzzer, "fr", settings));
        Scenario Pickup(string name, string lang, string? buzzer) =>
            new(name, "pickupCoupon", lang, new { order = InOrder(order), pickupNumber = "B17", buzzer, nowUtc = Text(now) },
                TicketDocumentBuilder.PickupCoupon(order, "B17", buzzer, lang, now));
        return
        [
            Fiscal("fiscal_receipt_buzzer", "9"),
            Fiscal("fiscal_receipt_no_buzzer", null),
            Pickup("pickup_modifiers", "fr", "9"),
            Pickup("pickup_no_buzzer", "en", null)
        ];
    }

    private static List<Scenario> KitchenScenarios()
    {
        Scenario Kitchen(string name, string lang, Order order, KitchenTicket ticket) =>
            new(name, "kitchenTicket", lang, new { kitchen = InKitchen(ticket), order = InOrder(order) },
                TicketDocumentBuilder.KitchenTicket(ticket, order, lang));

        var takeaway = SampleOrder(OrderDestination.Takeaway, "Comptoir", "C03", "12", Item("Burger maison", 2, 1450, Burger));
        var table = SampleOrder(OrderDestination.EatIn, "21", null, null,
            Item("Salade César", 1, 1100, Salad, CourseType.Suite), Item("Tiramisu", 1, 600, Tiramisu, CourseType.Dessert), Item("Café", 1, 250, Coffee, CourseType.OnDemand));
        var ar = SampleOrder(OrderDestination.EatIn, "5", null, null, Item("Burger maison", 1, 1450, Burger));
        return
        [
            Kitchen("kitchen_takeaway_pickup_buzzer_comment", "en", takeaway,
                SampleKitchen("Marie", 1, "2025-07-15T10:30:45.000Z", ("Burger maison", 2, Burger, "Sans oignon, Bien cuit", "Allergie arachides"))),
            Kitchen("kitchen_table_courses", "fr", table,
                SampleKitchen("Paul", 4, "2025-01-15T19:45:00.000Z",
                    ("Salade César", 1, Salad, null, null), ("Tiramisu", 1, Tiramisu, null, "Bougie"), ("Café", 1, Coffee, "Serré", null), ("Plat disparu", 3, Absent, null, null))),
            Kitchen("kitchen_ar", "ar", ar, SampleKitchen("ليلى", 2, "2025-07-15T20:05:00.000Z", ("Burger maison", 1, Burger, "بدون بصل", null)))
        ];
    }

    private static List<Scenario> ReportScenarios()
    {
        var settings = new RestaurantSettingsDto("fr", "fr", "Chez Léa", "5 Rue des Lilas\n69002 Lyon", "12345678900011", "FR98123456789", "NF525-2024-0042", 1, 1);
        var details = new ReportPrintData(
            [
                new ReportCategoryGroup("Boissons", [new ReportItemLine("Café", 3, 750), new ReportItemLine("Eau", 2, 500)], 1250),
                new ReportCategoryGroup(null, [new ReportItemLine("Divers", 1, 300)], 300)
            ],
            [new ReportTipLine("Alice", 500), new ReportTipLine(null, 200)], 700, true);
        var plain = new ReportPrintData([new ReportCategoryGroup("Plats", [new ReportItemLine("Burger", 2, 2900)], 2900)], [], 0, false);

        FiscalSummaryDto Summary(string start, string end, long seq) => new("T01", start == "" ? DateTimeOffset.MinValue : Utc(start), Utc(end),
            12345 + seq, 11000, 17,
            new Dictionary<decimal, long> { [10m] = 1000, [5.5m] = 345 },
            new Dictionary<PaymentMethod, long> { [PaymentMethod.CreditCard] = 8000, [PaymentMethod.Cash] = 4345 }, 987654);
        Scenario X(string name, string lang, FiscalSummaryDto s, ReportPrintData d, string now, RestaurantSettingsDto? st) =>
            new(name, "xReport", lang, new { summary = InSummary(s), reportData = InReportData(d), nowUtc = now, settings = InSettings(st) },
                TicketDocumentBuilder.XReport(s, d, lang, Utc(now), st));

        DailyFiscalClosureDto Closure(Dictionary<decimal, long> vat) => new(Guid.Empty, "T01", 7, 15000, 13500, 21, vat,
            new Dictionary<PaymentMethod, long> { [PaymentMethod.MealVoucher] = 3000, [PaymentMethod.Cash] = 12000, [PaymentMethod.RoomCharge] = 0 },
            1234567, Signature, Utc("2025-07-15T21:30:00.000Z"), Utc("2025-07-15T07:00:00.000Z"), "Jean Dupont");
        Scenario Z(string name, string lang, DailyFiscalClosureDto c, ReportPrintData d, RestaurantSettingsDto? st, int? dup = null) =>
            new(name, "zClosure", lang, new { closure = InClosure(c), reportData = InReportData(d), settings = InSettings(st), duplicateNumber = dup },
                TicketDocumentBuilder.ZClosure(c, d, lang, st, dup));

        PeriodClosureDto Period(FiscalPeriodType type, string key, string taxes, string tenders) => new(Guid.Empty, "T01", type, key, 3,
            Utc(type == FiscalPeriodType.Monthly ? "2025-06-01T00:00:00.000Z" : "2025-01-01T00:00:00.000Z"), Utc("2025-06-30T21:59:59.000Z"),
            50000, 45000, taxes, tenders, 9999999, 28, "prev", Signature, Guid.Empty, "Jean Dupont", Utc("2025-07-01T08:00:00.000Z"));
        Scenario P(string name, PeriodClosureDto c, ReportPrintData d, RestaurantSettingsDto? st) =>
            new(name, "periodClosure", "fr", new { periodClosure = InPeriod(c), reportData = InReportData(d), settings = InSettings(st) },
                TicketDocumentBuilder.PeriodClosure(c, d, "fr", st));

        const string Taxes = "{\"10\":3000,\"5.5\":1500}";
        return
        [
            X("x_first_report", "fr", Summary("", "2025-07-15T10:30:00.000Z", 0), NoData, "2025-07-15T10:31:00.000Z", settings),
            X("x_with_details", "fr", Summary("2025-07-14T06:00:00.000Z", "2025-07-15T10:30:00.000Z", 1), details, "2025-07-15T10:31:00.000Z", null),
            X("x_summer_dst", "en", Summary("2025-03-30T00:30:00.000Z", "2025-03-30T01:30:00.000Z", 2), plain, "2025-03-30T01:45:00.000Z", settings),
            X("x_winter", "ar", Summary("2025-01-15T06:00:00.000Z", "2025-01-15T22:00:00.000Z", 3), plain, "2025-01-15T22:05:00.000Z", settings),
            Z("z_fr", "fr", Closure(new Dictionary<decimal, long> { [10m] = 1000, [5.5m] = 500 }), details, settings),
            Z("z_duplicate_certificate", "en", Closure(new Dictionary<decimal, long> { [20m] = 2500 }), plain, settings, 2),
            Z("z_rates", "fr", Closure(new Dictionary<decimal, long> { [20m] = 300, [5.5m] = 100, [0.125m] = 5, [10m] = 200, [2.1m] = 50 }), NoData, null),
            P("period_monthly", Period(FiscalPeriodType.Monthly, "2025-06", Taxes, "{\"Cash\":20000,\"CreditCard\":30000}"), details, settings),
            P("period_annual", Period(FiscalPeriodType.Annual, "2025", Taxes, "{\"CreditCard\":30000}"), plain, null),
            P("period_invalid_json", Period(FiscalPeriodType.Monthly, "2025-06", "not json{", ""), NoData, null),
            P("period_tender_keys", Period(FiscalPeriodType.Monthly, "2025-06", Taxes, "{\"Cash\":1000,\"1\":2000,\"7\":50,\"Bogus\":300}"), NoData, null)
        ];
    }

    private static List<Scenario> MiscScenarios()
    {
        var now = Utc("2025-07-15T10:30:45.000Z");
        Scenario Test(string name, string lang, int width, string printerName, string ip) =>
            new(name, "testPage", lang,
                new { printer = new { name = printerName, ipAddress = ip, port = 9100, paperWidthMm = width }, nowUtc = Text(now) },
                TicketDocumentBuilder.TestPage(new PrinterConfiguration { Name = printerName, IpAddress = ip, PaperWidthMm = width }, lang, now));

        var withNotice = TicketDocumentBuilder.ReceiptFallback(SampleReceipt(SampleOrder(OrderDestination.EatIn, "1", null, null, Item("Café", 1, 250, Coffee)), "T01-000001", "2025-07-15T10:30:45.000Z"), "fr", null, 1);
        var withoutNotice = TicketDocumentBuilder.PickupCoupon(SampleOrder(OrderDestination.Takeaway, "Comptoir", "A1", null, Item("Café", 1, 250, Coffee)), "A1", null, "fr", now);
        return
        [
            Test("test_fr_80", "fr", 80, "Cuisine", "192.168.1.50"),
            Test("test_ar_58", "ar", 58, "Bar", "10.0.0.7"),
            new("duplicate_notice_replaces", "withDuplicateNotice", "fr", new { document = ToElement(withNotice), duplicateNumber = 3 },
                TicketDocumentBuilder.WithDuplicateNotice(withNotice, 3)),
            new("duplicate_notice_inserts", "withDuplicateNotice", "fr", new { document = ToElement(withoutNotice), duplicateNumber = 2 },
                TicketDocumentBuilder.WithDuplicateNotice(withoutNotice, 2))
        ];
    }

    // MARK: entités du domaine

    private static OrderItem Item(string name, int qty, long unitCents, Guid productId, CourseType course = CourseType.Direct, long extra = 0, string[]? mods = null) =>
        new()
        {
            ProductName = name,
            Quantity = qty,
            UnitPrice = Money.FromCents(unitCents),
            ModifiersPriceExtra = Money.FromCents(extra),
            ProductId = productId,
            Course = course,
            SelectedModifiers = [.. mods ?? []]
        };

    private static Order SampleOrder(OrderDestination dest, string table, string? pickup, string? buzzer, params OrderItem[] items) =>
        new() { Destination = dest, TableNumber = table, PickupNumber = pickup, PickupBuzzer = buzzer, Items = [.. items] };

    private static FiscalReceipt SampleReceipt(Order order, string number, string createdAt)
    {
        var ttc = order.Items.Sum(i => i.CalculateTotalTtc().AmountInCents);
        var ht = ttc * 10 / 11;
        return new FiscalReceipt
        {
            TerminalId = number[..3],
            ReceiptNumber = number,
            CreatedAtUtc = Utc(createdAt),
            TotalTtcAmount = Money.FromCents(ttc),
            TotalHtAmount = Money.FromCents(ht),
            TaxBreakdownJson = $"{{\"10\":{ttc - ht}}}",
            SignatureHash = Signature
        };
    }

    private static KitchenTicket SampleKitchen(string server, int covers, string dispatchedAt,
        params (string Name, int Qty, Guid Product, string? Modifiers, string? Comment)[] items) =>
        new()
        {
            TableNumber = "x",
            StationId = "HOT_KITCHEN",
            ServerName = server,
            CoversCount = covers,
            DispatchedAtUtc = Utc(dispatchedAt),
            Items = [.. items.Select(i => new KitchenTicketItem
            {
                ProductName = i.Name,
                Quantity = i.Qty,
                ProductId = i.Product,
                ModifiersSummary = i.Modifiers,
                KitchenComment = i.Comment
            })]
        };

    // MARK: entrées (schéma camelCase décodé par TicketScenarioInput côté Swift)

    private static string Text(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string? Opt(DateTimeOffset d) => d == DateTimeOffset.MinValue ? null : Text(d);

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static object? InSettings(RestaurantSettingsDto? s) => s is null ? null
        : new { companyName = s.CompanyName, addressLines = s.AddressLines, siret = s.Siret, vatNumber = s.VatNumber, certificateNumber = s.CertificateNumber };

    private static object InOrder(Order o) => new
    {
        destination = (int)o.Destination,
        tableNumber = o.TableNumber,
        pickupNumber = o.PickupNumber,
        pickupBuzzer = o.PickupBuzzer,
        items = o.Items.Select(i => new
        {
            quantity = i.Quantity,
            productName = i.ProductName,
            productId = i.ProductId.ToString("D"),
            totalTtcCents = i.CalculateTotalTtc().AmountInCents,
            selectedModifiers = i.SelectedModifiers,
            course = (int)i.Course
        }).ToArray()
    };

    private static object InReceipt(FiscalReceipt r) => new
    {
        receiptNumber = r.ReceiptNumber,
        createdAtUtc = Text(r.CreatedAtUtc),
        terminalId = r.TerminalId,
        totalTtcCents = r.TotalTtcAmount.AmountInCents,
        totalHtCents = r.TotalHtAmount.AmountInCents,
        taxBreakdownJson = r.TaxBreakdownJson,
        signatureHash = r.SignatureHash
    };

    private static object InKitchen(KitchenTicket t) => new
    {
        serverName = t.ServerName,
        coversCount = t.CoversCount,
        dispatchedAtUtc = Text(t.DispatchedAtUtc),
        items = t.Items.Select(i => new
        {
            quantity = i.Quantity,
            productName = i.ProductName,
            productId = i.ProductId.ToString("D"),
            modifiersSummary = i.ModifiersSummary,
            kitchenComment = i.KitchenComment
        }).ToArray()
    };

    // Ordre d'insertion du dictionnaire conservé : le Swift doit retrier comme le builder.
    private static object[] Rates(IReadOnlyDictionary<decimal, long> d) =>
        d.Select(kv => (object)new { rate = kv.Key.ToString(CultureInfo.InvariantCulture), cents = kv.Value }).ToArray();

    private static object[] Methods(IReadOnlyDictionary<PaymentMethod, long> d) =>
        d.Select(kv => (object)new { method = (int)kv.Key, cents = kv.Value }).ToArray();

    private static object InSummary(FiscalSummaryDto s) => new
    {
        terminalId = s.TerminalId,
        periodStartUtc = Opt(s.PeriodStartUtc),
        periodEndUtc = Text(s.PeriodEndUtc),
        totalSalesTtcCents = s.TotalSalesTtcCents,
        totalSalesHtCents = s.TotalSalesHtCents,
        receiptCount = s.ReceiptCount,
        vatBreakdownCents = Rates(s.VatBreakdownCents),
        paymentTotalsCents = Methods(s.PaymentTotalsCents),
        perpetualGrandTotalCents = s.PerpetualGrandTotalCents
    };

    private static object InClosure(DailyFiscalClosureDto c) => new
    {
        terminalId = c.TerminalId,
        closureSequence = c.ClosureSequence,
        totalSalesTtcCents = c.TotalSalesTtcCents,
        totalSalesHtCents = c.TotalSalesHtCents,
        receiptCount = c.ReceiptCount,
        vatBreakdownCents = Rates(c.VatBreakdownCents),
        paymentTotalsCents = Methods(c.PaymentTotalsCents),
        perpetualGrandTotalCents = c.PerpetualGrandTotalCents,
        signatureHash = c.SignatureHash,
        closedAtUtc = Text(c.ClosedAtUtc),
        periodStartUtc = Opt(c.PeriodStartUtc),
        sealedByUserName = c.SealedByUserName
    };

    private static object InPeriod(PeriodClosureDto c) => new
    {
        terminalId = c.TerminalId,
        isMonthly = c.PeriodType == FiscalPeriodType.Monthly,
        periodKey = c.PeriodKey,
        closureSequence = c.ClosureSequence,
        periodStartUtc = Opt(c.PeriodStartUtc),
        periodEndUtc = Text(c.PeriodEndUtc),
        totalTtcCents = c.TotalTtcCents,
        totalHtCents = c.TotalHtCents,
        taxesSummaryJson = c.TaxesSummaryJson,
        tenderTotalsJson = c.TenderTotalsJson,
        perpetualGrandTotalCents = c.PerpetualGrandTotalCents,
        dailyClosureCount = c.DailyClosureCount,
        signatureHash = c.SignatureHash,
        sealedByUserName = c.SealedByUserName
    };

    private static object InReportData(ReportPrintData d) => new
    {
        categories = d.Categories.Select(g => new
        {
            categoryName = g.CategoryName,
            items = g.Items.Select(i => new { productName = i.ProductName, quantity = i.Quantity, totalTtcCents = i.TotalTtcCents }).ToArray(),
            subtotalTtcCents = g.SubtotalTtcCents
        }).ToArray(),
        tips = d.Tips.Select(t => new { serverName = t.ServerName, tipCents = t.TipCents }).ToArray(),
        totalTipsCents = d.TotalTipsCents,
        hasGlobalDiscount = d.HasGlobalDiscount
    };

    // MARK: utilitaires

    private static JsonElement ToElement(TicketDocument doc) => JsonDocument.Parse(TicketDocumentJson.Serialize(doc)).RootElement.Clone();

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();
}
