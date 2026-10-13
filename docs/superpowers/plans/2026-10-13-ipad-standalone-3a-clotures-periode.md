# iPad autonome 3a : clôtures de période (mensuelle et annuelle) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le mode autonome clôture les périodes mensuelles et annuelles comme le .NET (`ExecutePeriodClosureAsync`), avec des signatures identiques.

**Architecture:** Une table `PeriodClosures` (migration V8, même nom et mêmes colonnes que `AppDbContext`) et un `LocalFiscalRepository+PeriodClosures` qui porte l'algorithme du .NET. `LocalPosAPI` expose `periodClosures` et `executePeriodClosure` (déjà déclarées dans `PosAPI`, aujourd'hui « non supportées »). La preuve est une fixture générée par le .NET (`period_closure_reference.json`) et rejouée en Swift.

**Tech Stack:** Swift 6 / Swift Testing / SQLite (PosKit) ; .NET 9 / xUnit / EF InMemory pour la fixture.

**Spec:** `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` (lot 3a).

## Global Constraints

- Montants en centimes entiers (`Int`), jamais de `Double`.
- Table nommée **`PeriodClosures`** (nom du `DbSet`, pas `FiscalPeriodClosures`), colonnes de `src/RestaurantPos.Api/Program.cs` lignes 306-326.
- Migration nouvelle (`schemaV8`), jamais de modification d'un script publié ; triggers `NoUpdate` / `NoDelete`.
- Poste unique `T01` (`LocalPosAPI.standaloneTerminalId`), pas de `POS_MAIN_TERM`.
- Le fuseau vient du `calendar` injecté dans `LocalPosAPI` ; aucun test ne dépend du fuseau de la machine.
- Dates fiscales écrites par `FiscalHashing.storedText`, jamais `SQLDate.format`.
- JSON signés au format `System.Text.Json` : `FiscalJSON.object`, clés dans l'ordre d'insertion du .NET.
- Type de période signé en entier : `Monthly = 1`, `Annual = 2`.
- Messages d'erreur en français, statut 409 pour les refus métier, 400 pour une clé invalide ; action réservée aux responsables (`requireManager`).
- Tests : `swift test` dans `ios/Packages/PosKit` ; .NET : `dotnet test tests/RestaurantPos.Infrastructure.Tests`. `Directory.Build.props` traite les avertissements .NET en erreurs.

## Review Focus

- Un Z lancé après minuit appartient au mois où sa période **commence** : Z du 28/02 23:30 UTC (= 1er mars 00:30 à Paris) compté en mars (Task 1 fixture, Task 4).
- Changement d'heure (30 mars et 26 octobre 2025) : les bornes UTC d'un mois sont calculées avec le décalage local de chaque borne (Task 1 fixture, Task 4).
- Ordre des clés des JSON fusionnés : celui d'insertion du .NET, pas l'ordre trié (fixture avec taux dans le désordre, Task 4).
- Mois sans aucun Z : clôture à zéro, perpétuel repris du dernier Z antérieur (Task 4).
- Annuelle avec un exercice qui commence le 15 avril : totaux issus des Z journalières, pas des mensuelles (Task 4).
- Reçu non couvert par un Z dans la période : refus avec la liste des jours au fuseau local (Task 5, vrais reçus).
- Clôture refusée = aucune écriture (ni ligne ni entrée JET) (Task 5).
- Un serveur (non responsable) ne peut pas clôturer (Task 5).

---

### Task 1: Fixture .NET `period_closure_reference.json`

**Files:**
- Create: `tests/RestaurantPos.Infrastructure.Tests/PeriodClosureReferenceFixtureTests.cs`
- Create (généré): `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/period_closure_reference.json`

**Interfaces:**
- Produces: le JSON suivant, relu par Task 4.
  ```
  { "timeZone": "Europe/Paris",
    "cases": [ { "name": String, "fiscalYearStartMonth": Int, "fiscalYearStartDay": Int,
                 "dailyClosures": [ { "sequence": Int, "periodStartUtc": String, "periodEndUtc": String,
                                      "ttcCents": Int, "htCents": Int, "taxesJson": String, "tendersJson": String,
                                      "perpetualCents": Int } ],
                 "steps": [ { "type": "monthly"|"annual", "key": String, "nowUtc": String,
                              "error": String?, "days": [String]?, "months": [String]?,
                              "result": { "sequence": Int, "periodStartUtc": String, "periodEndUtc": String,
                                          "ttcCents": Int, "htCents": Int, "taxesJson": String, "tendersJson": String,
                                          "perpetualCents": Int, "dailyClosureCount": Int,
                                          "previousSignatureHash": String, "signatureHash": String }? } ] } ] }
  ```
  Dates au format `yyyy-MM-ddTHH:mm:ss.fffZ` (UTC), celui de `FiscalHashing.storedText`. `error` ∈ `period_not_ended`, `period_already_closed`, `missing_monthly_closures`.

- [ ] **Step 1: Vérifier les noms réels du service .NET**

Run: `grep -n "record PeriodClosureDto" -A20 src/RestaurantPos.Application -r` et `grep -rn "class PeriodClosureException" -A15 src/RestaurantPos.Infrastructure`
Expected: propriétés `ClosureSequence`, `PeriodStartUtc`, `PeriodEndUtc`, `TotalTtcCents`, `TotalHtCents`, `TaxesSummaryJson`, `TenderTotalsJson`, `PerpetualGrandTotalCents`, `DailyClosureCount`, `PreviousSignatureHash`, `SignatureHash` ; exception avec `Code`, `Days`, `Months`. Si un nom diffère, adapter le test à l'étape suivante, jamais le service.

- [ ] **Step 2: Écrire le test générateur**

Ce test s'exécute sous `TZ=Europe/Paris` (variable posée dans le test, puis `TimeZoneInfo.ClearCachedData()`), construit trois cas dans une base EF InMemory, appelle le vrai `NF525FiscalAuditService.ExecutePeriodClosureAsync`, écrit le JSON si `WRITE_PERIOD_FIXTURE=1`, puis compare au fichier.

```csharp
using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
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
        db.RestaurantSettings.Add(new RestaurantSettings { FiscalYearStartMonth = c.Month, FiscalYearStartDay = c.Day });
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
                    type = s.Type, key = s.Key, nowUtc = Text(Utc(s.Now)),
                    result = new
                    {
                        sequence = r.ClosureSequence, periodStartUtc = Text(r.PeriodStartUtc), periodEndUtc = Text(r.PeriodEndUtc),
                        ttcCents = r.TotalTtcCents, htCents = r.TotalHtCents, taxesJson = r.TaxesSummaryJson, tendersJson = r.TenderTotalsJson,
                        perpetualCents = r.PerpetualGrandTotalCents, dailyClosureCount = r.DailyClosureCount,
                        previousSignatureHash = r.PreviousSignatureHash, signatureHash = r.SignatureHash,
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
            name = c.Name, fiscalYearStartMonth = c.Month, fiscalYearStartDay = c.Day,
            dailyClosures = c.Dailies.Select(d => new
            {
                sequence = d.Sequence, periodStartUtc = d.Start, periodEndUtc = d.End, ttcCents = d.Ttc, htCents = d.Ht,
                taxesJson = d.Taxes, tendersJson = d.Tenders, perpetualCents = d.Perpetual,
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
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (Environment.GetEnvironmentVariable("WRITE_PERIOD_FIXTURE") == "1")
        {
            File.WriteAllText(FixturePath(), json);
        }

        File.ReadAllText(FixturePath()).ReplaceLineEndings().Should().Be(json.ReplaceLineEndings(),
            "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_PERIOD_FIXTURE=1");
    }
}
```

- [ ] **Step 3: Générer la fixture**

Run: `WRITE_PERIOD_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PeriodClosureReferenceFixtureTests`
Expected: PASS ; le fichier `period_closure_reference.json` existe. Contrôle à l'œil :
- cas 1 : dans l'ordre `period_not_ended`, `missing_monthly_closures` (12 mois listés), 12 résultats (mars compte le Z du 28/02 23:30 UTC), `period_already_closed`, puis l'annuelle ;
- cas 2 : l'annuelle du 15 avril 2025 → 15 avril 2026 est calculée depuis les Z journalières ;
- cas 3 : `2025-04` contient le Z du 15 avril ; `2024-12` est à zéro avec `perpetualCents` 0.
Si le .NET refuse un cas (exception autre que `PeriodClosureException`), corriger les données du test, pas le service.

- [ ] **Step 4: Relancer sans régénération**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PeriodClosureReferenceFixtureTests`
Expected: PASS (la fixture est stable).

- [ ] **Step 5: Commit**

```bash
git add tests/RestaurantPos.Infrastructure.Tests/PeriodClosureReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/period_closure_reference.json
git commit -m "test(fiscal): fixture .NET des clôtures de période pour l'iPad autonome"
```

---

### Task 2: Signature de clôture de période en Swift

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift` (après `jetHash`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalHashingTests.swift`

**Interfaces:**
- Produces: `FiscalHashing.periodClosureHash(previousHash: String, terminalId: String, periodType: Int, periodKey: String, totalTtcCents: Int, totalHtCents: Int, taxesJson: String, tendersJson: String, perpetualGrandTotalCents: Int, periodEnd: Date) -> String` (hexadécimal minuscule).

- [ ] **Step 1: Écrire le test qui échoue**

Dans la suite existante de `FiscalHashingTests.swift` :

```swift
    @Test func periodClosureHashJoinsTheFieldsLikeTheDotNet() {
        let end = SQLDate.parse("2025-01-31T23:00:00.000Z")!
        let hash = FiscalHashing.periodClosureHash(
            previousHash: FiscalHashing.genesisHash, terminalId: "T01", periodType: 1, periodKey: "2025-01",
            totalTtcCents: 10000, totalHtCents: 9091, taxesJson: "{\"10\":909}", tendersJson: "{\"Cash\":10000}",
            perpetualGrandTotalCents: 10000, periodEnd: end
        )
        let raw = "\(FiscalHashing.genesisHash)|T01|1|2025-01|10000|9091|{\"10\":909}|{\"Cash\":10000}|10000|2025-01-31T23:00:00.0000000+00:00"
        #expect(hash == FiscalHashing.sha256(raw, lowercase: true))
        #expect(hash == hash.lowercased() && hash.count == 64)
    }
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter FiscalHashingTests`
Expected: erreur de compilation « no member periodClosureHash ».

- [ ] **Step 3: Implémenter**

Dans `FiscalHashing.swift`, après `jetHash` :

```swift
    /// `ComputePeriodClosureHash` : hexadécimal minuscule, `periodType` entier (`Monthly = 1`, `Annual = 2`), fin de période au format « O ».
    static func periodClosureHash(
        previousHash: String, terminalId: String, periodType: Int, periodKey: String, totalTtcCents: Int, totalHtCents: Int,
        taxesJson: String, tendersJson: String, perpetualGrandTotalCents: Int, periodEnd: Date
    ) -> String {
        sha256(
            "\(previousHash)|\(terminalId)|\(periodType)|\(periodKey)|\(totalTtcCents)|\(totalHtCents)|\(taxesJson)|\(tendersJson)|\(perpetualGrandTotalCents)|\(timestampText(periodEnd))",
            lowercase: true
        )
    }
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter FiscalHashingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): signature des clôtures de période (miroir de ComputePeriodClosureHash)"
```

---

### Task 3: Migration V8 `PeriodClosures`

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV8.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift:5`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalSchemaTests.swift`

**Interfaces:**
- Produces: table `PeriodClosures` (colonnes de `Program.cs`), `LocalMigrator.schemaV8`, `LocalMigrator.steps.count == 8`.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalFiscalSchemaTests` :

```swift
    private static let insertPeriodClosure = """
    INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
        TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
        SealedByUserName, CreatedAtUtc)
    VALUES ('P1', 'T01', 1, '2025-01', 1, '2024-12-31T23:00:00.000Z', '2025-01-31T23:00:00.000Z', 1000, 909, '{}', '{}', 1000, 1, 'GENESIS', 'SIG',
        'U1', 'Chef', '2026-02-01T10:00:00.000Z')
    """

    @Test func migrationV8CreatesThePeriodClosuresTable() throws {
        let db = try makeDB()
        #expect(LocalMigrator.steps.count >= 8)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE type = 'table' AND name = 'PeriodClosures'").count == 1)
    }

    @Test func periodClosuresAreAppendOnly() throws {
        let db = try makeDB()
        try db.run(Self.insertPeriodClosure)
        #expect(throws: (any Error).self) { try db.run("UPDATE PeriodClosures SET TotalTtcAmount = 1 WHERE Id = 'P1'") }
        #expect(throws: (any Error).self) { try db.run("DELETE FROM PeriodClosures WHERE Id = 'P1'") }
    }

    @Test func aPeriodCanOnlyBeClosedOncePerTerminalAndType() throws {
        let db = try makeDB()
        try db.run(Self.insertPeriodClosure)
        #expect(throws: (any Error).self) {
            try db.run(Self.insertPeriodClosure.replacingOccurrences(of: "'P1'", with: "'P2'").replacingOccurrences(of: ", 1, '2024-12-31", with: ", 2, '2024-12-31"))
        }
    }

    @Test func upgradingAV7DatabaseAddsThePeriodClosuresTable() throws {
        let older = try SQLiteDatabase(path: ":memory:")
        for script in LocalMigrator.steps.prefix(7) { try older.exec(script) }
        try older.exec("PRAGMA user_version = 7")
        try LocalMigrator.migrate(older)
        #expect(try older.userVersion() == LocalMigrator.steps.count)
        #expect(try older.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'PeriodClosures'").count == 1)
    }
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFiscalSchemaTests`
Expected: FAIL (`no such table: PeriodClosures`).

- [ ] **Step 3: Créer la migration**

`LocalSchemaV8.swift` :

```swift
import Foundation

extension LocalMigrator {
    /// Clôtures de période (mensuelle, annuelle) NF525 : même table et mêmes colonnes que `PeriodClosures` dans `AppDbContext` (nom du `DbSet`).
    static let schemaV8 = """
    CREATE TABLE PeriodClosures (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        PeriodType INTEGER NOT NULL,
        PeriodKey TEXT NOT NULL,
        ClosureSequence INTEGER NOT NULL,
        PeriodStartUtc TEXT NOT NULL,
        PeriodEndUtc TEXT NOT NULL,
        TotalTtcAmount INTEGER NOT NULL,
        TotalHtAmount INTEGER NOT NULL,
        TaxesSummaryJson TEXT NOT NULL,
        TenderTotalsJson TEXT NOT NULL,
        PerpetualGrandTotalCents INTEGER NOT NULL,
        DailyClosureCount INTEGER NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        SealedByUserId TEXT NOT NULL,
        SealedByUserName TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_PeriodClosures_Terminal_Type_Key ON PeriodClosures (TerminalId, PeriodType, PeriodKey);
    CREATE UNIQUE INDEX IX_PeriodClosures_Terminal_Type_Sequence ON PeriodClosures (TerminalId, PeriodType, ClosureSequence);

    CREATE TRIGGER TR_PeriodClosures_NoUpdate BEFORE UPDATE ON PeriodClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    CREATE TRIGGER TR_PeriodClosures_NoDelete BEFORE DELETE ON PeriodClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    """
}
```

Dans `LocalMigrator.swift` ligne 5, ajouter `schemaV8` à la fin du tableau `steps`.

- [ ] **Step 4: Vérifier**

Run: `swift test`
Expected: PASS (tous les tests, y compris ceux qui comparent `steps.count`).

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): migration V8, table PeriodClosures immuable"
```

---

### Task 4: `LocalFiscalRepository+PeriodClosures` rejoué sur la fixture

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+PeriodClosures.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift` (ajout `periodClosure`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift` (ajout `FiscalJSON.parseIntObject`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift` (`closureRecord` passe de `private` à interne)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPeriodClosureReferenceTests.swift`

**Interfaces:**
- Consumes: `FiscalHashing.periodClosureHash` (Task 2), table `PeriodClosures` (Task 3), `LocalFiscalRepository.appendJournal`, `FiscalHashing.storedText`, `FiscalJSON.object`.
- Produces:
  ```swift
  struct PeriodClosureRecord: Equatable { id: UUID; terminalId: String; periodType: FiscalPeriodType; periodKey: String; sequence: Int; periodStartUtc: Date; periodEndUtc: Date; totalTtcCents: Int; totalHtCents: Int; taxesSummaryJson: String; tenderTotalsJson: String; perpetualGrandTotalCents: Int; dailyClosureCount: Int; previousSignatureHash: String; signatureHash: String; sealedByUserId: UUID; sealedByUserName: String; createdAtUtc: Date }
  enum PeriodClosureError: Error, Equatable { case invalidKey, notEnded, alreadyClosed, missingDailyClosures([String]), missingMonthlyClosures([String]) }
  extension LocalFiscalRepository {
      func executePeriodClosure(terminalId: String, type: FiscalPeriodType, key: String, managerId: UUID, managerName: String, fiscalYearStartMonth: Int, fiscalYearStartDay: Int, calendar: Calendar, now: Date) throws -> PeriodClosureRecord
      func periodClosures(terminalId: String, type: FiscalPeriodType?) throws -> [PeriodClosureRecord]
  }
  ```
  `FiscalJSON.parseIntObject(_ json: String) -> [(String, Int)]?`, `LocalJournalEvents.periodClosure == "PERIOD_CLOSURE"`.

- [ ] **Step 1: Écrire le test de rejeu qui échoue**

`LocalPeriodClosureReferenceTests.swift` : pour chaque cas de la fixture, base `:memory:` migrée, Z insérés en SQL brut, puis chaque pas rejoué avec un calendrier `Europe/Paris`.

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Clôtures de période : parité avec la fixture .NET")
struct LocalPeriodClosureReferenceTests {
    private struct Reference: Decodable {
        var timeZone: String
        var cases: [Case]
    }

    private struct Case: Decodable {
        var name: String
        var fiscalYearStartMonth: Int
        var fiscalYearStartDay: Int
        var dailyClosures: [Daily]
        var steps: [Step]
    }

    private struct Daily: Decodable {
        var sequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesJson: String
        var tendersJson: String
        var perpetualCents: Int
    }

    private struct Step: Decodable {
        var type: String
        var key: String
        var nowUtc: String
        var error: String?
        var days: [String]?
        var months: [String]?
        var result: Result?
    }

    private struct Result: Decodable {
        var sequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesJson: String
        var tendersJson: String
        var perpetualCents: Int
        var dailyClosureCount: Int
        var previousSignatureHash: String
        var signatureHash: String
    }

    private static let manager = UUID()

    private static func reference() throws -> Reference {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("period_closure_reference"))
    }

    private static func insertDaily(_ db: SQLiteDatabase, _ d: Daily) throws {
        try db.run(
            """
            INSERT INTO DailyFiscalClosures (Id, TerminalId, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalSalesTtc, TotalSalesHt,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, PreviousSignatureHash, SignatureHash, SealedByUserName, SealedByUserId, CreatedAtUtc)
            VALUES (?, 'T01', ?, ?, ?, ?, ?, ?, ?, ?, '', '', '', ?, ?)
            """,
            [.uuid(UUID()), .integer(d.sequence), .text(d.periodStartUtc), .text(d.periodEndUtc), .integer(d.ttcCents), .integer(d.htCents),
             .text(d.taxesJson), .text(d.tendersJson), .integer(d.perpetualCents), .uuid(manager), .text(d.periodEndUtc)]
        )
    }

    @Test func everyCaseOfTheDotNetFixtureReplaysIdentically() throws {
        let reference = try Self.reference()
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = try #require(TimeZone(identifier: reference.timeZone))

        for c in reference.cases {
            let db = try SQLiteDatabase(path: ":memory:")
            try LocalMigrator.migrate(db)
            for d in c.dailyClosures { try Self.insertDaily(db, d) }
            let repo = LocalFiscalRepository(db: db)

            for step in c.steps {
                let context = Comment(rawValue: "\(c.name) — \(step.type) \(step.key)")
                let type: FiscalPeriodType = step.type == "monthly" ? .monthly : .annual
                let now = try #require(SQLDate.parse(step.nowUtc))
                func run() throws -> PeriodClosureRecord {
                    try repo.executePeriodClosure(
                        terminalId: "T01", type: type, key: step.key, managerId: Self.manager, managerName: "Responsable",
                        fiscalYearStartMonth: c.fiscalYearStartMonth, fiscalYearStartDay: c.fiscalYearStartDay, calendar: calendar, now: now
                    )
                }

                if let error = step.error {
                    let expected: PeriodClosureError = switch error {
                    case "period_not_ended": .notEnded
                    case "period_already_closed": .alreadyClosed
                    case "missing_monthly_closures": .missingMonthlyClosures(step.months ?? [])
                    case "missing_daily_closures": .missingDailyClosures(step.days ?? [])
                    default: .invalidKey
                    }
                    #expect(throws: expected) { try run() }
                    continue
                }

                let expected = try #require(step.result, context)
                let actual = try run()
                #expect(actual.sequence == expected.sequence, context)
                #expect(FiscalHashing.storedText(actual.periodStartUtc) == expected.periodStartUtc, context)
                #expect(FiscalHashing.storedText(actual.periodEndUtc) == expected.periodEndUtc, context)
                #expect(actual.totalTtcCents == expected.ttcCents, context)
                #expect(actual.totalHtCents == expected.htCents, context)
                #expect(actual.taxesSummaryJson == expected.taxesJson, context)
                #expect(actual.tenderTotalsJson == expected.tendersJson, context)
                #expect(actual.perpetualGrandTotalCents == expected.perpetualCents, context)
                #expect(actual.dailyClosureCount == expected.dailyClosureCount, context)
                #expect(actual.previousSignatureHash == expected.previousSignatureHash, context)
                #expect(actual.signatureHash == expected.signatureHash, context)
            }
        }
    }

    @Test func parseIntObjectKeepsTheKeyOrder() {
        #expect(FiscalJSON.parseIntObject("{\"5.5\":900,\"10\":835}")?.map(\.0) == ["5.5", "10"])
        #expect(FiscalJSON.parseIntObject("{}")?.isEmpty == true)
        #expect(FiscalJSON.parseIntObject("pas du json") == nil)
    }

    @Test func aKeyThatIsNotADateIsRefused() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        let repo = LocalFiscalRepository(db: db)
        let calendar = Calendar(identifier: .gregorian)
        for key in ["2025-13", "2025-1", "abcd-01", ""] {
            #expect(throws: PeriodClosureError.invalidKey) {
                try repo.executePeriodClosure(terminalId: "T01", type: .monthly, key: key, managerId: UUID(), managerName: "M",
                                              fiscalYearStartMonth: 1, fiscalYearStartDay: 1, calendar: calendar, now: Date())
            }
        }
        #expect(throws: PeriodClosureError.invalidKey) {
            try repo.executePeriodClosure(terminalId: "T01", type: .annual, key: "2025", managerId: UUID(), managerName: "M",
                                          fiscalYearStartMonth: 2, fiscalYearStartDay: 30, calendar: calendar, now: Date())
        }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalPeriodClosureReferenceTests`
Expected: erreur de compilation (types absents).

- [ ] **Step 3: Ajouter l'analyse JSON ordonnée, l'événement JET et rendre `closureRecord` interne**

Dans `LocalJournalEvents.swift` : `static let periodClosure = "PERIOD_CLOSURE"`.

Dans `LocalFiscalRepository.swift`, retirer `private` devant `func closureRecord(_ row: SQLRow)`.

Dans `FiscalHashing.swift`, dans `enum FiscalJSON` :

```swift
    /// Lit un objet `{"clé":entier,…}` en conservant l'ordre des clés (le .NET fusionne dans l'ordre d'insertion). `nil` si le texte n'est pas de cette forme.
    /// ponytail: clés sans guillemet, virgule ni deux-points échappés (taux de TVA et noms de moyens de paiement) ; passer à un vrai parseur si les clés deviennent libres.
    static func parseIntObject(_ json: String) -> [(String, Int)]? {
        let text = json.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.hasPrefix("{"), text.hasSuffix("}") else { return nil }
        let inner = text.dropFirst().dropLast().trimmingCharacters(in: .whitespacesAndNewlines)
        if inner.isEmpty { return [] }
        var entries: [(String, Int)] = []
        for part in inner.split(separator: ",") {
            guard let colon = part.lastIndex(of: ":") else { return nil }
            let key = part[..<colon].trimmingCharacters(in: .whitespaces)
            let value = part[part.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            guard key.count >= 2, key.hasPrefix("\""), key.hasSuffix("\""), let number = Int(value) else { return nil }
            entries.append((String(key.dropFirst().dropLast()), number))
        }
        return entries
    }
```

- [ ] **Step 4: Écrire le dépôt**

`LocalFiscalRepository+PeriodClosures.swift` :

```swift
import Foundation

struct PeriodClosureRecord: Equatable {
    let id: UUID
    let terminalId: String
    let periodType: FiscalPeriodType
    let periodKey: String
    let sequence: Int
    let periodStartUtc: Date
    let periodEndUtc: Date
    let totalTtcCents: Int
    let totalHtCents: Int
    let taxesSummaryJson: String
    let tenderTotalsJson: String
    let perpetualGrandTotalCents: Int
    let dailyClosureCount: Int
    let previousSignatureHash: String
    let signatureHash: String
    let sealedByUserId: UUID
    let sealedByUserName: String
    let createdAtUtc: Date
}

/// Refus métier d'une clôture de période : mêmes causes que `PeriodClosureException` du .NET.
enum PeriodClosureError: Error, Equatable {
    case invalidKey
    case notEnded
    case alreadyClosed
    case missingDailyClosures([String])
    case missingMonthlyClosures([String])
}

extension FiscalPeriodType {
    /// Valeur signée et stockée (`FiscalPeriodType` du .NET : `Monthly = 1`, `Annual = 2`).
    var storedValue: Int { self == .monthly ? 1 : 2 }
}

extension LocalFiscalRepository {
    /// Clôture une période (`ExecutePeriodClosureAsync`). Mensuelle : somme des Z journalières dont la période commence dans le mois.
    /// Annuelle : somme des 12 mensuelles si l'exercice commence le 1er, sinon des Z journalières. Aucune transaction ouverte ici.
    func executePeriodClosure(
        terminalId: String, type: FiscalPeriodType, key: String, managerId: UUID, managerName: String,
        fiscalYearStartMonth: Int, fiscalYearStartDay: Int, calendar: Calendar, now: Date
    ) throws -> PeriodClosureRecord {
        let start = try Self.periodStart(type: type, key: key, month: fiscalYearStartMonth, day: fiscalYearStartDay, calendar: calendar)
        guard let rawEnd = calendar.date(byAdding: type == .monthly ? .month : .year, value: 1, to: start) else { throw PeriodClosureError.invalidKey }
        let end = FiscalHashing.roundedToMilliseconds(rawEnd)
        let timestampNow = FiscalHashing.roundedToMilliseconds(now)
        guard timestampNow >= end else { throw PeriodClosureError.notEnded }
        guard try db.query(
            "SELECT 1 AS x FROM PeriodClosures WHERE TerminalId = ? AND PeriodType = ? AND PeriodKey = ?",
            [.text(terminalId), .integer(type.storedValue), .text(key)]
        ).isEmpty else { throw PeriodClosureError.alreadyClosed }

        var totalTtc = 0, totalHt = 0, dailyCount = 0, perpetual = 0
        var taxes = OrderedTotals(), tenders = OrderedTotals()

        if type == .monthly || fiscalYearStartDay != 1 {
            let all = try dailyClosures(terminalId: terminalId)
            let inPeriod = all.filter { $0.periodStartUtc >= start && $0.periodStartUtc < end }
            try ensureEveryReceiptIsCovered(terminalId: terminalId, start: start, end: end, closures: inPeriod, calendar: calendar)
            totalTtc = inPeriod.reduce(0) { $0 + $1.totalSalesTtcCents }
            totalHt = inPeriod.reduce(0) { $0 + $1.totalSalesHtCents }
            dailyCount = inPeriod.count
            if let last = inPeriod.last {
                perpetual = last.perpetualGrandTotalCents
            } else {
                perpetual = all.filter { $0.periodEndUtc < start }.max { $0.periodEndUtc < $1.periodEndUtc }?.perpetualGrandTotalCents ?? 0
            }
            for z in inPeriod {
                taxes.merge(z.taxesSummaryJson)
                tenders.merge(z.tenderTotalsJson)
            }
        } else {
            let expected = (0..<12).compactMap { calendar.date(byAdding: .month, value: $0, to: start) }.map { Self.monthKey($0, calendar) }
            let monthly = try periodClosures(terminalId: terminalId, type: .monthly)
            let closed = Set(monthly.map(\.periodKey))
            let missing = expected.filter { !closed.contains($0) }
            guard missing.isEmpty else { throw PeriodClosureError.missingMonthlyClosures(missing) }
            let year = monthly.filter { expected.contains($0.periodKey) }.sorted { $0.periodKey < $1.periodKey }
            totalTtc = year.reduce(0) { $0 + $1.totalTtcCents }
            totalHt = year.reduce(0) { $0 + $1.totalHtCents }
            dailyCount = year.reduce(0) { $0 + $1.dailyClosureCount }
            perpetual = year.last?.perpetualGrandTotalCents ?? 0
            for m in year {
                taxes.merge(m.taxesSummaryJson)
                tenders.merge(m.tenderTotalsJson)
            }
        }

        let last = try periodClosures(terminalId: terminalId, type: type).last
        let sequence = (last?.sequence ?? 0) + 1
        let previousHash = last?.signatureHash ?? FiscalHashing.genesisHash
        let taxesJson = taxes.json, tendersJson = tenders.json
        let signature = FiscalHashing.periodClosureHash(
            previousHash: previousHash, terminalId: terminalId, periodType: type.storedValue, periodKey: key,
            totalTtcCents: totalTtc, totalHtCents: totalHt, taxesJson: taxesJson, tendersJson: tendersJson,
            perpetualGrandTotalCents: perpetual, periodEnd: end
        )
        let id = UUID()
        try db.run(
            """
            INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                SealedByUserName, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                .uuid(id), .text(terminalId), .integer(type.storedValue), .text(key), .integer(sequence),
                .text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end)), .integer(totalTtc), .integer(totalHt),
                .text(taxesJson), .text(tendersJson), .integer(perpetual), .integer(dailyCount), .text(previousHash), .text(signature),
                .uuid(managerId), .text(managerName), .text(FiscalHashing.storedText(timestampNow)),
            ]
        )
        try appendJournal(
            eventType: LocalJournalEvents.periodClosure,
            payloadJson: FiscalJSON.object([
                ("PeriodClosureId", FiscalJSON.guid(id)),
                ("TerminalId", FiscalJSON.string(terminalId)),
                ("PeriodType", FiscalJSON.string(type.rawValue)),
                ("PeriodKey", FiscalJSON.string(key)),
                ("ClosureSequence", "\(sequence)"),
                ("TotalTtcCents", "\(totalTtc)"),
                ("PerpetualGrandTotalCents", "\(perpetual)"),
                ("SignatureHash", FiscalJSON.string(signature)),
            ]),
            terminalId: terminalId, operatorId: managerId, now: now
        )
        return PeriodClosureRecord(
            id: id, terminalId: terminalId, periodType: type, periodKey: key, sequence: sequence, periodStartUtc: start, periodEndUtc: end,
            totalTtcCents: totalTtc, totalHtCents: totalHt, taxesSummaryJson: taxesJson, tenderTotalsJson: tendersJson, perpetualGrandTotalCents: perpetual,
            dailyClosureCount: dailyCount, previousSignatureHash: previousHash, signatureHash: signature, sealedByUserId: managerId,
            sealedByUserName: managerName, createdAtUtc: timestampNow
        )
    }

    /// Clôtures du poste, de la plus ancienne à la plus récente (par type puis séquence).
    func periodClosures(terminalId: String, type: FiscalPeriodType?) throws -> [PeriodClosureRecord] {
        var sql = "SELECT * FROM PeriodClosures WHERE TerminalId = ?"
        var params: [SQLValue] = [.text(terminalId)]
        if let type {
            sql += " AND PeriodType = ?"
            params.append(.integer(type.storedValue))
        }
        sql += " ORDER BY PeriodType, ClosureSequence"
        return try db.query(sql, params).compactMap(periodClosureRecord)
    }

    func dailyClosures(terminalId: String) throws -> [DailyFiscalClosureRecord] {
        try db.query("SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence", [.text(terminalId)]).compactMap(closureRecord)
    }

    // MARK: Bornes de période

    /// Début de période à minuit local. Refuse une clé mal formée et un jour d'exercice inexistant (30 février).
    private static func periodStart(type: FiscalPeriodType, key: String, month: Int, day: Int, calendar: Calendar) throws -> Date {
        let components: DateComponents
        switch type {
        case .monthly:
            let parts = key.split(separator: "-", omittingEmptySubsequences: false)
            guard parts.count == 2, parts[0].count == 4, parts[1].count == 2, let y = Int(parts[0]), let m = Int(parts[1]), (1...12).contains(m)
            else { throw PeriodClosureError.invalidKey }
            components = DateComponents(year: y, month: m, day: 1)
        case .annual:
            guard key.count == 4, let y = Int(key) else { throw PeriodClosureError.invalidKey }
            components = DateComponents(year: y, month: month, day: day)
        }
        guard let start = calendar.date(from: components),
              calendar.component(.day, from: start) == components.day, calendar.component(.month, from: start) == components.month
        else { throw PeriodClosureError.invalidKey }
        return FiscalHashing.roundedToMilliseconds(start)
    }

    private static func monthKey(_ date: Date, _ calendar: Calendar) -> String {
        let c = calendar.dateComponents([.year, .month], from: date)
        return String(format: "%04d-%02d", c.year ?? 0, c.month ?? 0)
    }

    /// Un reçu de la période doit être couvert par une Z journalière dont la fin est postérieure ou égale ; sinon on liste les jours (fuseau local).
    private func ensureEveryReceiptIsCovered(terminalId: String, start: Date, end: Date, closures: [DailyFiscalClosureRecord], calendar: Calendar) throws {
        let rows = try db.query(
            "SELECT CreatedAtUtc FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc >= ? AND CreatedAtUtc < ?",
            [.text(terminalId), .text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end))]
        )
        var missing = Set<String>()
        for row in rows {
            guard let created = row.date("CreatedAtUtc"), !closures.contains(where: { $0.periodEndUtc >= created }) else { continue }
            let c = calendar.dateComponents([.year, .month, .day], from: created)
            missing.insert(String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0))
        }
        guard missing.isEmpty else { throw PeriodClosureError.missingDailyClosures(missing.sorted()) }
    }

    private func periodClosureRecord(_ row: SQLRow) -> PeriodClosureRecord? {
        guard let id = row.uuid("Id"), let typeValue = row.int("PeriodType") else { return nil }
        return PeriodClosureRecord(
            id: id, terminalId: row.string("TerminalId") ?? "", periodType: typeValue == 1 ? .monthly : .annual, periodKey: row.string("PeriodKey") ?? "",
            sequence: row.int("ClosureSequence") ?? 0, periodStartUtc: row.date("PeriodStartUtc") ?? Date(timeIntervalSince1970: 0),
            periodEndUtc: row.date("PeriodEndUtc") ?? Date(), totalTtcCents: row.int("TotalTtcAmount") ?? 0, totalHtCents: row.int("TotalHtAmount") ?? 0,
            taxesSummaryJson: row.string("TaxesSummaryJson") ?? "{}", tenderTotalsJson: row.string("TenderTotalsJson") ?? "{}",
            perpetualGrandTotalCents: row.int("PerpetualGrandTotalCents") ?? 0, dailyClosureCount: row.int("DailyClosureCount") ?? 0,
            previousSignatureHash: row.string("PreviousSignatureHash") ?? FiscalHashing.genesisHash, signatureHash: row.string("SignatureHash") ?? "",
            sealedByUserId: row.uuid("SealedByUserId") ?? UUID(), sealedByUserName: row.string("SealedByUserName") ?? "",
            createdAtUtc: row.date("CreatedAtUtc") ?? Date()
        )
    }
}

/// Totaux par clé fusionnés dans l'ordre d'insertion (comme le `Dictionary<string, long>` du .NET), puis sérialisés sans espace.
private struct OrderedTotals {
    private var keys: [String] = []
    private var values: [String: Int] = [:]

    mutating func merge(_ json: String) {
        guard let entries = FiscalJSON.parseIntObject(json) else { return } // JSON illisible d'une donnée ancienne : ignoré comme le .NET
        for (key, value) in entries {
            if values[key] == nil { keys.append(key) }
            values[key, default: 0] += value
        }
    }

    var json: String { FiscalJSON.object(keys.map { ($0, "\(values[$0] ?? 0)") }) }
}
```

- [ ] **Step 5: Vérifier**

Run: `swift test --filter LocalPeriodClosureReferenceTests`
Expected: PASS. En cas d'écart de signature, comparer d'abord `periodEndUtc` (bornes et changement d'heure), puis l'ordre des clés de `taxesJson`.

- [ ] **Step 6: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): clôtures de période mensuelle et annuelle, parité avec la fixture .NET"
```

---

### Task 5: API `LocalPosAPI`, droits, JET et jours manquants

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+PeriodClosures.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift:11-12` (retirer les deux stubs)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPeriodClosureAPITests.swift`

**Interfaces:**
- Consumes: Task 4 (`executePeriodClosure`, `periodClosures`, `PeriodClosureError`).
- Produces: `LocalPosAPI.periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure]` et `executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure` (signatures du protocole `PosAPI`). Refus : `APIError.server(status: 409, message:)` ; clé invalide : `status: 400`.

- [ ] **Step 1: Écrire les tests qui échouent**

`LocalPeriodClosureAPITests.swift` : API sur fichier avec horloge de test (`LocalTestClock`) et calendrier UTC (`localTestCalendar`).

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : clôtures de période")
struct LocalPeriodClosureAPITests {
    private func payOneBurger(_ api: LocalPosAPI, table: String) async throws {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: table))
        _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.creditCard, 1950)]))
    }

    /// API sur fichier pilotée par une horloge de test, avec une seconde connexion pour relire les tables.
    private func makeAPIWithDatabase(pin: String = "1234") async throws -> (LocalPosAPI, SQLiteDatabase, LocalTestClock) {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path), clock)
    }

    @Test func aMonthIsClosedFromItsDailyClosuresAndIsListed() async throws {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        try await payOneBurger(api, table: "T1")
        clock.set("2026-09-10T21:00:00Z")
        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        clock.set("2026-10-02T09:00:00Z")

        let closure = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        #expect(closure.closureSequence == 1)
        #expect(closure.totalTtcCents == z.totalSalesTtc.cents)
        #expect(closure.dailyClosureCount == 1)
        #expect(closure.perpetualGrandTotalCents == z.perpetualGrandTotal.cents)
        #expect(closure.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(try await api.periodClosures(terminalId: nil, periodType: nil).map(\.periodKey) == ["2026-09"])
        #expect(try await api.periodClosures(terminalId: "T01", periodType: .annual).isEmpty)
    }

    @Test func theClosureIsJournaledWithTheDotNetPayload() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        clock.set("2026-10-02T09:00:00Z")
        let closure = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        let entry = try #require(try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'PERIOD_CLOSURE'").first)
        let id = FiscalHashing.guidText(closure.id)
        let signature = try #require(closure.signatureHash)
        #expect(entry.string("PayloadJson") ==
            "{\"PeriodClosureId\":\"\(id)\",\"TerminalId\":\"T01\",\"PeriodType\":\"monthly\",\"PeriodKey\":\"2026-09\",\"ClosureSequence\":1,\"TotalTtcCents\":0,\"PerpetualGrandTotalCents\":0,\"SignatureHash\":\"\(signature)\"}")
    }

    @Test func aMonthStillRunningIsRefusedAndWritesNothing() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        clock.set("2026-09-20T09:00:00Z")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("la clôture aurait dû être refusée")
        } catch let APIError.server(status, _) {
            #expect(status == 409)
        }
        #expect(try db.query("SELECT 1 AS x FROM PeriodClosures").isEmpty)
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'PERIOD_CLOSURE'").isEmpty)
    }

    @Test func aMonthWithAReceiptButNoZClosureListsTheMissingDays() async throws {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        try await payOneBurger(api, table: "T1")
        clock.set("2026-10-02T09:00:00Z")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("la clôture aurait dû être refusée")
        } catch let APIError.server(status, message) {
            #expect(status == 409)
            #expect(message?.contains("2026-09-10") == true)
        }
    }

    @Test func aWaiterCannotClosePeriods() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let api = try await makeLocalAPI(pin: "2468", clock: clock)
        await #expect(throws: APIError.self) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        }
        await #expect(throws: APIError.self) { _ = try await api.periodClosures(terminalId: nil, periodType: nil) }
    }

    @Test func aDoubleClosureIsRefusedAndAnInvalidKeyIs400() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("le doublon aurait dû être refusé")
        } catch let APIError.server(status, _) { #expect(status == 409) }
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-13")
            Issue.record("la clé invalide aurait dû être refusée")
        } catch let APIError.server(status, _) { #expect(status == 400) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalPeriodClosureAPITests`
Expected: FAIL (« non supporté » levé par les stubs).

- [ ] **Step 3: Implémenter l'API**

Supprimer les deux lignes `periodClosures` et `executePeriodClosure` de `LocalPosAPI+Unsupported.swift` (lignes 11-12). Créer `LocalPosAPI+PeriodClosures.swift` :

```swift
import Foundation

extension LocalPosAPI {
    /// Clôtures mensuelles et annuelles du poste autonome (`GET /api/fiscal/period-closures`). Réservé aux responsables ; le poste demandé est ignoré (toujours `T01`).
    public func periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure] {
        try requireManager()
        return try fiscalRepository.periodClosures(terminalId: Self.standaloneTerminalId, type: periodType).map(Self.dto)
    }

    /// Clôture une période (`POST /api/fiscal/period-closures`). Refus métier en 409 (période en cours, déjà clôturée, Z ou mensuelles manquantes),
    /// clé invalide en 400. Un refus n'écrit rien.
    public func executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure {
        let manager = try requireManager()
        let fiscal = fiscalRepository, floor = floorRepository
        let now = clock(), calendar = calendar
        do {
            return try db.transaction { () throws -> FiscalPeriodClosure in
                let settings = try floor.settings()
                let record = try fiscal.executePeriodClosure(
                    terminalId: Self.standaloneTerminalId, type: periodType, key: periodKey, managerId: manager.id, managerName: manager.name,
                    fiscalYearStartMonth: settings.fiscalYearStartMonth ?? 1, fiscalYearStartDay: settings.fiscalYearStartDay ?? 1, calendar: calendar, now: now
                )
                return Self.dto(record)
            }
        } catch let error as PeriodClosureError {
            throw Self.apiError(error)
        }
    }

    private static func dto(_ r: PeriodClosureRecord) -> FiscalPeriodClosure {
        FiscalPeriodClosure(
            id: r.id, terminalId: r.terminalId, periodType: r.periodType, periodKey: r.periodKey, closureSequence: r.sequence,
            periodStartUtc: r.periodStartUtc, periodEndUtc: r.periodEndUtc, totalTtc: Money(cents: r.totalTtcCents), totalHt: Money(cents: r.totalHtCents),
            totalTtcCents: r.totalTtcCents, totalHtCents: r.totalHtCents, taxesSummaryJson: r.taxesSummaryJson, tenderTotalsJson: r.tenderTotalsJson,
            perpetualGrandTotal: Money(cents: r.perpetualGrandTotalCents), perpetualGrandTotalCents: r.perpetualGrandTotalCents,
            dailyClosureCount: r.dailyClosureCount, previousSignatureHash: r.previousSignatureHash, signatureHash: r.signatureHash,
            sealedByUserId: r.sealedByUserId, sealedByUserName: r.sealedByUserName, createdAtUtc: r.createdAtUtc, printQueued: false
        )
    }

    private static func apiError(_ error: PeriodClosureError) -> APIError {
        switch error {
        case .invalidKey:
            .server(status: 400, message: "Clé de période invalide : attendu AAAA-MM (mensuelle) ou AAAA (annuelle).")
        case .notEnded:
            .server(status: 409, message: "La période fiscale n'est pas encore terminée.")
        case .alreadyClosed:
            .server(status: 409, message: "Cette période fiscale est déjà clôturée.")
        case .missingDailyClosures(let days):
            .server(status: 409, message: "Clôtures journalières (Z) manquantes pour les jours avec des reçus : \(days.joined(separator: ", ")).")
        case .missingMonthlyClosures(let months):
            .server(status: 409, message: "Clôtures mensuelles manquantes pour l'exercice : \(months.joined(separator: ", ")).")
        }
    }
}
```

Vérifier que `StaffMember` expose bien `name` (`grep -n "struct StaffMember" -A8 ios/Packages/PosKit/Sources/PosKit/Models`) et que `calendar` est accessible depuis l'extension (propriété de `LocalPosAPI`, `LocalPosAPI.swift:20`). Si `db.transaction` laisse passer l'erreur `PeriodClosureError` telle quelle, le `catch` ci-dessus la convertit ; s'il l'enveloppe, adapter le `catch` à l'erreur réelle.

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalPeriodClosureAPITests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): API des clôtures de période en mode autonome"
```

---

### Task 6: Vérification globale, suivi et documentation

**Files:**
- Create: `docs/plans/ipad-standalone-3a-progress.md`
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift »)
- Modify: `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` (nom de table)

- [ ] **Step 1: Suites complètes**

Run: `cd ios/Packages/PosKit && swift test` puis `cd ../../.. && dotnet test RestaurantPos.slnx`
Expected: tout PASS ; relever les nombres de tests (PosKit, .NET).

- [ ] **Step 2: Corriger le spec**

Remplacer `FiscalPeriodClosures` par `PeriodClosures` dans le spec (section Décisions, lots 3a et 3d) et préciser : « table nommée comme le `DbSet` .NET ».

- [ ] **Step 3: Écrire le suivi**

`docs/plans/ipad-standalone-3a-progress.md` : une ligne par tâche (statut, nombre de tests .NET et PosKit, findings ouverts), mise à jour après chaque tâche.

- [ ] **Step 4: Compléter `CLAUDE.md`**

Dans le paragraphe « Parité fiscale .NET/Swift », ajouter : les clôtures de période (table `PeriodClosures`, migration V8) sont portées dans `LocalFiscalRepository+PeriodClosures` ; `period_closure_reference.json` est **généré par le .NET** (`WRITE_PERIOD_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter PeriodClosureReferenceFixtureTests`) et rejoué par `LocalPeriodClosureReferenceTests` ; toute modification de `ExecutePeriodClosureAsync` ou de `ComputePeriodClosureHash` impose de la régénérer.

- [ ] **Step 5: Commit, puis revue de branche**

```bash
git add -A docs CLAUDE.md ios tests
git commit -m "docs: suivi du lot 3a, règle de parité des clôtures de période"
```

Lancer ensuite la revue de la branche (`superpowers:requesting-code-review`) avant de fusionner.
