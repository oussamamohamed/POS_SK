using System.Globalization;
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
/// Fixture partagée avec l'iPad (mode autonome) : le Swift rejoue ces clôtures de période et doit obtenir les mêmes totaux et signatures.
/// Régénérer avec <c>WRITE_PERIOD_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PeriodClosureReferenceFixtureTests</c>
/// après toute modification de <c>ExecutePeriodClosureAsync</c> ou de <c>FiscalHashing.ComputePeriodClosureHash</c>.
/// </summary>
public class PeriodClosureReferenceFixtureTests
{
    private static readonly Guid Manager = Guid.Parse("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "period_closure_reference.json");
    }

    private static string Text(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private sealed record Daily(int Sequence, string Start, string End, long Ttc, long Ht, string Taxes, string Tenders, long Perpetual);

    private sealed record Step(string Type, string Key, string Now);

    private sealed record Case(string Name, int Month, int Day, List<Daily> Dailies, List<Step> Steps);

    private static List<Daily> YearOfDailies()
    {
        // Un Z par mois de 2025 (le 15), plus des Z aux bornes : minuit passé (28/02 23:30 UTC = 1er mars 00:30 à Paris)
        // et un Z le jour du changement d'heure d'octobre. Les taux sont volontairement dans le désordre pour figer l'ordre de fusion.
        var rows = new List<Daily>();
        long perpetual = 0;
        int seq = 0;
        void Add(string start, string end, long ttc, long ht, string taxes, string tenders)
        {
            perpetual += ttc;
            rows.Add(new Daily(++seq, start, end, ttc, ht, taxes, tenders, perpetual));
        }

        Add("2025-01-15T10:00:00.000Z", "2025-01-15T21:00:00.000Z", 10000, 9091, "{\"10\":909}", "{\"Cash\":10000}");
        Add("2025-02-15T10:00:00.000Z", "2025-02-15T21:00:00.000Z", 20000, 18265, "{\"5.5\":900,\"10\":835}", "{\"CreditCard\":20000}");
        Add("2025-02-28T23:30:00.000Z", "2025-03-01T08:00:00.000Z", 5000, 4546, "{\"10\":454}", "{\"Cash\":5000}");
        Add("2025-03-15T10:00:00.000Z", "2025-03-15T21:00:00.000Z", 30000, 27273, "{\"10\":2727}", "{\"Cash\":10000,\"CreditCard\":20000}");
        Add("2025-04-15T10:00:00.000Z", "2025-04-15T21:00:00.000Z", 40000, 35000, "{\"20\":5000}", "{\"CreditCard\":40000}");
        Add("2025-05-15T10:00:00.000Z", "2025-05-15T21:00:00.000Z", 1000, 910, "{\"10\":90}", "{\"Cash\":1000}");
        Add("2025-06-15T10:00:00.000Z", "2025-06-15T21:00:00.000Z", 2000, 1819, "{\"10\":181}", "{\"Cash\":2000}");
        Add("2025-07-15T10:00:00.000Z", "2025-07-15T21:00:00.000Z", 3000, 2728, "{\"10\":272}", "{\"Cash\":3000}");
        Add("2025-08-15T10:00:00.000Z", "2025-08-15T21:00:00.000Z", 4000, 3637, "{\"10\":363}", "{\"Cash\":4000}");
        Add("2025-09-15T10:00:00.000Z", "2025-09-15T21:00:00.000Z", 5000, 4546, "{\"10\":454}", "{\"Cash\":5000}");
        Add("2025-10-26T10:00:00.000Z", "2025-10-26T21:00:00.000Z", 6000, 5455, "{\"10\":545}", "{\"Cash\":6000}");
        Add("2025-11-15T10:00:00.000Z", "2025-11-15T21:00:00.000Z", 7000, 6364, "{\"10\":636}", "{\"Cash\":7000}");
        Add("2025-12-15T10:00:00.000Z", "2025-12-15T21:00:00.000Z", 8000, 7273, "{\"10\":727}", "{\"Cash\":8000}");
        return rows;
    }

    private static List<Case> Cases()
    {
        var months = Enumerable.Range(1, 12).Select(m => new Step("monthly", $"2025-{m:D2}", "2026-02-01T10:00:00.000Z")).ToList();
        var civilYear = new List<Step>
        {
            new("monthly", "2026-02", "2026-02-10T10:00:00.000Z"),   // period_not_ended (février 2026 finit le 1er mars)
            new("annual", "2025", "2026-02-01T10:00:00.000Z"),       // missing_monthly_closures (aucune mensuelle)
        };
        civilYear.AddRange(months);
        civilYear.Add(new("monthly", "2025-01", "2026-02-02T10:00:00.000Z")); // period_already_closed
        civilYear.Add(new("annual", "2025", "2026-02-03T10:00:00.000Z"));

        return
        [
            new("exercice civil 2025 : refus, 12 mensuelles puis l'annuelle", 1, 1, YearOfDailies(), civilYear),
            new("exercice du 15 avril : totaux de l'annuelle issus des Z journalières", 4, 15, YearOfDailies(),
                [new("annual", "2025", "2026-05-01T10:00:00.000Z")]),
            new("mois sans Z : zéro, perpétuel repris du dernier Z antérieur", 1, 1, YearOfDailies(),
                [new("monthly", "2025-04", "2026-02-01T10:00:00.000Z"), new("monthly", "2024-12", "2026-02-01T10:00:00.000Z")]),
        ];
    }

    private static async Task<object> RunAsync(Case c)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("PeriodFixture_" + Guid.NewGuid().ToString("N")).Options;
        using var db = new AppDbContext(options);
        db.RestaurantSettings.Add(new RestaurantSettings { ReceiptLanguage = "fr", KitchenTicketLanguage = "fr", FiscalYearStartMonth = c.Month, FiscalYearStartDay = c.Day });
        foreach (var d in c.Dailies)
        {
            db.DailyFiscalClosures.Add(new DailyFiscalClosure
            {
                TerminalId = "T01",
                ClosureSequence = d.Sequence,
                PeriodStartUtc = Utc(d.Start),
                PeriodEndUtc = Utc(d.End),
                TotalSalesTtc = Money.FromCents(d.Ttc),
                TotalSalesHt = Money.FromCents(d.Ht),
                TaxesSummaryJson = d.Taxes,
                TenderTotalsJson = d.Tenders,
                PerpetualGrandTotalCents = d.Perpetual,
            });
        }

        await db.SaveChangesAsync();
        var service = new NF525FiscalAuditService(db);
        var steps = new List<object>();
        foreach (var s in c.Steps)
        {
            var type = s.Type == "monthly" ? FiscalPeriodType.Monthly : FiscalPeriodType.Annual;
            try
            {
                var r = await service.ExecutePeriodClosureAsync("T01", type, s.Key, Manager, "Responsable", Utc(s.Now));
                steps.Add(new
                {
                    type = s.Type,
                    key = s.Key,
                    nowUtc = Text(Utc(s.Now)),
                    result = new
                    {
                        sequence = r.ClosureSequence,
                        periodStartUtc = Text(r.PeriodStartUtc),
                        periodEndUtc = Text(r.PeriodEndUtc),
                        ttcCents = r.TotalTtcCents,
                        htCents = r.TotalHtCents,
                        taxesJson = r.TaxesSummaryJson,
                        tendersJson = r.TenderTotalsJson,
                        perpetualCents = r.PerpetualGrandTotalCents,
                        dailyClosureCount = r.DailyClosureCount,
                        previousSignatureHash = r.PreviousSignatureHash,
                        signatureHash = r.SignatureHash,
                    },
                });
            }
            catch (PeriodClosureException ex)
            {
                steps.Add(new { type = s.Type, key = s.Key, nowUtc = Text(Utc(s.Now)), error = ex.Code, days = ex.Days, months = ex.Months });
            }
        }

        return new
        {
            name = c.Name,
            fiscalYearStartMonth = c.Month,
            fiscalYearStartDay = c.Day,
            dailyClosures = c.Dailies.Select(d => new
            {
                sequence = d.Sequence,
                periodStartUtc = d.Start,
                periodEndUtc = d.End,
                ttcCents = d.Ttc,
                htCents = d.Ht,
                taxesJson = d.Taxes,
                tendersJson = d.Tenders,
                perpetualCents = d.Perpetual,
            }),
            steps,
        };
    }

    [Fact]
    public async Task GeneratesOrChecksThePeriodClosureFixture()
    {
        Environment.SetEnvironmentVariable("TZ", "Europe/Paris");
        TimeZoneInfo.ClearCachedData();
        TimeZoneInfo.Local.GetUtcOffset(new DateTime(2025, 7, 1)).Should().Be(TimeSpan.FromHours(2), "le générateur doit tourner en heure de Paris");

        var cases = new List<object>();
        foreach (var c in Cases())
        {
            cases.Add(await RunAsync(c));
        }

        string json = JsonSerializer.Serialize(
            new { timeZone = "Europe/Paris", cases },
            JsonOptions);

        if (Environment.GetEnvironmentVariable("WRITE_PERIOD_FIXTURE") == "1")
        {
            File.WriteAllText(FixturePath(), json);
        }

        File.ReadAllText(FixturePath()).ReplaceLineEndings().Should().Be(json.ReplaceLineEndings(),
            "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_PERIOD_FIXTURE=1");
    }
}
