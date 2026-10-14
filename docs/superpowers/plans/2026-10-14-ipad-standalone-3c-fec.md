# iPad autonome 3c : export FEC Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le mode autonome produit le même fichier FEC que `FecExportService` (.NET), octet pour octet, et consigne l'export au JET (`FEC_EXPORTED`).

**Architecture:** Port en quatre pièces dans `ios/Packages/PosKit/Sources/PosKit/Local/` : `LocalFecFormat` (formats, comptes, en-tête : fonctions pures), `LocalFecBreakdown` (ventilation HT / TVA par taux : HT exact de `HtBreakdownJson` ou déduction depuis la TVA), `LocalFecWriter` (écritures d'une liste de reçus : pur), `LocalFecExporter` (lecture SQLite de `FiscalReceipts` + `PaymentTenders`). `LocalPosAPI.exportFec` (déjà déclarée dans `PosAPI`, aujourd'hui « non supportée ») relie le tout et écrit le JET. La preuve est une fixture générée par le .NET (`fec_reference.json` : reçus + FEC attendus) rejouée en Swift.

**Tech Stack:** Swift 6 / Swift Testing / SQLite (PosKit) ; .NET 9 / xUnit / EF InMemory pour la fixture.

**Spec:** `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` (lot 3c, Décisions « FEC » et « Fuseau horaire », Risques).

**Branche :** `feat/ipad-standalone-3c-fec`, créée depuis `fix/debut-periode-z` (HEAD `2a82e19`, lot 3a terminé) ou depuis `main` si cette branche y est fusionnée. Aucune dépendance de code avec le lot 3b (V9, archives) : 3c n'ajoute **aucune migration**. Conflits attendus et triviaux à la fusion avec 3b : `LocalJournalEvents.swift`, `LocalPosAPI+Unsupported.swift`, `CLAUDE.md`.

## Faits établis en lisant le .NET (à ne pas re-décider)

- `FecExportService.GenerateFecAsync` (`src/RestaurantPos.Infrastructure/Services/FecExportService.cs`) lit **tous** les reçus (`OrderBy(SequenceNumber)`, aucun filtre de poste), garde ceux dont `CreatedAtUtc ∈ [début ; fin]` (bornes incluses, inversées si début > fin), une écriture par reçu numérotée `D7` à partir de 1.
- Dates d'écriture, de pièce et de validation : `CreatedAtUtc.ToString("yyyyMMdd")` dans le décalage stocké, **UTC** en pratique (`DateTimeOffset.UtcNow`). Le fuseau local n'intervient jamais : un reçu de 23 h 30 UTC (1 h 30 à Paris) est daté du jour UTC. Le Swift formate en UTC (calendrier grégorien, `.gmt`), sans dépendre du `calendar` injecté.
- Nom de fichier : `{SIREN rogné, ou 000000000 si vide}FEC{fin, yyyyMMdd}.txt`, la fin étant prise **après** l'inversion éventuelle.
- Texte : `StringBuilder.AppendLine` (fin de ligne `\n` sur le serveur macOS/Linux, y compris après la dernière ligne), `Encoding.UTF8.GetBytes` (pas de BOM), 18 colonnes séparées par `\t`, montants `(cents / 100m).ToString("0.00")`, taux `{rate:0.#}`.
- `FecExportRequest.CompanyName` n'est **pas** utilisé dans le fichier. Aucun réglage (`RestaurantSettings`) n'est lu : le SIREN est le seul paramètre, et l'iPad le reçoit déjà en argument (`FiscalStore.exportFec` passe `settings.siren`). Rien à ajouter dans les réglages locaux.
- Endpoint `GET /api/fiscal/fec` : réservé `RequireManagerOrAdmin` ; dates par défaut (`UtcNow.Date − 30 j`, `UtcNow`) sans objet sur l'iPad, car `PosAPI.exportFec(from:to:siren:)` exige les deux dates et le client HTTP les envoie toujours. JET `FEC_EXPORTED`, `terminalId: null`, opérateur = utilisateur connecté, charge utile `{"StartDateUtc":…,"EndDateUtc":…,"Siren":…,"Company":…,"FileName":…}` avec les dates **telles que reçues** (avant inversion). L'iPad n'a pas de paramètre société : `"Company":null`, comme un appel HTTP sans `company`.

## Global Constraints

- Montants en centimes entiers (`Int`), jamais de `Double`.
- Aucune migration : lecture seule de `FiscalReceipts` (V5 + `HtBreakdownJson` V6) et `PaymentTenders` (V5).
- Signature publique inchangée : `public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data)`.
- Action réservée aux responsables (`requireManager`, `APIError.forbidden` pour un serveur) ; `FiscalStore` affiche déjà le message de refus.
- Libellés, comptes et formats copiés **à l'identique** de `FecExportService.cs` (ASCII sans accents : « Collectee », « Especes »…).
- Fin de ligne `\n`, UTF-8 sans BOM.
- Dates d'écriture en UTC ; aucun test ne dépend du fuseau de la machine.
- JSON du JET via `FiscalJSON.object` (format `System.Text.Json`), dates via `FiscalJSON.dateTime`.
- Clés JSON des ventilations lues dans l'ordre du texte (`FiscalJSON.parseIntObject`), jamais via un `Dictionary` Swift.
- Tests : `swift test` dans `ios/Packages/PosKit` ; .NET : `dotnet test tests/RestaurantPos.Infrastructure.Tests`. `Directory.Build.props` traite les avertissements .NET en erreurs (`dotnet format RestaurantPos.slnx` si besoin, puis annuler les reformatages hors du fichier créé).

## Review Focus

- Reçu de 23 h 30 UTC (1 h 30 du matin à Paris) : daté du jour UTC dans le FEC, comme le .NET, même si l'iPad est réglé sur Paris (Task 1, reçu 5, générateur en `TZ=Europe/Paris` ; Task 5, rejeu).
- Bornes de période incluses et dates saisies à l'envers (début > fin) : mêmes reçus et même nom de fichier (date la plus tardive) que le .NET (Task 1, export 2 ; Task 4, test d'inversion ; Task 5).
- Annulation d'une vente avec pourboire : l'excédent est contre-passé au débit du 468600 et chaque ligne change de sens (Task 1, reçu 7 ; Task 4).
- Reçu de facturation chambre, propre à l'iPad (le .NET n'en écrit pas) : règlement au 411000 « Clients - Facturation Chambre » (Task 1, reçu 12 ; Task 2).
- Vraies ventes de l'iPad (pourboire, annulation) : chaque écriture du FEC exporté est équilibrée, l'export est consigné au JET, un serveur ne peut pas exporter (Task 6).

---

### Task 1: Fixture .NET `fec_reference.json`

**Files:**
- Create: `tests/RestaurantPos.Infrastructure.Tests/FecReferenceFixtureTests.cs`
- Create (généré): `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fec_reference.json`

**Interfaces:**
- Consumes: `FecExportService.GenerateFecAsync(FecExportRequest)` (.NET, inchangé).
- Produces: le JSON suivant, relu par Task 5.
  ```
  { "receipts": [ { "id": String (GUID minuscule), "receiptNumber": String, "sequenceNumber": Int, "createdAtUtc": String,
                    "ttcCents": Int, "htCents": Int, "taxBreakdownJson": String, "htBreakdownJson": String?,
                    "voidedReceiptId": String?,
                    "tenders": [ { "id": String, "method": Int, "amountCents": Int, "tenderedCents": Int, "changeGivenCents": Int } ] } ],
    "exports": [ { "name": String, "fromUtc": String, "toUtc": String, "siren": String, "fileName": String,
                   "totalRecords": Int, "totalDebitCents": Int, "totalCreditCents": Int,
                   "fecText": String, "fecBase64": String } ] }
  ```
  Dates au format `yyyy-MM-ddTHH:mm:ss.fffZ` (celui de `FiscalHashing.storedText`). `fecBase64` = octets exacts du fichier ; `fecText` = même contenu, lisible dans un diff.

- [ ] **Step 0: Créer la branche**

```bash
git switch fix/debut-periode-z && git switch -c feat/ipad-standalone-3c-fec
```

- [ ] **Step 1: Écrire le test générateur**

Le test tourne sous `TZ=Europe/Paris` (restauré ensuite) pour prouver que les dates d'écriture restent UTC. Il insère douze reçus d'un seul poste `T01` avec des montants cohérents avec les règles actuelles (sauf les reçus 9 et 11, volontairement hors règle), appelle le vrai `FecExportService` dans un second `DbContext`, sérialise reçus et exports, écrit le fichier si `WRITE_FEC_FIXTURE=1`, puis compare.

```csharp
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
```

- [ ] **Step 2: Générer la fixture**

Run: `WRITE_FEC_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FecReferenceFixtureTests`
Expected: PASS ; `fec_reference.json` existe. Contrôle à l'œil (le .NET est l'oracle : si une valeur diffère, relire `FecExportService.cs` avant de toucher au générateur) :
- export « septembre 2026 » : `fileName` `123456789FEC20260930.txt`, écritures `0000001` (reçu 2) à `0000010` (reçu 11), reçus 1 et 12 absents ;
- reçu 3 : ligne `468600` au crédit `5.00` ; reçu 7 : lignes `706100` / `706200` / `445710` / `445720` au débit, `512000` au crédit `50.00`, `468600` au débit `5.00` ;
- reçu 5 : `706000` `9.82`, `706550` `9.45`, date `20260902` ; reçu 9 : mêmes montants (repli) ; reçu 6 : libellé `Vente T01-000006 (HT 10%)` ;
- reçu 10 : `530000` « Reglement Caisse T01-000010 » ; reçu 11 : `706000` `1.93`, `706100` `9.95`, `706200` `9.98` ;
- export 2 : `987654321FEC20260903.txt`, reçus 3 à 7 (bornes incluses) ; export 3 : `000000000FEC20261130.txt`, en-tête seul ; export 4 : douze écritures dont `411000` pour le reçu 12 ;
- `fecText` ne contient aucun `\r`.

- [ ] **Step 3: Relancer sans régénération**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FecReferenceFixtureTests`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/RestaurantPos.Infrastructure.Tests/FecReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fec_reference.json
git commit -m "test(fiscal): fixture .NET du FEC pour la parité iPad"
```

---

### Task 2: Formats et comptes du FEC (`LocalFecFormat`)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFecFormat.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecFormatTests.swift`

**Interfaces:**
- Consumes: `PaymentMethod` (`Models/Enums.swift`, `cash = 0 … roomCharge = 4`).
- Produces:
  ```swift
  struct FecAccount: Equatable { let number: String; let label: String }
  enum LocalFecFormat {
      static let header: String
      static let tipsAccount: FecAccount          // 468600
      static let cashFallbackAccount: FecAccount  // 530000
      static func amount(_ cents: Int) -> String
      static func rateLabel(_ rate: Decimal) -> String
      static func salesAccount(rate: Decimal) -> FecAccount
      static func vatAccount(rate: Decimal) -> FecAccount
      static func paymentAccount(method: Int) -> FecAccount
      static func methodName(_ method: Int) -> String
      static func date(_ date: Date) -> String
      static func fileName(siren: String, end: Date) -> String
      static func entryNumber(_ number: Int) -> String
      static func line(entry: String, date: String, account: FecAccount, pieceRef: String, label: String, debit: Int, credit: Int) -> String
  }
  ```

- [ ] **Step 1: Écrire les tests qui échouent**

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("FEC : formats et comptes (miroir de FecExportService)")
struct LocalFecFormatTests {
    private func decimal(_ text: String) -> Decimal { Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))! }

    @Test func headerHasTheEighteenOfficialColumns() {
        let columns = LocalFecFormat.header.split(separator: "\t", omittingEmptySubsequences: false)
        #expect(columns.count == 18)
        #expect(columns.first == "JournalCode")
        #expect(columns[11] == "Debit")
        #expect(columns.last == "Idevise")
    }

    @Test func amountsUseTwoDecimalsAndAPoint() {
        #expect(LocalFecFormat.amount(0) == "0.00")
        #expect(LocalFecFormat.amount(5) == "0.05")
        #expect(LocalFecFormat.amount(3850) == "38.50")
        #expect(LocalFecFormat.amount(123456) == "1234.56")
    }

    @Test func rateLabelsFollowTheDotNetZeroHashFormat() {
        #expect(LocalFecFormat.rateLabel(10) == "10")
        #expect(LocalFecFormat.rateLabel(decimal("10.0")) == "10")
        #expect(LocalFecFormat.rateLabel(decimal("5.5")) == "5.5")
        #expect(LocalFecFormat.rateLabel(decimal("2.1")) == "2.1")
        #expect(LocalFecFormat.rateLabel(0) == "0")
        #expect(LocalFecFormat.rateLabel(decimal("8.25")) == "8.3") // milieu arrondi loin de zéro, comme un decimal .NET
    }

    @Test func salesAndVatAccountsFollowTheRateBands() {
        #expect(LocalFecFormat.salesAccount(rate: 20) == FecAccount(number: "706200", label: "Ventes Boissons Alcoolisees 20%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("19.6")).number == "706200")
        #expect(LocalFecFormat.salesAccount(rate: 10) == FecAccount(number: "706100", label: "Ventes Restauration Sur Place 10%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("5.5")) == FecAccount(number: "706550", label: "Ventes Restauration A Emporter 5.5%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("2.1")) == FecAccount(number: "706000", label: "Ventes Prestations Restauration"))
        #expect(LocalFecFormat.salesAccount(rate: 0).number == "706000")
        #expect(LocalFecFormat.vatAccount(rate: 20) == FecAccount(number: "445720", label: "TVA Collectee 20%"))
        #expect(LocalFecFormat.vatAccount(rate: 10) == FecAccount(number: "445710", label: "TVA Collectee 10%"))
        #expect(LocalFecFormat.vatAccount(rate: decimal("5.5")) == FecAccount(number: "445755", label: "TVA Collectee 5.5%"))
        #expect(LocalFecFormat.vatAccount(rate: decimal("2.1")) == FecAccount(number: "445700", label: "TVA Collectee"))
    }

    @Test func paymentAccountsAndNamesMatchTheDotNetEnum() {
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.creditCard.rawValue) == FecAccount(number: "512000", label: "Banque Cartes Bancaires"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.cash.rawValue) == FecAccount(number: "530000", label: "Caisse Especes"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.mealVoucher.rawValue) == FecAccount(number: "580000", label: "Titres Restaurant a Encaisser"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.roomCharge.rawValue) == FecAccount(number: "411000", label: "Clients - Facturation Chambre"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.giftCard.rawValue) == FecAccount(number: "419100", label: "Clients - Cartes Cadeaux"))
        #expect(LocalFecFormat.paymentAccount(method: 9) == FecAccount(number: "580000", label: "Virements Internes POS"))
        #expect([0, 1, 2, 3, 4, 9].map(LocalFecFormat.methodName) == ["Cash", "CreditCard", "MealVoucher", "GiftCard", "RoomCharge", "9"])
    }

    @Test func datesAreUtcDaysWhateverTheLocalTimeZone() throws {
        let lateEvening = try #require(SQLDate.parse("2026-09-02T23:30:00.000Z")) // 1 h 30 le 3 à Paris
        #expect(LocalFecFormat.date(lateEvening) == "20260902")
    }

    @Test func fileNameTrimsTheSirenOrUsesNineZeros() throws {
        let end = try #require(SQLDate.parse("2026-09-03T09:00:00.000Z"))
        #expect(LocalFecFormat.fileName(siren: " 987654321 ", end: end) == "987654321FEC20260903.txt")
        #expect(LocalFecFormat.fileName(siren: "  ", end: end) == "000000000FEC20260903.txt")
        #expect(LocalFecFormat.fileName(siren: "", end: end) == "000000000FEC20260903.txt")
    }

    @Test func entryNumbersHaveSevenDigits() {
        #expect(LocalFecFormat.entryNumber(1) == "0000001")
        #expect(LocalFecFormat.entryNumber(1234567) == "1234567")
    }

    @Test func aLineHasTheEighteenFieldsOfFormatFecLine() {
        let line = LocalFecFormat.line(
            entry: "0000001", date: "20260903", account: FecAccount(number: "512000", label: "Banque Cartes Bancaires"),
            pieceRef: "T01-000001", label: "Reglement CreditCard T01-000001", debit: 2000, credit: 0
        )
        #expect(line == "VT\tJournal des Ventes\t0000001\t20260903\t512000\tBanque Cartes Bancaires\t\t\tT01-000001\t20260903\tReglement CreditCard T01-000001\t20.00\t0.00\t\t\t20260903\t\tEUR")
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalFecFormatTests`
Expected: FAIL à la compilation (`LocalFecFormat` inconnu).

- [ ] **Step 3: Implémenter**

```swift
import Foundation

/// Compte du plan comptable, numéro et libellé tels que `FecExportService` les écrit.
struct FecAccount: Equatable {
    let number: String
    let label: String
}

/// Formats, comptes et en-tête du FEC : miroir de `FecExportService.cs` (journal « VT », 18 colonnes séparées par des tabulations,
/// montants `0.00` à point décimal, dates `yyyyMMdd` du jour UTC). Toute modification côté .NET se reporte ici et impose de régénérer
/// `fec_reference.json` (voir `CLAUDE.md`).
enum LocalFecFormat {
    static let header = [
        "JournalCode", "JournalLib", "EcritureNum", "EcritureDate", "CompteNum", "CompteLib", "CompAuxNum", "CompAuxLib", "PieceRef",
        "PieceDate", "EcritureLib", "Debit", "Credit", "EcritureLet", "DateLet", "ValidDate", "Montantdevise", "Idevise",
    ].joined(separator: "\t")

    /// `TipsAccount` du .NET : excédent de règlement (pourboire) au-delà du TTC du reçu, dette envers le personnel.
    static let tipsAccount = FecAccount(number: "468600", label: "Pourboires et excedents de reglement a reverser")
    /// Contrepartie d'un reçu sans règlement enregistré.
    static let cashFallbackAccount = FecAccount(number: "530000", label: "Caisse Especes")

    /// Le .NET formate `CreatedAtUtc` (décalage +00:00) : jour UTC, jamais le fuseau de l'iPad.
    private static let utcCalendar: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .gmt
        return calendar
    }()

    /// `(cents / 100m).ToString("0.00", InvariantCulture)`.
    static func amount(_ cents: Int) -> String {
        let magnitude = cents.magnitude
        let fraction = magnitude % 100
        return (cents < 0 ? "-" : "") + "\(magnitude / 100)." + (fraction < 10 ? "0" : "") + "\(fraction)"
    }

    /// `{rate:0.#}` d'un `decimal` .NET : une décimale au plus, milieu arrondi loin de zéro, pas de zéro final (« 10 », « 5.5 »).
    static func rateLabel(_ rate: Decimal) -> String {
        var scaled = rate * 10
        var rounded = Decimal()
        NSDecimalRound(&rounded, &scaled, 0, .plain)
        let tenths = NSDecimalNumber(decimal: rounded).intValue
        let magnitude = tenths.magnitude
        return (tenths < 0 ? "-" : "") + "\(magnitude / 10)" + (magnitude % 10 == 0 ? "" : ".\(magnitude % 10)")
    }

    private enum RateBand { case twenty, ten, fiveFive, other }

    /// Plages du `switch` .NET : [19,5 ; 20,5], [9,5 ; 10,5], [5,0 ; 6,0], puis le reste.
    private static func band(_ rate: Decimal) -> RateBand {
        let tenths = rate * 10
        if tenths >= 195 && tenths <= 205 { return .twenty }
        if tenths >= 95 && tenths <= 105 { return .ten }
        if tenths >= 50 && tenths <= 60 { return .fiveFive }
        return .other
    }

    static func salesAccount(rate: Decimal) -> FecAccount {
        switch band(rate) {
        case .twenty: FecAccount(number: "706200", label: "Ventes Boissons Alcoolisees 20%")
        case .ten: FecAccount(number: "706100", label: "Ventes Restauration Sur Place 10%")
        case .fiveFive: FecAccount(number: "706550", label: "Ventes Restauration A Emporter 5.5%")
        case .other: FecAccount(number: "706000", label: "Ventes Prestations Restauration")
        }
    }

    static func vatAccount(rate: Decimal) -> FecAccount {
        switch band(rate) {
        case .twenty: FecAccount(number: "445720", label: "TVA Collectee 20%")
        case .ten: FecAccount(number: "445710", label: "TVA Collectee 10%")
        case .fiveFive: FecAccount(number: "445755", label: "TVA Collectee 5.5%")
        case .other: FecAccount(number: "445700", label: "TVA Collectee")
        }
    }

    /// `GetPaymentAccount` ; `method` est la valeur entière stockée (`PaymentTenders.Method`).
    static func paymentAccount(method: Int) -> FecAccount {
        switch PaymentMethod(rawValue: method) {
        case .creditCard: FecAccount(number: "512000", label: "Banque Cartes Bancaires")
        case .cash: FecAccount(number: "530000", label: "Caisse Especes")
        case .mealVoucher: FecAccount(number: "580000", label: "Titres Restaurant a Encaisser")
        case .roomCharge: FecAccount(number: "411000", label: "Clients - Facturation Chambre")
        case .giftCard: FecAccount(number: "419100", label: "Clients - Cartes Cadeaux")
        case nil: FecAccount(number: "580000", label: "Virements Internes POS")
        }
    }

    /// `PaymentMethod.ToString()` du .NET ; une valeur hors énumération s'écrit en chiffres, comme en C#.
    static func methodName(_ method: Int) -> String {
        switch PaymentMethod(rawValue: method) {
        case .cash: "Cash"
        case .creditCard: "CreditCard"
        case .mealVoucher: "MealVoucher"
        case .giftCard: "GiftCard"
        case .roomCharge: "RoomCharge"
        case nil: "\(method)"
        }
    }

    static func date(_ date: Date) -> String {
        let parts = utcCalendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d%02d%02d", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    /// `{SIREN rogné, ou 000000000}FEC{fin}.txt` ; `end` est la borne la plus tardive (après inversion éventuelle).
    static func fileName(siren: String, end: Date) -> String {
        let trimmed = siren.trimmingCharacters(in: .whitespacesAndNewlines)
        return (trimmed.isEmpty ? "000000000" : trimmed) + "FEC" + date(end) + ".txt"
    }

    /// `ToString("D7")`.
    static func entryNumber(_ number: Int) -> String {
        let digits = String(number)
        return String(repeating: "0", count: max(0, 7 - digits.count)) + digits
    }

    /// `FormatFecLine` : journal des ventes, date de pièce et de validation = date d'écriture, colonnes auxiliaires vides, devise EUR.
    static func line(entry: String, date: String, account: FecAccount, pieceRef: String, label: String, debit: Int, credit: Int) -> String {
        [
            "VT", "Journal des Ventes", entry, date, account.number, account.label, "", "", pieceRef, date, label,
            amount(debit), amount(credit), "", "", date, "", "EUR",
        ].joined(separator: "\t")
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalFecFormatTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFecFormat.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecFormatTests.swift
git commit -m "feat(ios): formats et comptes du FEC (miroir de FecExportService)"
```

---

### Task 3: Ventilation HT / TVA par taux (`LocalFecBreakdown`)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFecBreakdown.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecBreakdownTests.swift`

**Interfaces:**
- Consumes: `FiscalJSON.parseIntObject(_:) -> [(String, Int)]?` (`FiscalHashing.swift`, ordre du texte conservé), `OrderMath.split(ttc:ratePercent:)` (miroir de `TaxBreakdownItem.Calculate`), `Money.roundAwayFromZero(_:) -> Int` (interne).
- Produces:
  ```swift
  struct FecVatItem: Equatable { let rate: Decimal; let htCents: Int; let vatCents: Int }
  struct FecHtRange: Equatable { let lo: Int; let hi: Int; let est: Int }
  enum LocalFecBreakdown {
      static func items(ttcCents: Int, htCents: Int, taxBreakdownJson: String, htBreakdownJson: String?) -> [FecVatItem]
      static func feasibleHt(rate: Decimal, vatCents: Int) -> FecHtRange
  }
  ```

- [ ] **Step 1: Écrire les tests qui échouent**

Les valeurs attendues viennent de `tests/RestaurantPos.Infrastructure.Tests/FecExportServiceTests.cs` (mêmes entrées).

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("FEC : ventilation HT / TVA (miroir de ParseVatBreakdown)")
struct LocalFecBreakdownTests {
    private func decimal(_ text: String) -> Decimal { Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))! }
    private let twoLowRatesVat = "{\"2.1\": 21, \"5.5\": 52}"

    @Test func feasibleHtGivesTheRangeConsistentWithTheVat() {
        #expect(LocalFecBreakdown.feasibleHt(rate: 10, vatCents: 100) == FecHtRange(lo: 995, hi: 1005, est: 1000))
        #expect(LocalFecBreakdown.feasibleHt(rate: 20, vatCents: 200) == FecHtRange(lo: 998, hi: 1003, est: 1000))
        #expect(LocalFecBreakdown.feasibleHt(rate: decimal("5.5"), vatCents: 52).est == 945)
    }

    @Test func coherentHtBreakdownIsTakenAsIs() {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: "{\"2.1\":979,\"5.5\":948}")
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 948, vatCents: 52)])
    }

    @Test func voidHtBreakdownGivesAbsoluteValues() {
        let items = LocalFecBreakdown.items(ttcCents: -2000, htCents: -1927, taxBreakdownJson: "{\"2.1\": -21, \"5.5\": -52}", htBreakdownJson: "{\"2.1\":-979,\"5.5\":-948}")
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 948, vatCents: 52)])
    }

    @Test func zeroRateGroupKeepsItsHt() {
        let exact = LocalFecBreakdown.items(ttcCents: 1700, htCents: 1500, taxBreakdownJson: "{\"0\": 0, \"20\": 200}", htBreakdownJson: "{\"0\":500,\"20\":1000}")
        let deduced = LocalFecBreakdown.items(ttcCents: 1700, htCents: 1500, taxBreakdownJson: "{\"0\": 0, \"20\": 200}", htBreakdownJson: nil)
        let expected = [FecVatItem(rate: 0, htCents: 500, vatCents: 0), FecVatItem(rate: 20, htCents: 1000, vatCents: 200)]
        #expect(exact == expected)
        #expect(deduced == expected)
    }

    @Test func legacyReceiptDeducesHtAndTheLowestRateAbsorbsTheRest() {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 982, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 945, vatCents: 52)])
    }

    @Test func reducedRateRoundingStaysOnTheAbsorber() {
        let items = LocalFecBreakdown.items(ttcCents: 2100, htCents: 1979, taxBreakdownJson: "{\"2.1\": 21, \"10\": 100}", htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: 10, htCents: 1000, vatCents: 100)])
    }

    @Test(arguments: [
        "pas du json",
        "{\"2.1\":1500,\"5.5\":427}", // somme conservée, ventilation hors plage faisable
        "{\"2.1\":979,\"5.5\":900}",  // somme fausse
        "{\"2.1\":979,\"10\":948}",   // clés différentes de la TVA signée
        "{}",
        "   ",
    ])
    func unusableHtBreakdownFallsBackToTheDeduction(htJson: String) {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: htJson)
        #expect(items.map(\.htCents) == [982, 945])
    }

    @Test func negativeResidualIsTakenBackWithinTheFeasibleRanges() {
        // Reçu historique hors règle : estimations 1000 + 1000 > HT 1800 ; 10 % descend à 995, 20 % à 998, le 0 % absorbe −193.
        let items = LocalFecBreakdown.items(ttcCents: 2100, htCents: 1800, taxBreakdownJson: "{\"0\":0,\"10\":100,\"20\":200}", htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: 0, htCents: -193, vatCents: 0), FecVatItem(rate: 10, htCents: 995, vatCents: 100), FecVatItem(rate: 20, htCents: 998, vatCents: 200)])
    }

    @Test func duplicateRateKeysAreMergedInTheExactBreakdown() {
        // « 10 » et « 10.0 » sont le même taux decimal côté .NET : fusion, ordre de la première clé.
        let items = LocalFecBreakdown.items(ttcCents: 1100, htCents: 1000, taxBreakdownJson: "{\"10\":100}", htBreakdownJson: "{\"10\":600,\"10.0\":400}")
        #expect(items == [FecVatItem(rate: 10, htCents: 1000, vatCents: 100)])
    }

    @Test func emptyOrUnreadableVatFallsBackToASingleTenPercentLine() {
        for json in ["{}", "", "pas du json", "{\"abc\":100}"] {
            let items = LocalFecBreakdown.items(ttcCents: 1100, htCents: 1000, taxBreakdownJson: json, htBreakdownJson: nil)
            #expect(items == [FecVatItem(rate: 10, htCents: 1000, vatCents: 100)], "\(json)")
        }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFecBreakdownTests`
Expected: FAIL à la compilation (`LocalFecBreakdown` inconnu).

- [ ] **Step 3: Implémenter**

```swift
import Foundation

/// Ventilation d'un reçu à un taux. Valeurs absolues (le sens vient de l'annulation, à l'écriture), sauf le HT de l'absorbeur d'un
/// reçu historique hors règle, qui peut être négatif : l'écriture en prend la valeur absolue, comme le .NET.
struct FecVatItem: Equatable {
    let rate: Decimal
    let htCents: Int
    let vatCents: Int
}

/// HT cohérents avec une TVA à un taux (`[lo ; hi]`) et le plus proche de TVA / taux (`est`).
struct FecHtRange: Equatable {
    let lo: Int
    let hi: Int
    let est: Int
}

/// Miroir de `ParseVatBreakdown`, `ParseExactBreakdown`, `IsCoherent`, `ParseByRate` et `FeasibleHt` (`FecExportService.cs`).
/// Reçu récent : `HtBreakdownJson` (colonne non signée) donne le HT exact par taux s'il colle au reçu signé. Sinon (reçu historique,
/// colonne incohérente ou illisible) le HT est déduit de la TVA ; un taux 0 % ou, à défaut, le plus petit taux absorbe le reste.
/// L'ordre des taux est celui du JSON : le .NET énumère ses dictionnaires dans l'ordre d'insertion.
enum LocalFecBreakdown {
    private typealias RateCents = (rate: Decimal, cents: Int)

    static func items(ttcCents: Int, htCents: Int, taxBreakdownJson: String, htBreakdownJson: String?) -> [FecVatItem] {
        if let exact = exact(ttcCents: ttcCents, taxJson: taxBreakdownJson, htJson: htBreakdownJson) { return exact }
        let totalHt = abs(htCents)
        let totalVat = abs(ttcCents) - totalHt
        if !isBlank(taxBreakdownJson), taxBreakdownJson != "{}", let entries = FiscalJSON.parseIntObject(taxBreakdownJson) {
            // Une clé qui n'est pas un taux est ignorée (`decimal.TryParse` échoue), le reste est gardé.
            let parsed: [RateCents] = entries.compactMap { entry in rate(entry.0).map { (rate: $0, cents: abs(entry.1)) } }
            if !parsed.isEmpty { return deduced(parsed, totalHt: totalHt) }
        }
        return [FecVatItem(rate: 10, htCents: totalHt, vatCents: totalVat)]
    }

    /// HT possibles pour une TVA donnée (TVA = TTC − HT, HT = round(TTC / (1 + t))). Plage vide : estimation seule.
    static func feasibleHt(rate: Decimal, vatCents: Int) -> FecHtRange {
        let estimate = Money.roundAwayFromZero(Decimal(vatCents) / (rate / 100))
        var quotient = 100 / rate
        var ceiling = Decimal()
        NSDecimalRound(&ceiling, &quotient, 0, .up)
        let window = NSDecimalNumber(decimal: ceiling).intValue + 2
        var feasible: [Int] = []
        for base in max(0, estimate - window)...(estimate + window) {
            let parts = OrderMath.split(ttc: Money(cents: base + vatCents), ratePercent: rate)
            if parts.ht.cents == base && parts.vat.cents == vatCents { feasible.append(base) }
        }
        guard let first = feasible.first, let last = feasible.last else { return FecHtRange(lo: estimate, hi: estimate, est: estimate) }
        var closest = first // à égalité, le plus petit (`OrderBy` stable)
        for base in feasible where abs(base - estimate) < abs(closest - estimate) { closest = base }
        return FecHtRange(lo: first, hi: last, est: closest)
    }

    // MARK: HT exact (HtBreakdownJson)

    private static func exact(ttcCents: Int, taxJson: String, htJson: String?) -> [FecVatItem]? {
        guard let htJson, !isBlank(htJson), let ht = byRate(htJson), !ht.isEmpty else { return nil }
        let vat: [RateCents]
        if isBlank(taxJson) {
            vat = []
        } else {
            guard let parsed = byRate(taxJson) else { return nil }
            vat = parsed
        }
        guard coherent(ttcCents: ttcCents, ht: ht, vat: vat) else { return nil }
        // Mêmes taux des deux côtés (`coherent` l'impose) : la branche .NET « TVA sans HT » ne peut pas servir.
        return ht.map { h in FecVatItem(rate: h.rate, htCents: abs(h.cents), vatCents: abs(cents(at: h.rate, in: vat))) }
    }

    /// La colonne n'est pas signée : on ne la croit que si elle colle au reçu signé (mêmes taux, Σ|HT| = |TTC| − Σ|TVA|,
    /// et chaque HT à taux > 0 dans la plage cohérente avec sa TVA).
    private static func coherent(ttcCents: Int, ht: [RateCents], vat: [RateCents]) -> Bool {
        let sameRates = ht.allSatisfy { h in vat.contains { $0.rate == h.rate } } && vat.allSatisfy { v in ht.contains { $0.rate == v.rate } }
        let htSum = ht.reduce(0) { $0 + abs($1.cents) }
        let vatSum = vat.reduce(0) { $0 + abs($1.cents) }
        guard sameRates, htSum == abs(ttcCents) - vatSum else { return false }
        return ht.allSatisfy { h in
            guard h.rate > 0 else { return true }
            let range = feasibleHt(rate: h.rate, vatCents: abs(cents(at: h.rate, in: vat)))
            return (range.lo...range.hi).contains(abs(h.cents))
        }
    }

    /// `ParseByRate` : taux decimal → centimes, doublons (« 10 » et « 10.0 ») additionnés à la place de la première clé.
    /// `nil` si le JSON est illisible ou si une clé n'est pas un taux.
    private static func byRate(_ json: String) -> [RateCents]? {
        guard let entries = FiscalJSON.parseIntObject(json) else { return nil }
        var result: [RateCents] = []
        for (key, value) in entries {
            guard let rate = rate(key) else { return nil }
            if let index = result.firstIndex(where: { $0.rate == rate }) {
                result[index].cents += value
            } else {
                result.append((rate: rate, cents: value))
            }
        }
        return result
    }

    // MARK: HT déduit de la TVA (reçus historiques)

    private static func deduced(_ parsed: [RateCents], totalHt: Int) -> [FecVatItem] {
        let absorber = parsed.firstIndex { $0.rate == 0 } ?? parsed.indices.min { parsed[$0].rate < parsed[$1].rate } ?? 0
        let ranges = parsed.map { $0.rate > 0 ? feasibleHt(rate: $0.rate, vatCents: $0.cents) : FecHtRange(lo: 0, hi: 0, est: 0) }
        var ht = ranges.map(\.est)
        ht[absorber] = 0
        var residual = totalHt - ht.reduce(0, +)
        // Reste négatif (estimations trop hautes) : repris sur les taux les plus bas d'abord, dans leur plage cohérente (tri stable).
        let order = parsed.indices.filter { $0 != absorber }.sorted {
            parsed[$0].rate != parsed[$1].rate ? parsed[$0].rate < parsed[$1].rate : $0 < $1
        }
        for index in order where residual < 0 {
            let shift = min(-residual, ht[index] - ranges[index].lo)
            ht[index] -= shift
            residual += shift
        }
        ht[absorber] = residual
        return parsed.indices.map { FecVatItem(rate: parsed[$0].rate, htCents: ht[$0], vatCents: parsed[$0].cents) }
    }

    // MARK: Outils

    private static func cents(at rate: Decimal, in list: [RateCents]) -> Int {
        list.first { $0.rate == rate }?.cents ?? 0
    }

    private static func isBlank(_ text: String) -> Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    /// Clé de taux (« 10 », « 5.5 », « 10.0 »).
    /// ponytail: nombres décimaux simples seulement ; `decimal.TryParse(NumberStyles.Any)` accepte aussi « 1e1 », « +5 » ou des
    /// séparateurs de milliers, que ni le .NET ni l'iPad n'écrivent. Élargir ici si une clé de cette forme apparaît.
    private static func rate(_ key: String) -> Decimal? {
        let text = key.trimmingCharacters(in: .whitespaces)
        guard text.range(of: #"^-?[0-9]+(\.[0-9]+)?$"#, options: .regularExpression) != nil else { return nil }
        return Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalFecBreakdownTests`
Expected: PASS. Si `feasibleHt(rate: 10, vatCents: 100)` ne rend pas `995…1005`, vérifier que `OrderMath.split` arrondit bien loin de zéro (`Money.roundAwayFromZero`) avant de toucher au test.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFecBreakdown.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecBreakdownTests.swift
git commit -m "feat(ios): ventilation HT/TVA du FEC (HT exact ou déduit, miroir du .NET)"
```

---

### Task 4: Écritures du FEC (`LocalFecWriter`)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFecWriter.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecWriterTests.swift`

**Interfaces:**
- Consumes: `LocalFecFormat` (Task 2), `LocalFecBreakdown.items(ttcCents:htCents:taxBreakdownJson:htBreakdownJson:) -> [FecVatItem]` (Task 3).
- Produces:
  ```swift
  struct FecTender: Equatable { let method: Int; let amountCents: Int }
  struct FecReceipt: Equatable {
      let receiptNumber: String; let createdAtUtc: Date; let ttcCents: Int; let htCents: Int
      let taxBreakdownJson: String; let htBreakdownJson: String?; let voidsReceipt: Bool; let tenders: [FecTender]
  }
  struct FecExport: Equatable { let fileName: String; let data: Data; let totalRecords: Int; let totalDebitCents: Int; let totalCreditCents: Int }
  enum LocalFecWriter {
      static func export(receipts: [FecReceipt], from: Date, to: Date, siren: String) -> FecExport
  }
  ```
  `receipts` arrive triés par `SequenceNumber` ; le writer filtre la période lui-même.

- [ ] **Step 1: Écrire les tests qui échouent**

Mêmes cas que `FecExportServiceTests.cs` côté .NET, plus la période et l'inversion des bornes.

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("FEC : écritures (miroir de GenerateFecAsync)")
struct LocalFecWriterTests {
    private struct Row: Equatable {
        let num: String
        let account: String
        let debit: Int
        let credit: Int
        let label: String
    }

    private let noon = SQLDate.parse("2026-09-03T12:00:00.000Z")!

    private func receipt(_ number: String = "POS01-000001", at date: Date? = nil, ttc: Int, ht: Int, tax: String, htJson: String? = nil,
                         void: Bool = false, tenders: [(PaymentMethod, Int)]) -> FecReceipt {
        FecReceipt(
            receiptNumber: number, createdAtUtc: date ?? noon, ttcCents: ttc, htCents: ht, taxBreakdownJson: tax, htBreakdownJson: htJson,
            voidsReceipt: void, tenders: tenders.map { FecTender(method: $0.0.rawValue, amountCents: $0.1) }
        )
    }

    private func export(_ receipts: [FecReceipt]) -> FecExport {
        LocalFecWriter.export(receipts: receipts, from: noon.addingTimeInterval(-86_400), to: noon.addingTimeInterval(86_400), siren: "123456789")
    }

    private func rows(_ result: FecExport) -> [Row] {
        String(decoding: result.data, as: UTF8.self).split(separator: "\n").dropFirst().map { line in
            let c = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            return Row(num: c[2], account: c[4], debit: cents(c[11]), credit: cents(c[12]), label: c[10])
        }
    }

    private func cents(_ amount: String) -> Int { Int(amount.replacingOccurrences(of: ".", with: "")) ?? -1 }

    private func balancedPerEntry(_ rows: [Row]) -> Bool {
        Dictionary(grouping: rows, by: \.num).values.allSatisfy { e in e.reduce(0) { $0 + $1.debit } == e.reduce(0) { $0 + $1.credit } }
    }

    @Test func aSaleWithTwoTendersBalancesAndEndsWithANewline() {
        let result = export([receipt(ttc: 3850, ht: 3500, tax: "{\"10.0\": 350}", tenders: [(.creditCard, 2000), (.cash, 1850)])])
        let r = rows(result)
        #expect(r.map(\.account) == ["706100", "445710", "512000", "530000"])
        #expect(r[0] == Row(num: "0000001", account: "706100", debit: 0, credit: 3500, label: "Vente POS01-000001 (HT 10%)"))
        #expect(r[1].label == "TVA Collectee 10% POS01-000001")
        #expect(r[2] == Row(num: "0000001", account: "512000", debit: 2000, credit: 0, label: "Reglement CreditCard POS01-000001"))
        #expect(result.totalRecords == 4 && result.totalDebitCents == 3850 && result.totalCreditCents == 3850)
        #expect(result.fileName == "123456789FEC20260904.txt")
        let text = String(decoding: result.data, as: UTF8.self)
        #expect(text.hasPrefix(LocalFecFormat.header + "\n"))
        #expect(text.hasSuffix("\tEUR\n"))
        #expect(!text.contains("\r"))
    }

    @Test func aVoidAtSeveralRatesIsDebitedPerRate() {
        let r = rows(export([receipt("POS01-VOID-000002", ttc: -2300, ht: -2000, tax: "{\"10.0\": -100, \"20.0\": -200}", void: true, tenders: [(.cash, -2300)])]))
        #expect(r.first { $0.account == "706100" }?.debit == 1000)
        #expect(r.first { $0.account == "706200" }?.debit == 1000)
        #expect(r.first { $0.account == "445720" }?.debit == 200)
        #expect(r.first { $0.account == "530000" }?.credit == 2300)
        #expect(balancedPerEntry(r))
    }

    @Test(arguments: [false, true])
    func theTipGoesToTheTipsAccountSoTheEntryBalances(isVoid: Bool) {
        let sign = isVoid ? -1 : 1
        let r = rows(export([receipt(ttc: sign * 1100, ht: sign * 1000, tax: "{\"10.0\": \(sign * 100)}", void: isVoid, tenders: [(.creditCard, sign * 1300)])]))
        let tip = r.first { $0.account == "468600" }
        #expect((isVoid ? tip?.debit : tip?.credit) == 200)
        #expect(tip?.label.hasPrefix("Excedent de reglement / pourboire") == true)
        #expect(balancedPerEntry(r))
    }

    @Test func aNegativeTotalWithoutVoidLinkIsStillAVoid() {
        let r = rows(export([receipt(ttc: -1100, ht: -1000, tax: "{\"10\": -100}", tenders: [(.cash, -1100)])]))
        #expect(r.first { $0.account == "706100" }?.debit == 1000)
        #expect(r.first { $0.account == "530000" }?.credit == 1100)
    }

    @Test(arguments: [false, true])
    func aReceiptWithoutTendersSettlesToCash(isVoid: Bool) {
        let sign = isVoid ? -1 : 1
        let r = rows(export([receipt(ttc: sign * 1100, ht: sign * 1000, tax: "{\"10.0\": \(sign * 100)}", void: isVoid, tenders: [])]))
        let cash = r.first { $0.account == "530000" }
        #expect(cash?.label == "Reglement Caisse POS01-000001")
        #expect((isVoid ? cash?.credit : cash?.debit) == 1100)
        #expect((isVoid ? cash?.debit : cash?.credit) == 0)
        #expect(balancedPerEntry(r))
    }

    @Test func aZeroRateGroupHasNoVatLine() {
        let r = rows(export([receipt(ttc: 1700, ht: 1500, tax: "{\"0\": 0, \"20\": 200}", tenders: [(.creditCard, 1700)])]))
        #expect(r.map(\.account) == ["706000", "706200", "445720", "512000"])
        #expect(r[0].label == "Vente POS01-000001 (HT 0%)")
    }

    @Test func roomChargeSettlesToTheRoomAccount() {
        let r = rows(export([receipt(ttc: 600, ht: 500, tax: "{\"20\":100}", tenders: [(.roomCharge, 600)])]))
        #expect(r.last == Row(num: "0000001", account: "411000", debit: 600, credit: 0, label: "Reglement RoomCharge POS01-000001"))
    }

    @Test func periodBoundsAreInclusiveSwappedWhenReversedAndNumberingRestarts() throws {
        let first = try #require(SQLDate.parse("2026-09-01T20:45:00.000Z"))
        let last = try #require(SQLDate.parse("2026-09-03T09:00:00.000Z"))
        let receipts = [
            receipt("A-1", at: first.addingTimeInterval(-0.001), ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-2", at: first, ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-3", at: last, ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-4", at: last.addingTimeInterval(0.001), ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
        ]
        let result = LocalFecWriter.export(receipts: receipts, from: last, to: first, siren: " 987654321 ")
        let text = String(decoding: result.data, as: UTF8.self)
        #expect(result.fileName == "987654321FEC20260903.txt")
        #expect(Set(rows(result).map(\.num)) == ["0000001", "0000002"])
        #expect(text.contains("\tA-2\t") && text.contains("\tA-3\t"))
        #expect(!text.contains("\tA-1\t") && !text.contains("\tA-4\t"))
    }

    @Test func anEmptyPeriodGivesTheHeaderOnly() {
        let result = LocalFecWriter.export(receipts: [], from: noon, to: noon, siren: "")
        #expect(result.data == Data((LocalFecFormat.header + "\n").utf8))
        #expect(result.fileName == "000000000FEC20260903.txt")
        #expect(result.totalRecords == 0)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFecWriterTests`
Expected: FAIL à la compilation (`LocalFecWriter` inconnu).

- [ ] **Step 3: Implémenter**

```swift
import Foundation

/// Règlement d'un reçu : moyen (valeur entière de `PaymentTenders.Method`) et montant réglé (`Amount`, pas le montant remis).
struct FecTender: Equatable {
    let method: Int
    let amountCents: Int
}

/// Reçu tel que le FEC le lit : colonnes de `FiscalReceipts`, règlements dans l'ordre d'enregistrement.
struct FecReceipt: Equatable {
    let receiptNumber: String
    let createdAtUtc: Date
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
    let htBreakdownJson: String?
    /// `VoidedReceiptId` renseigné : reçu d'annulation.
    let voidsReceipt: Bool
    let tenders: [FecTender]
}

/// Fichier FEC et totaux, comme `FecExportResult`.
struct FecExport: Equatable {
    let fileName: String
    let data: Data
    let totalRecords: Int
    let totalDebitCents: Int
    let totalCreditCents: Int
}

/// Miroir de `FecExportService.GenerateFecAsync` : une écriture par reçu de la période (bornes incluses, inversées si besoin), dans l'ordre
/// des reçus. Vente : HT par taux puis TVA au crédit, règlements au débit, excédent de règlement (pourboire) au crédit du 468600.
/// Annulation (`VoidedReceiptId` ou TTC négatif) : mêmes lignes en valeur absolue, sens inversé.
/// Fins de ligne `\n` (`AppendLine` du serveur macOS/Linux, dernière ligne comprise), UTF-8 sans BOM.
enum LocalFecWriter {
    static func export(receipts: [FecReceipt], from: Date, to: Date, siren: String) -> FecExport {
        let (start, end) = from > to ? (to, from) : (from, to)
        var lines = [LocalFecFormat.header]
        var totalDebit = 0
        var totalCredit = 0
        let selected = receipts.filter { $0.createdAtUtc >= start && $0.createdAtUtc <= end }
        for (index, receipt) in selected.enumerated() {
            let entry = LocalFecFormat.entryNumber(index + 1)
            let date = LocalFecFormat.date(receipt.createdAtUtc)
            let piece = receipt.receiptNumber
            let isVoid = receipt.voidsReceipt || receipt.ttcCents < 0
            func write(_ account: FecAccount, _ label: String, debit: Int, credit: Int) {
                lines.append(LocalFecFormat.line(entry: entry, date: date, account: account, pieceRef: piece, label: label, debit: debit, credit: credit))
                totalDebit += debit
                totalCredit += credit
            }

            let items = LocalFecBreakdown.items(
                ttcCents: receipt.ttcCents, htCents: receipt.htCents, taxBreakdownJson: receipt.taxBreakdownJson, htBreakdownJson: receipt.htBreakdownJson
            )
            for item in items {
                let ht = abs(item.htCents)
                let vat = abs(item.vatCents)
                if ht <= 0 && vat <= 0 { continue }
                let rate = LocalFecFormat.rateLabel(item.rate)
                write(LocalFecFormat.salesAccount(rate: item.rate), "Vente \(piece) (HT \(rate)%)", debit: isVoid ? ht : 0, credit: isVoid ? 0 : ht)
                if vat > 0 {
                    write(LocalFecFormat.vatAccount(rate: item.rate), "TVA Collectee \(rate)% \(piece)", debit: isVoid ? vat : 0, credit: isVoid ? 0 : vat)
                }
            }

            if receipt.tenders.isEmpty {
                let amount = abs(receipt.ttcCents)
                write(LocalFecFormat.cashFallbackAccount, "Reglement Caisse \(piece)", debit: isVoid ? 0 : amount, credit: isVoid ? amount : 0)
            } else {
                for tender in receipt.tenders {
                    let amount = abs(tender.amountCents)
                    let label = "Reglement \(LocalFecFormat.methodName(tender.method)) \(piece)"
                    write(LocalFecFormat.paymentAccount(method: tender.method), label, debit: isVoid ? 0 : amount, credit: isVoid ? amount : 0)
                }
                // Excédent de règlement (pourboire) : encaissé hors TTC du reçu, dette et non produit ; sans lui l'écriture serait déséquilibrée.
                let tip = receipt.tenders.reduce(0) { $0 + abs($1.amountCents) } - abs(receipt.ttcCents)
                if tip > 0 {
                    write(LocalFecFormat.tipsAccount, "Excedent de reglement / pourboire \(piece)", debit: isVoid ? tip : 0, credit: isVoid ? 0 : tip)
                }
            }
        }
        let text = lines.map { $0 + "\n" }.joined()
        return FecExport(
            fileName: LocalFecFormat.fileName(siren: siren, end: end), data: Data(text.utf8), totalRecords: lines.count - 1,
            totalDebitCents: totalDebit, totalCreditCents: totalCredit
        )
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalFecWriterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFecWriter.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecWriterTests.swift
git commit -m "feat(ios): écritures du FEC (ventes, TVA, règlements, excédent au 468600)"
```

---

### Task 5: `LocalFecExporter` sur SQLite, rejeu octet pour octet de la fixture

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFecExporter.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecReferenceTests.swift`

**Interfaces:**
- Consumes: `fec_reference.json` (Task 1), `LocalFecWriter.export(receipts:from:to:siren:) -> FecExport` et `FecReceipt` / `FecTender` (Task 4), `SQLiteDatabase.query`, `SQLRow.string/int/date`, `ContractDecodingTests.fixture(_:)`.
- Produces:
  ```swift
  struct LocalFecExporter {
      let db: SQLiteDatabase
      func export(from: Date, to: Date, siren: String) throws -> FecExport
  }
  ```
  Lève `APIError.server(status: 500, …)` si un `CreatedAtUtc` est illisible (un reçu n'est jamais omis en silence).

- [ ] **Step 1: Écrire les tests qui échouent**

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("FEC : parité octet pour octet avec la fixture .NET")
struct LocalFecReferenceTests {
    private struct Reference: Decodable {
        var receipts: [Receipt]
        var exports: [Export]
    }

    private struct Receipt: Decodable {
        var id: String
        var receiptNumber: String
        var sequenceNumber: Int
        var createdAtUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxBreakdownJson: String
        var htBreakdownJson: String?
        var voidedReceiptId: String?
        var tenders: [Tender]
    }

    private struct Tender: Decodable {
        var id: String
        var method: Int
        var amountCents: Int
        var tenderedCents: Int
        var changeGivenCents: Int
    }

    private struct Export: Decodable {
        var name: String
        var fromUtc: String
        var toUtc: String
        var siren: String
        var fileName: String
        var totalRecords: Int
        var totalDebitCents: Int
        var totalCreditCents: Int
        var fecText: String
        var fecBase64: String
    }

    private static func reference() throws -> Reference {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fec_reference"))
    }

    /// Base migrée (sans amorçage de démonstration) contenant exactement les reçus et règlements de la fixture, dans son ordre.
    private static func seededDatabase(_ reference: Reference) throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        for r in reference.receipts {
            try db.run(
                """
                INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                    PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId, HtBreakdownJson)
                VALUES (?, 'T01', ?, ?, ?, ?, ?, ?, '', '', ?, 0, ?, ?)
                """,
                [.text(r.id), .text(r.receiptNumber), .uuid(UUID()), .integer(r.sequenceNumber), .integer(r.ttcCents), .integer(r.htCents),
                 .text(r.taxBreakdownJson), .text(r.createdAtUtc), .string(r.voidedReceiptId), .string(r.htBreakdownJson)]
            )
            for t in r.tenders {
                try db.run(
                    "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                    [.text(t.id), .text(r.id), .integer(t.method), .integer(t.amountCents), .integer(t.tenderedCents), .integer(t.changeGivenCents)]
                )
            }
        }
        return db
    }

    @Test func everyExportOfTheDotNetFixtureIsByteIdentical() throws {
        let reference = try Self.reference()
        let exporter = LocalFecExporter(db: try Self.seededDatabase(reference))
        for e in reference.exports {
            let context = Comment(rawValue: e.name)
            let from = try #require(SQLDate.parse(e.fromUtc), context)
            let to = try #require(SQLDate.parse(e.toUtc), context)
            let result = try exporter.export(from: from, to: to, siren: e.siren)
            #expect(result.fileName == e.fileName, context)
            #expect(String(decoding: result.data, as: UTF8.self) == e.fecText, context) // diff lisible en cas d'écart
            #expect(result.data == Data(base64Encoded: e.fecBase64), context)       // preuve octet pour octet
            #expect(result.totalRecords == e.totalRecords, context)
            #expect(result.totalDebitCents == e.totalDebitCents, context)
            #expect(result.totalCreditCents == e.totalCreditCents, context)
        }
    }

    @Test func theFixtureCoversTheReviewFocusCases() throws {
        let reference = try Self.reference()
        #expect(reference.exports.count == 4)
        let year = try #require(reference.exports.last).fecText
        #expect(year.contains("\t468600\t"))                         // pourboire et sa contre-passation
        #expect(year.contains("\t411000\t"))                         // facturation chambre (iPad)
        #expect(year.contains("Reglement Caisse T01-000010"))        // reçu sans règlement
        #expect(year.contains("\t20260902\t706550\tVentes Restauration A Emporter 5.5%\t\t\tT01-000005\t")) // 23 h 30 UTC daté du jour UTC
        #expect(!year.contains("\r"))
        let reversed = reference.exports[1]
        #expect(reversed.fileName == "987654321FEC20260903.txt")
    }

    @Test func anUnreadableReceiptDateStopsTheExportInsteadOfDroppingTheReceipt() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid)
            VALUES (?, 'T01', 'T01-000001', ?, 1, 1100, 1000, '{"10":100}', '', '', 'pas une date', 0)
            """,
            [.uuid(UUID()), .uuid(UUID())]
        )
        #expect(throws: APIError.self) {
            _ = try LocalFecExporter(db: db).export(from: .distantPast, to: .distantFuture, siren: "123456789")
        }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFecReferenceTests`
Expected: FAIL à la compilation (`LocalFecExporter` inconnu).

- [ ] **Step 3: Implémenter**

```swift
import Foundation

/// Export FEC du mode autonome : lit tous les reçus (`FiscalReceipts`, ordre de séquence) et leurs règlements (`PaymentTenders`, ordre
/// d'enregistrement), puis délègue à `LocalFecWriter`. Comme le .NET, aucun filtre de poste (le poste autonome est seul, `T01`).
/// ponytail: tous les reçus sont chargés en mémoire, comme `FecExportService` ; quelques milliers par mois tiennent sans peine.
/// Filtrer par date en SQL si une base dépasse quelques centaines de milliers de reçus.
struct LocalFecExporter {
    let db: SQLiteDatabase

    func export(from: Date, to: Date, siren: String) throws -> FecExport {
        var tenders: [String: [FecTender]] = [:]
        for row in try db.query("SELECT FiscalReceiptId, Method, Amount FROM PaymentTenders ORDER BY rowid") {
            let receiptId = (row.string("FiscalReceiptId") ?? "").uppercased()
            tenders[receiptId, default: []].append(FecTender(method: row.int("Method") ?? 0, amountCents: row.int("Amount") ?? 0))
        }
        let receipts = try db.query(
            """
            SELECT Id, ReceiptNumber, CreatedAtUtc, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson, HtBreakdownJson, VoidedReceiptId
            FROM FiscalReceipts ORDER BY SequenceNumber, rowid
            """
        ).map { row throws -> FecReceipt in
            let number = row.string("ReceiptNumber") ?? ""
            guard let createdAt = row.date("CreatedAtUtc") else {
                throw APIError.server(status: 500, message: "Date illisible pour le reçu \(number) : export FEC impossible.")
            }
            return FecReceipt(
                receiptNumber: number, createdAtUtc: createdAt, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
                taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", htBreakdownJson: row.string("HtBreakdownJson"),
                voidsReceipt: row.string("VoidedReceiptId") != nil, tenders: tenders[(row.string("Id") ?? "").uppercased()] ?? []
            )
        }
        return LocalFecWriter.export(receipts: receipts, from: from, to: to, siren: siren)
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalFecReferenceTests`
Expected: PASS. En cas d'écart, le `#expect` sur `fecText` montre la première ligne différente : corriger le Swift (Tasks 2 à 4), jamais la fixture.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFecExporter.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecReferenceTests.swift
git commit -m "feat(ios): export FEC sur la base locale, identique octet pour octet au .NET"
```

---

### Task 6: API `LocalPosAPI.exportFec`, JET `FEC_EXPORTED`, suivi et documentation

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fec.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` (retirer la ligne `exportFec`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift` (ajouter `fecExported`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecAPITests.swift`
- Create: `docs/plans/ipad-standalone-3c-progress.md`
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift »)

**Interfaces:**
- Consumes: `LocalFecExporter(db:).export(from:to:siren:) -> FecExport` (Task 5), `LocalFiscalRepository.appendJournal(eventType:payloadJson:terminalId:operatorId:now:)`, `FiscalJSON.object/string/dateTime`, `requireManager() -> StaffMember`, `clock()`, `db.transaction`.
- Produces: `public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data)` sur `LocalPosAPI` (signature de `PosAPI`, inchangée) ; `LocalJournalEvents.fecExported = "FEC_EXPORTED"`.

- [ ] **Step 1: Écrire les tests qui échouent**

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : export FEC")
struct LocalFecAPITests {
    private struct Row {
        let num: String
        let account: String
        let debit: Int
        let credit: Int
    }

    /// API sur fichier pilotée par une horloge de test, avec une seconde connexion pour relire les tables.
    private func makeAPIWithDatabase(pin: String = "1234") async throws -> (LocalPosAPI, SQLiteDatabase, LocalTestClock) {
        let clock = LocalTestClock("2026-10-12T12:00:00Z")
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path), clock)
    }

    private func rows(_ data: Data) -> [Row] {
        String(decoding: data, as: UTF8.self).split(separator: "\n").dropFirst().map { line in
            let c = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            return Row(num: c[2], account: c[4], debit: Int(c[11].replacingOccurrences(of: ".", with: "")) ?? -1,
                       credit: Int(c[12].replacingOccurrences(of: ".", with: "")) ?? -1)
        }
    }

    @Test func realSalesExportBalancedEntriesAndTheExportIsJournaled() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        let burger = try await localProduct(api, "Burger Gourmet Rossini") // 19,50 €
        try await seatTable(api, "T1", items: [localInput(burger)])
        let first = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.pay(localPayment(first, table: "T1", tenders: [localTender(.creditCard, 2150)], tip: 200))
        clock.advance(600)
        try await seatTable(api, "T2", items: [localInput(burger)])
        let second = try #require(try await api.activeOrder(table: "T2"))
        _ = try await api.pay(localPayment(second, table: "T2", tenders: [localTender(.cash, 1950)]))
        let secondReceipt = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE ReceiptNumber = 'T01-000002'").first?.uuid("Id"))
        clock.advance(600)
        _ = try await api.voidReceipt(receiptId: secondReceipt)

        let from = try #require(SQLDate.parse("2026-10-01T00:00:00.000Z"))
        let (fileName, data) = try await api.exportFec(from: from, to: clock.now, siren: "123456789")

        #expect(fileName == "123456789FEC20261012.txt")
        let r = rows(data)
        #expect(Set(r.map(\.num)) == ["0000001", "0000002", "0000003"])
        for num in Set(r.map(\.num)) {
            let entry = r.filter { $0.num == num }
            #expect(entry.reduce(0) { $0 + $1.debit } == entry.reduce(0) { $0 + $1.credit }, "écriture \(num) déséquilibrée")
        }
        #expect(r.contains { $0.num == "0000001" && $0.account == "468600" && $0.credit == 200 })
        #expect(r.contains { $0.num == "0000003" && $0.account == "530000" && $0.credit == 1950 })

        let journal = try db.query("SELECT PayloadJson, TerminalId, OperatorId FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'")
        #expect(journal.count == 1)
        #expect(journal.first?.string("PayloadJson")
            == "{\"StartDateUtc\":\"2026-10-01T00:00:00+00:00\",\"EndDateUtc\":\"2026-10-12T12:20:00+00:00\",\"Siren\":\"123456789\",\"Company\":null,\"FileName\":\"123456789FEC20261012.txt\"}")
        #expect(journal.first?.string("TerminalId") == nil)
        #expect(journal.first?.uuid("OperatorId") != nil)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func anEmptyPeriodWithoutSirenGivesTheHeaderOnly() async throws {
        let (api, _, clock) = try await makeAPIWithDatabase()
        let (fileName, data) = try await api.exportFec(from: clock.now, to: clock.now, siren: "")
        #expect(fileName == "000000000FEC20261012.txt")
        #expect(data == Data((LocalFecFormat.header + "\n").utf8))
    }

    @Test func aWaiterCannotExportTheFecAndNothingIsJournaled() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase(pin: "2468")
        await #expect(throws: APIError.self) {
            _ = try await api.exportFec(from: clock.now.addingTimeInterval(-86_400), to: clock.now, siren: "123456789")
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
    }
}
```

`LocalFiscalRepository.verifyChains() throws -> [String]` (ligne 479) renvoie la liste des ruptures, vide si les chaînes (dont le JET) sont intactes.

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFecAPITests`
Expected: FAIL (`501 Non disponible en mode autonome : exportFec(from:to:siren:)` levé par le stub).

- [ ] **Step 3: Implémenter**

Dans `LocalPosAPI+Unsupported.swift`, supprimer la ligne :

```swift
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) { throw unsupported() }
```

Dans `LocalJournalEvents.swift`, ajouter après `periodClosure` :

```swift
    static let fecExported = "FEC_EXPORTED"
```

Créer `LocalPosAPI+Fec.swift` :

```swift
import Foundation

extension LocalPosAPI {
    /// Export FEC (`GET /api/fiscal/fec`) : fichier identique au .NET pour les mêmes reçus, entrée JET `FEC_EXPORTED` dans la même
    /// transaction. Réservé aux responsables. Charge utile du JET comme l'endpoint : dates telles que reçues (avant inversion), SIREN tel
    /// que saisi, `Company` nul (pas de paramètre société côté iPad, comme un appel HTTP sans `company`).
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {
        let manager = try requireManager()
        let fiscal = fiscalRepository, exporter = LocalFecExporter(db: db)
        let now = clock()
        return try db.transaction { () throws -> (fileName: String, data: Data) in
            let result = try exporter.export(from: from, to: to, siren: siren)
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.fecExported,
                payloadJson: FiscalJSON.object([
                    ("StartDateUtc", FiscalJSON.dateTime(from)),
                    ("EndDateUtc", FiscalJSON.dateTime(to)),
                    ("Siren", FiscalJSON.string(siren)),
                    ("Company", "null"),
                    ("FileName", FiscalJSON.string(result.fileName)),
                ]),
                terminalId: nil, operatorId: manager.id, now: now
            )
            return (result.fileName, result.data)
        }
    }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter LocalFecAPITests`
Expected: PASS.

- [ ] **Step 5: Suites complètes**

Run: `cd ios/Packages/PosKit && swift test` puis, à la racine, `dotnet test RestaurantPos.slnx`
Expected: tout PASS ; relever les nombres de tests (PosKit, .NET).

- [ ] **Step 6: Écrire le suivi**

Créer `docs/plans/ipad-standalone-3c-progress.md` sur le modèle de `docs/plans/ipad-standalone-3a-progress.md` :

```markdown
# iPad standalone 3c — suivi d'exécution (export FEC)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-14-ipad-standalone-3c-fec.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `fec_reference.json` | fait | <n> | <n> | - | aucun |
| 2 Formats et comptes (`LocalFecFormat`) | fait | <n> | <n> | - | aucun |
| 3 Ventilation HT / TVA (`LocalFecBreakdown`) | fait | <n> | <n> | - | aucun |
| 4 Écritures (`LocalFecWriter`) | fait | <n> | <n> | - | aucun |
| 5 Export SQLite + rejeu octet pour octet | fait | <n> | <n> | - | aucun |
| 6 API, JET `FEC_EXPORTED`, documentation | fait | <n> | <n> | - | aucun |
```

Remplacer chaque `<n>` par le nombre relevé après la tâche correspondante (l'exécutant met ce fichier à jour à la fin de **chaque** tâche, pas seulement ici ; la consigne du `CLAUDE.md` du dépôt l'impose).

- [ ] **Step 7: Compléter `CLAUDE.md`**

Dans le paragraphe « Parité fiscale .NET/Swift », après la phrase sur `period_closure_reference.json`, ajouter : « Le FEC est porté dans `LocalFecFormat`, `LocalFecBreakdown`, `LocalFecWriter` et `LocalFecExporter` (`LocalPosAPI.exportFec`, JET `FEC_EXPORTED`) ; `fec_reference.json` est **généré par le .NET** (`WRITE_FEC_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FecReferenceFixtureTests`, sous macOS ou Linux car `AppendLine` y écrit `\n`) et rejoué octet pour octet par `LocalFecReferenceTests` ; toute modification de `FecExportService` impose de la régénérer. Les dates d'écriture du FEC sont celles du jour UTC des reçus, des deux côtés. »

- [ ] **Step 8: Commit, puis revue de branche**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fec.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift \
        ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecAPITests.swift \
        docs/plans/ipad-standalone-3c-progress.md CLAUDE.md
git commit -m "feat(ios): export FEC en mode autonome, JET FEC_EXPORTED ; suivi du lot 3c"
```

Lancer ensuite la revue de la branche (`superpowers:requesting-code-review`) avant de fusionner.

---

## Écarts connus et risques (non couverts par la fixture)

- **Fin de ligne.** Le .NET écrit `Environment.NewLine` : `\r\n` si le serveur tournait sous Windows. L'iPad écrit toujours `\n` (serveur de référence macOS/Linux) ; la fixture doit être générée sous macOS ou Linux.
- **Date d'écriture.** Le .NET formate `CreatedAtUtc` dans son décalage stocké. Un reçu enregistré avec un décalage non nul (ex. reçu synchronisé par `/api/sync/receipt` avec une date locale) serait daté du jour local côté .NET ; l'iPad écrit toujours en UTC, ce qui est son propre cas.
- **Ordre des règlements.** Le `Include(Tenders)` du .NET n'a pas de `ORDER BY` (ordre de la clé UUIDv7 en pratique, donc d'insertion) ; l'iPad lit `ORDER BY rowid` (ordre d'insertion).
- **Charge utile du JET.** Non comparée au .NET par une fixture (horodatages et identifiants différents) ; seule sa forme est testée. `Company` est toujours nul côté iPad.
- **Clés de taux exotiques.** `decimal.TryParse(NumberStyles.Any)` et un JSON à clés dupliquées (dernière valeur gagnante côté `System.Text.Json`) ne sont pas reproduits ; ni le .NET ni l'iPad n'écrivent ces formes (`ponytail:` dans `LocalFecBreakdown.rate`).
- **Mémoire.** Tous les reçus sont chargés, comme au .NET (`ponytail:` dans `LocalFecExporter`, conforme au risque noté dans le spec).
- **Interface.** L'écran FEC du mode autonome (dates, SIREN, partage) appartient au lot 3d ; `FiscalStore.exportFec` fonctionne déjà avec `LocalPosAPI` dès la Task 6.
