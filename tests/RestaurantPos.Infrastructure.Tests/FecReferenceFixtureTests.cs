using System.Globalization;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad (mode autonome) : le FEC Swift (<c>LocalFecExporter</c>) doit être identique octet pour octet à celui du .NET
/// pour les mêmes reçus. Un seul poste (<c>T01</c>) : le .NET ne filtre pas par poste.
/// Régénérer avec <c>WRITE_FEC_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FecReferenceFixtureTests</c>
/// après toute modification de <c>FecExportService</c>. Générer sous macOS ou Linux : <c>AppendLine</c> y écrit <c>\n</c>.
/// </summary>
public class FecReferenceFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Tender(PaymentMethod Method, long Amount, long Tendered, long Change);

    private sealed record Receipt(int Seq, string Number, string CreatedAt, long Ttc, long Ht, string Taxes, string? Hts, int? VoidsSeq, Tender[] Tenders);

    private sealed record Export(string Name, string From, string To, string Siren);

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "fec_reference.json");
    }

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static Guid ReceiptId(int seq) => Guid.Parse($"00000000-0000-0000-0000-{seq:D12}");

    private static Guid TenderId(int seq, int index) => Guid.Parse($"00000000-0000-0000-{index + 1:D4}-{seq:D12}");

    private static List<Receipt> Receipts() =>
    [
        // Juste avant septembre : hors de l'export du mois, dans celui de l'année.
        new(1, "T01-000001", "2026-08-31T23:59:59.999Z", 1100, 1000, "{\"10\":100}", "{\"10\":1000}", null, [new(PaymentMethod.Cash, 1100, 1100, 0)]),
        // Sur place 10 %, espèces avec rendu : le FEC prend le montant réglé, pas le montant remis.
        new(2, "T01-000002", "2026-09-01T11:15:00.000Z", 2450, 2227, "{\"10\":223}", "{\"10\":2227}", null, [new(PaymentMethod.Cash, 2450, 3000, 550)]),
        // Sur place 10 % + alcool 20 %, carte avec 5,00 de pourboire : excédent au 468600.
        new(3, "T01-000003", "2026-09-01T20:45:00.000Z", 4500, 4000, "{\"10\":300,\"20\":200}", "{\"10\":3000,\"20\":1000}", null,
            [new(PaymentMethod.CreditCard, 5000, 5000, 0)]),
        // À emporter 5,5 % + 20 %, remise répartie au plus fort reste (901 / 449) ; titre-restaurant puis espèces (ordre des règlements).
        new(4, "T01-000004", "2026-09-02T12:30:00.000Z", 1350, 1228, "{\"5.5\":47,\"20\":75}", "{\"5.5\":854,\"20\":374}", null,
            [new(PaymentMethod.MealVoucher, 1000, 1000, 0), new(PaymentMethod.Cash, 350, 400, 50)]),
        // Historique (HtBreakdownJson NULL), deux taux bas, 23 h 30 UTC = 1 h 30 à Paris : HT déduit (5,5 % : 945 ; 2,1 % : 982), daté du jour UTC.
        new(5, "T01-000005", "2026-09-02T23:30:00.000Z", 2000, 1927, "{\"2.1\":21,\"5.5\":52}", null, null, [new(PaymentMethod.GiftCard, 2000, 2000, 0)]),
        // Historique à clé « 10.0 » : libellé « 10 ».
        new(6, "T01-000006", "2026-09-03T08:00:00.000Z", 1100, 1000, "{\"10.0\":100}", null, null, [new(PaymentMethod.CreditCard, 1100, 1100, 0)]),
        // Annulation du reçu 3 : montants négatifs, pourboire contre-passé au débit du 468600.
        new(7, "T01-VOID-000007", "2026-09-03T09:00:00.000Z", -4500, -4000, "{\"10\":-300,\"20\":-200}", "{\"10\":-3000,\"20\":-1000}", 3,
            [new(PaymentMethod.CreditCard, -5000, -5000, 0)]),
        // Taux 0 % + 20 %, HT exact : le 0 % garde son HT au 706000, sans ligne de TVA.
        new(8, "T01-000008", "2026-09-03T12:00:00.000Z", 1700, 1500, "{\"0\":0,\"20\":200}", "{\"0\":500,\"20\":1000}", null, [new(PaymentMethod.Cash, 1700, 1700, 0)]),
        // HtBreakdownJson incohérent (somme fausse) : repli sur la déduction (945 / 982).
        new(9, "T01-000009", "2026-09-04T12:00:00.000Z", 2000, 1927, "{\"2.1\":21,\"5.5\":52}", "{\"2.1\":979,\"5.5\":900}", null,
            [new(PaymentMethod.CreditCard, 2000, 2000, 0)]),
        // Sans règlement enregistré : contrepartie en caisse (530000, « Reglement Caisse »).
        new(10, "T01-000010", "2026-09-04T13:00:00.000Z", 800, 727, "{\"10\":73}", "{\"10\":727}", null, []),
        // Historique hors règle actuelle (HT signé trop bas) : reste négatif repris dans les plages cohérentes ; écriture déséquilibrée, comme au .NET.
        new(11, "T01-000011", "2026-09-05T10:00:00.000Z", 2100, 1800, "{\"0\":0,\"10\":100,\"20\":200}", null, null, [new(PaymentMethod.CreditCard, 2100, 2100, 0)]),
        // Après septembre ; facturation chambre (seul l'iPad en écrit) : compte 411000.
        new(12, "T01-000012", "2026-10-01T00:00:00.000Z", 600, 500, "{\"20\":100}", "{\"20\":500}", null, [new(PaymentMethod.RoomCharge, 600, 600, 0)]),
    ];

    private static List<Export> Exports() =>
    [
        new("septembre 2026", "2026-09-01T00:00:00.000Z", "2026-09-30T23:59:59.999Z", "123456789"),
        new("bornes incluses, dates inversees, SIREN entoure d'espaces", "2026-09-03T09:00:00.000Z", "2026-09-01T20:45:00.000Z", " 987654321 "),
        new("SIREN vide, periode sans recu", "2026-11-01T00:00:00.000Z", "2026-11-30T00:00:00.000Z", ""),
        new("annee 2026", "2026-01-01T00:00:00.000Z", "2026-12-31T23:59:59.999Z", "123456789"),
    ];

    private static async Task SeedAsync(DbContextOptions<AppDbContext> options, List<Receipt> receipts)
    {
        using var db = new AppDbContext(options);
        foreach (var r in receipts)
        {
            var receipt = new FiscalReceipt
            {
                Id = ReceiptId(r.Seq),
                TerminalId = "T01",
                ReceiptNumber = r.Number,
                OrderId = Guid.Parse($"00000000-0000-0000-ffff-{r.Seq:D12}"),
                SequenceNumber = r.Seq,
                TotalTtcAmount = Money.FromCents(r.Ttc),
                TotalHtAmount = Money.FromCents(r.Ht),
                TaxBreakdownJson = r.Taxes,
                HtBreakdownJson = r.Hts,
                CreatedAtUtc = Utc(r.CreatedAt),
                VoidedReceiptId = r.VoidsSeq is int voided ? ReceiptId(voided) : null,
            };
            for (int i = 0; i < r.Tenders.Length; i++)
            {
                var t = r.Tenders[i];
                receipt.Tenders.Add(new PaymentTender
                {
                    Id = TenderId(r.Seq, i),
                    FiscalReceiptId = receipt.Id,
                    Method = t.Method,
                    Amount = Money.FromCents(t.Amount),
                    Tendered = Money.FromCents(t.Tendered),
                    ChangeGiven = Money.FromCents(t.Change),
                });
            }

            db.FiscalReceipts.Add(receipt);
        }

        await db.SaveChangesAsync();
    }

    private static object Describe(Receipt r) => new
    {
        id = ReceiptId(r.Seq),
        receiptNumber = r.Number,
        sequenceNumber = r.Seq,
        createdAtUtc = r.CreatedAt,
        ttcCents = r.Ttc,
        htCents = r.Ht,
        taxBreakdownJson = r.Taxes,
        htBreakdownJson = r.Hts,
        voidedReceiptId = r.VoidsSeq is int voided ? ReceiptId(voided) : (Guid?)null,
        tenders = r.Tenders.Select((t, i) => new
        {
            id = TenderId(r.Seq, i),
            method = (int)t.Method,
            amountCents = t.Amount,
            tenderedCents = t.Tendered,
            changeGivenCents = t.Change,
        }),
    };

    [Fact]
    public async Task GeneratesOrChecksTheFecFixture()
    {
        string? previousTz = Environment.GetEnvironmentVariable("TZ");
        try
        {
            Environment.SetEnvironmentVariable("TZ", "Europe/Paris");
            TimeZoneInfo.ClearCachedData();
            TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 9, 2)).Should().Be(TimeSpan.FromHours(2), "le générateur doit tourner en heure de Paris");

            var receipts = Receipts();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("FecFixture_" + Guid.NewGuid().ToString("N")).Options;
            await SeedAsync(options, receipts);

            // Second contexte : le service relit les reçus et leurs règlements comme en production (rien de suivi en mémoire).
            using var db = new AppDbContext(options);
            var service = new FecExportService(db);
            var exports = new List<object>();
            foreach (var e in Exports())
            {
                var r = await service.GenerateFecAsync(new FecExportRequest(Utc(e.From), Utc(e.To), e.Siren));
                exports.Add(new
                {
                    name = e.Name,
                    fromUtc = e.From,
                    toUtc = e.To,
                    siren = e.Siren,
                    fileName = r.FileName,
                    totalRecords = r.TotalRecords,
                    totalDebitCents = (long)(r.TotalDebit * 100m),
                    totalCreditCents = (long)(r.TotalCredit * 100m),
                    fecText = Encoding.UTF8.GetString(r.FileBytes),
                    fecBase64 = Convert.ToBase64String(r.FileBytes),
                });
            }

            string json = JsonSerializer.Serialize(new { receipts = receipts.Select(Describe), exports }, JsonOptions);

            if (Environment.GetEnvironmentVariable("WRITE_FEC_FIXTURE") == "1")
            {
                File.WriteAllText(FixturePath(), json);
            }

            File.ReadAllText(FixturePath()).ReplaceLineEndings().Should().Be(json.ReplaceLineEndings(),
                "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_FEC_FIXTURE=1");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TZ", previousTz);
            TimeZoneInfo.ClearCachedData();
        }
    }
}
