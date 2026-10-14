using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad (mode autonome) : le Swift rejoue ces données et doit écrire les mêmes fichiers JSON d'archive, octet pour octet,
/// et les mêmes signatures d'archive. Régénérer avec
/// <c>WRITE_ARCHIVE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter ArchiveReferenceFixtureTests</c>
/// après toute modification de <c>FiscalArchiveService</c>, de <c>FiscalHashing.ComputeArchiveHash</c> ou des entités sérialisées
/// (<c>FiscalReceipt</c>, <c>PaymentTender</c>, <c>DailyFiscalClosure</c>, <c>FiscalPeriodClosure</c>, <c>TransactionJournalEntry</c>, <c>Money</c>).
/// </summary>
public partial class ArchiveReferenceFixtureTests : IDisposable
{
    private const string Genesis = NF525FiscalAuditService.GenesisHash;
    private static readonly Guid Manager = G(900);
    private static readonly string[] DataFiles = ["receipts.json", "closures.json", "period-closures.json", "journal.json"];

    // Mensuelle 2025-03 : 4 reçus, 2 Z, 1 clôture, 3 entrées JET. Annuelle 2025 : 6, 3, 3 (mars, avril, l'annuelle), 5.
    private static readonly (Guid ClosureId, int[] Counts)[] Expected = [(G(32), [4, 2, 1, 3]), (G(34), [6, 3, 3, 5])];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pos_archive_fixture_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }

        GC.SuppressFinalize(this);
    }

    [GeneratedRegex("\"(archiveId|createdAtUtc)\": \"[^\"]*\"")]
    private static partial Regex VolatileManifestField();

    private static Guid G(int n) => Guid.Parse("A0B1C2D3-E4F5-4000-8000-" + n.ToString("D12", CultureInfo.InvariantCulture));

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "archive_reference.json");
    }

    private sealed record TenderRow(Guid Id, int Method, long Amount, long Tendered, long ChangeGiven);

    private sealed record ReceiptRow(
        Guid Id, string TerminalId, string ReceiptNumber, Guid OrderId, long SequenceNumber, long TtcCents, long HtCents, string TaxBreakdownJson,
        string? HtBreakdownJson, string PreviousSignatureHash, string SignatureHash, string CreatedAtUtc, Guid? VoidedReceiptId, List<TenderRow> Tenders);

    private sealed record DailyRow(
        Guid Id, string TerminalId, long ClosureSequence, string PeriodStartUtc, string PeriodEndUtc, long TtcCents, long HtCents,
        string TaxesSummaryJson, string TenderTotalsJson, long PerpetualGrandTotalCents, string PreviousSignatureHash, string SignatureHash,
        string SealedByUserName, Guid SealedByUserId);

    private sealed record PeriodRow(
        Guid Id, string TerminalId, int PeriodType, string PeriodKey, long ClosureSequence, string PeriodStartUtc, string PeriodEndUtc,
        long TtcCents, long HtCents, string TaxesSummaryJson, string TenderTotalsJson, long PerpetualGrandTotalCents, int DailyClosureCount,
        string PreviousSignatureHash, string SignatureHash, Guid SealedByUserId, string SealedByUserName, string CreatedAtUtc);

    private sealed record JournalRow(
        Guid Id, long LocalSequence, string? TerminalId, string OccurredAtUtc, string IdempotencyKey, string EventType, string PayloadJson,
        string EntryHash, long ChainSequence, string? PreviousHash, Guid? OperatorId);

    // Mars 2025 à Paris : [2025-02-28T23:00Z ; 2025-03-31T22:00Z]. Exercice 2025 : [2024-12-31T23:00Z ; 2025-12-31T23:00Z].
    private static List<ReceiptRow> Receipts() =>
    [
        // Juste avant le début de mars : annuelle seulement.
        new(G(1), "T01", "T01-000001", G(501), 1, 1000, 909, "{\"10\":91}", "{\"10\":909}", Genesis, "SIG-R1", "2025-02-28T22:59:59.999Z", null,
            [new(G(11), 0, 1000, 2000, 1000)]),
        // Exactement au début de mars ; deux règlements.
        new(G(2), "T01", "T01-000002", G(502), 2, 1950, 1773, "{\"10\":177}", "{\"10\":1773}", "SIG-R1", "SIG-R2", "2025-02-28T23:00:00.000Z", null,
            [new(G(12), 1, 1000, 1000, 0), new(G(13), 2, 950, 950, 0)]),
        // Millisecondes non nulles (.120 → « .12 » dans le JSON).
        new(G(3), "T01", "T01-000003", G(503), 3, 2500, 2370, "{\"5.5\":130}", "{\"5.5\":2370}", "SIG-R2", "SIG-R3", "2025-03-15T12:34:56.120Z", null,
            [new(G(14), 0, 2500, 2500, 0)]),
        // Annulation de R3 : montants négatifs, HT par taux nul, VoidedReceiptId posé.
        new(G(4), "T01", "T01-000004", G(503), 4, -2500, -2370, "{\"5.5\":-130}", null, "SIG-R3", "SIG-R4", "2025-03-16T09:00:00.000Z", G(3),
            [new(G(15), 0, -2500, -2500, 0)]),
        // Exactement à la fin de mars (borne incluse), sans règlement.
        new(G(5), "T01", "T01-000005", G(505), 5, 800, 727, "{\"10\":73}", "{\"10\":727}", "SIG-R4", "SIG-R5", "2025-03-31T22:00:00.000Z", null, []),
        // Avril : annuelle seulement.
        new(G(6), "T01", "T01-000006", G(506), 6, 1200, 1091, "{\"10\":109}", "{\"10\":1091}", "SIG-R5", "SIG-R6", "2025-04-02T10:00:00.000Z", null,
            [new(G(16), 1, 1200, 1200, 0)]),
        // Autre poste : jamais dans une archive de T01.
        new(G(7), "T02", "T02-000001", G(507), 1, 3000, 2727, "{\"10\":273}", "{\"10\":2727}", Genesis, "SIG-R7", "2025-03-10T10:00:00.000Z", null,
            [new(G(17), 0, 3000, 3000, 0)]),
    ];

    private static List<DailyRow> Dailies() =>
    [
        new(G(21), "T01", 1, "2025-02-27T23:00:00.000Z", "2025-02-28T22:59:59.999Z", 1000, 909, "{\"10\":91}", "{\"Cash\":1000}", 1000,
            Genesis, "SIG-Z1", "Chef", Manager),
        new(G(22), "T01", 2, "2025-02-28T23:30:00.000Z", "2025-03-01T08:00:00.000Z", 1950, 1773, "{\"10\":177}",
            "{\"CreditCard\":1000,\"MealVoucher\":950}", 2950, "SIG-Z1", "SIG-Z2", "Chef", Manager),
        // Commence exactement à la fin de mars : dans mars (<=) et dans avril (>=).
        new(G(23), "T01", 3, "2025-03-31T22:00:00.000Z", "2025-04-02T21:00:00.000Z", 2000, 1818, "{\"10\":182}",
            "{\"CreditCard\":1200,\"Cash\":800}", 4950, "SIG-Z2", "SIG-Z3", "H\u00e9l\u00e8ne", Manager),
        new(G(24), "T02", 1, "2025-03-10T08:00:00.000Z", "2025-03-10T22:00:00.000Z", 3000, 2727, "{\"10\":273}", "{\"Cash\":3000}", 3000,
            Genesis, "SIG-Z4", "Chef", Manager),
    ];

    private static List<PeriodRow> Periods() =>
    [
        // Décembre 2024 : finit au début de l'exercice 2025, hors de l'annuelle.
        new(G(31), "T01", 1, "2024-12", 1, "2024-11-30T23:00:00.000Z", "2024-12-31T23:00:00.000Z", 0, 0, "{}", "{}", 0, 0,
            Genesis, "SIG-M1", Manager, "Chef", "2025-01-02T09:00:00.000Z"),
        new(G(32), "T01", 1, "2025-03", 2, "2025-02-28T23:00:00.000Z", "2025-03-31T22:00:00.000Z", 1950, 1773, "{\"10\":177}",
            "{\"CreditCard\":1000,\"MealVoucher\":950}", 2950, 1, "SIG-M1", "SIG-M2", Manager, "Chef", "2025-04-01T08:00:00.000Z"),
        new(G(33), "T01", 1, "2025-04", 3, "2025-03-31T22:00:00.000Z", "2025-04-30T22:00:00.000Z", 2000, 1818, "{\"10\":182}",
            "{\"CreditCard\":1200,\"Cash\":800}", 4950, 1, "SIG-M2", "SIG-M3", Manager, "Chef", "2025-05-01T08:00:00.000Z"),
        new(G(34), "T01", 2, "2025", 1, "2024-12-31T23:00:00.000Z", "2025-12-31T23:00:00.000Z", 3950, 3591, "{\"10\":359}",
            "{\"CreditCard\":2200,\"MealVoucher\":950,\"Cash\":800}", 4950, 2, Genesis, "SIG-A1", Manager, "H\u00e9l\u00e8ne \"Chef\"",
            "2026-01-05T09:00:00.000Z"),
    ];

    private static List<JournalRow> Journal() =>
    [
        new(G(41), 1, "T01", "2025-02-28T22:59:59.999Z", "idem-1", "Z_CLOSURE", "{\"ClosureSequence\":1}", "HASH-1", 1, Genesis, Manager),
        // Poste, opérateur et hash précédent nuls ; caractères échappés par System.Text.Json.
        new(G(42), 2, null, "2025-02-28T23:00:00.000Z", "idem-2", "BACKUP_COMPLETED",
            "{\"fileName\":\"caf\u00e9 <1> & 'a'+b.posbak\",\"sizeBytes\":10}", "HASH-2", 2, null, null),
        // Autre poste : le .NET ne filtre pas le JET par poste.
        new(G(43), 3, "T02", "2025-03-20T10:00:00.500Z", "idem-3", "RECEIPT_VOIDED", "{\"ReceiptId\":\"x\"}", "HASH-3", 3, "HASH-2", G(901)),
        new(G(44), 4, "T01", "2025-03-31T22:00:00.000Z", "idem-4", "Z_CLOSURE", "{\"ClosureSequence\":3}", "HASH-4", 4, "HASH-3", Manager),
        new(G(45), 5, "T01", "2025-04-01T00:00:00.000Z", "idem-5", "PERIOD_CLOSURE", "{\"PeriodKey\":\"2025-03\"}", "HASH-5", 5, "HASH-4", Manager),
        new(G(46), 6, "T01", "2026-01-05T09:00:00.000Z", "idem-6", "PERIOD_CLOSURE", "{\"PeriodKey\":\"2025\"}", "HASH-6", 6, "HASH-5", Manager),
    ];

    private static void Seed(AppDbContext db)
    {
        foreach (var r in Receipts())
        {
            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = r.Id,
                TerminalId = r.TerminalId,
                ReceiptNumber = r.ReceiptNumber,
                OrderId = r.OrderId,
                SequenceNumber = r.SequenceNumber,
                TotalTtcAmount = Money.FromCents(r.TtcCents),
                TotalHtAmount = Money.FromCents(r.HtCents),
                TaxBreakdownJson = r.TaxBreakdownJson,
                HtBreakdownJson = r.HtBreakdownJson,
                PreviousSignatureHash = r.PreviousSignatureHash,
                SignatureHash = r.SignatureHash,
                CreatedAtUtc = Utc(r.CreatedAtUtc),
                VoidedReceiptId = r.VoidedReceiptId,
                Tenders = r.Tenders.Select(t => new PaymentTender
                {
                    Id = t.Id,
                    FiscalReceiptId = r.Id,
                    Method = (PaymentMethod)t.Method,
                    Amount = Money.FromCents(t.Amount),
                    Tendered = Money.FromCents(t.Tendered),
                    ChangeGiven = Money.FromCents(t.ChangeGiven),
                }).ToList(),
            });
        }

        foreach (var d in Dailies())
        {
            db.DailyFiscalClosures.Add(new DailyFiscalClosure
            {
                Id = d.Id,
                TerminalId = d.TerminalId,
                ClosureSequence = d.ClosureSequence,
                PeriodStartUtc = Utc(d.PeriodStartUtc),
                PeriodEndUtc = Utc(d.PeriodEndUtc),
                TotalSalesTtc = Money.FromCents(d.TtcCents),
                TotalSalesHt = Money.FromCents(d.HtCents),
                TaxesSummaryJson = d.TaxesSummaryJson,
                TenderTotalsJson = d.TenderTotalsJson,
                PerpetualGrandTotalCents = d.PerpetualGrandTotalCents,
                PreviousSignatureHash = d.PreviousSignatureHash,
                SignatureHash = d.SignatureHash,
                SealedByUserName = d.SealedByUserName,
                SealedByUserId = d.SealedByUserId,
            });
        }

        foreach (var p in Periods())
        {
            db.PeriodClosures.Add(new FiscalPeriodClosure
            {
                Id = p.Id,
                TerminalId = p.TerminalId,
                PeriodType = (FiscalPeriodType)p.PeriodType,
                PeriodKey = p.PeriodKey,
                ClosureSequence = p.ClosureSequence,
                PeriodStartUtc = Utc(p.PeriodStartUtc),
                PeriodEndUtc = Utc(p.PeriodEndUtc),
                TotalTtcAmount = Money.FromCents(p.TtcCents),
                TotalHtAmount = Money.FromCents(p.HtCents),
                TaxesSummaryJson = p.TaxesSummaryJson,
                TenderTotalsJson = p.TenderTotalsJson,
                PerpetualGrandTotalCents = p.PerpetualGrandTotalCents,
                DailyClosureCount = p.DailyClosureCount,
                PreviousSignatureHash = p.PreviousSignatureHash,
                SignatureHash = p.SignatureHash,
                SealedByUserId = p.SealedByUserId,
                SealedByUserName = p.SealedByUserName,
                CreatedAtUtc = Utc(p.CreatedAtUtc),
            });
        }

        foreach (var j in Journal())
        {
            db.JournalEntries.Add(new TransactionJournalEntry
            {
                Id = j.Id,
                LocalSequence = j.LocalSequence,
                TerminalId = j.TerminalId,
                OccurredAtUtc = Utc(j.OccurredAtUtc),
                IdempotencyKey = j.IdempotencyKey,
                EventType = j.EventType,
                PayloadJson = j.PayloadJson,
                EntryHash = j.EntryHash,
                ChainSequence = j.ChainSequence,
                PreviousHash = j.PreviousHash,
                OperatorId = j.OperatorId,
            });
        }
    }

    private static string EntryText(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    [Fact]
    public async Task GeneratesOrChecksTheArchiveFixture()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("ArchiveFixture_" + Guid.NewGuid().ToString("N")).Options;
        using var db = new AppDbContext(options);
        Seed(db);
        await db.SaveChangesAsync();
        var service = new FiscalArchiveService(db, new FiscalJournalService(db), archivesDirectory: _directory);

        var archives = new List<object>();
        foreach (var (closureId, counts) in Expected)
        {
            var dto = await service.CreateArchiveAsync(closureId, Manager);
            using var zip = ZipFile.OpenRead(Path.Combine(_directory, dto.FileName));
            zip.Entries.Select(e => e.FullName).Should().Equal(DataFiles.Append("manifest.json"));
            var texts = DataFiles.Select(name => EntryText(zip, name)).ToList();
            string manifest = EntryText(zip, "manifest.json");
            using var doc = JsonDocument.Parse(manifest);
            var c = doc.RootElement.GetProperty("counts");
            new[]
            {
                c.GetProperty("receipts").GetInt32(), c.GetProperty("closures").GetInt32(),
                c.GetProperty("periodClosures").GetInt32(), c.GetProperty("journalEntries").GetInt32(),
            }.Should().Equal(counts, "la sélection du .NET fait foi ; corriger les données du test, pas le service");

            archives.Add(new
            {
                periodClosureId = closureId,
                fileName = dto.FileName,
                files = new
                {
                    receipts = texts[0],
                    closures = texts[1],
                    periodClosures = texts[2],
                    journal = texts[3],
                    manifest = VolatileManifestField().Replace(manifest, "\"$1\": \"*\""),
                },
                sha256 = new { receipts = Sha(texts[0]), closures = Sha(texts[1]), periodClosures = Sha(texts[2]), journal = Sha(texts[3]) },
            });
        }

        string sha1 = FiscalHashing.ComputeSha256("archive mensuelle", lowerCase: true);
        string sha2 = FiscalHashing.ComputeSha256("archive annuelle", lowerCase: true);
        string sig1 = FiscalHashing.ComputeArchiveHash(Genesis, 1, "2025-03", sha1, Utc("2025-04-01T08:00:00.000Z"));
        string sig2 = FiscalHashing.ComputeArchiveHash(sig1, 2, "2025", sha2, Utc("2026-01-05T09:00:01.120Z"));
        var signatureVectors = new[]
        {
            new { previousHash = Genesis, periodType = 1, periodKey = "2025-03", fileSha256 = sha1, createdAtUtc = "2025-04-01T08:00:00.000Z", signatureHash = sig1 },
            new { previousHash = sig1, periodType = 2, periodKey = "2025", fileSha256 = sha2, createdAtUtc = "2026-01-05T09:00:01.120Z", signatureHash = sig2 },
        };

        string json = JsonSerializer.Serialize(
            new
            {
                receipts = Receipts(),
                dailyClosures = Dailies(),
                periodClosures = Periods(),
                journalEntries = Journal(),
                archives,
                signatureVectors,
            },
            JsonOptions);

        if (Environment.GetEnvironmentVariable("WRITE_ARCHIVE_FIXTURE") == "1")
        {
            File.WriteAllText(FixturePath(), json);
        }

        File.ReadAllText(FixturePath()).ReplaceLineEndings().Should().Be(json.ReplaceLineEndings(),
            "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_ARCHIVE_FIXTURE=1");
    }
}
