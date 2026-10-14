# iPad autonome 3b : archives fiscales Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le mode autonome crée, liste, exporte et vérifie les archives fiscales d'une clôture de période comme `FiscalArchiveService` du .NET : même sélection, mêmes cinq fichiers JSON, même chaîne de signatures, mêmes résultats de vérification.

**Architecture:** Une table `FiscalArchives` (migration V9, mêmes colonnes que `Program.cs`) et quatre briques PosKit : `ZipStoreWriter` / `ZipStoreReader` (ZIP « stocké », CRC32, Foundation seule), `IndentedJSON` (sortie identique à `JsonSerializer` avec `WriteIndented = true`), `LocalFiscalRepository+ArchiveContent` (sélection et sérialisation des cinq fichiers) et `LocalFiscalRepository+Archives` (chaîne globale, JET, liste, vérification). `LocalPosAPI+Archives` remplace les trois stubs de `LocalPosAPI+Unsupported.swift` et ajoute `archiveFile(id:)` (hors protocole, pour le partage du lot 3d). La preuve est une fixture **générée par le .NET** (`archive_reference.json`) : données d'entrée, texte exact des quatre fichiers de données et du manifeste (identifiant et horodatage masqués), SHA-256, vecteurs de signature. Le contenu JSON est donc comparé **octet pour octet** (le spec n'exigeait que la forme) ; seuls le conteneur ZIP, les identifiants et les horodatages diffèrent.

**Tech Stack:** Swift 6 / Swift Testing / SQLite / CryptoKit (PosKit) ; .NET 9 / xUnit / EF InMemory / `System.IO.Compression` pour la fixture ; `/usr/bin/unzip` comme lecteur ZIP indépendant (macOS).

**Spec:** `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` (lot 3b, Décisions, Risques). Code de référence : `src/RestaurantPos.Infrastructure/Services/FiscalArchiveService.cs`, `FiscalHashing.cs` (`ComputeArchiveHash`), `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs` (routes `/api/fiscal/archives*`).

## Global Constraints

- Branche de départ : `fix/debut-periode-z` (lot 3a fait, HEAD `2a82e19`). Nouvelle migration **V9** (`schemaV9`), jamais de modification d'un script publié ; triggers `NoUpdate` / `NoDelete`.
- Table **`FiscalArchives`**, colonnes de `src/RestaurantPos.Api/Program.cs` lignes 327-342, index uniques `IX_FiscalArchives_PeriodClosureId` et `IX_FiscalArchives_ArchiveSequence`.
- Poste unique `T01` (`LocalPosAPI.standaloneTerminalId`), pas de `POS_MAIN_TERM`.
- Les cinq fichiers : `receipts.json`, `closures.json`, `period-closures.json`, `journal.json`, `manifest.json`, dans cet ordre dans le ZIP. Propriétés PascalCase dans l'ordre de déclaration des entités .NET pour les quatre premiers ; manifeste en camelCase (type anonyme du .NET). Indentation 2 espaces, fin de ligne `\n`, UTF-8 sans BOM, pas de saut de ligne final. Échappements de `FiscalJSON.string` (encodeur par défaut de `System.Text.Json`).
- `Money` sérialisé `{ "AmountInCents": n, "Currency": "EUR" }` ; `Guid` en minuscules ; `DateTimeOffset` via `FiscalJSON.dateTime` (décimales sans zéros de fin, `+00:00`) ; enums en entier.
- Nom de fichier `archive-{poste}-{M|A}-{clé}.zip`, caractères hors `[A-Za-z0-9_.-]` remplacés par `_`.
- Signature `ComputeArchiveHash` : `previousHash|periodType|periodKey|fileSha256|createdAtUtc:O`, hexadécimal minuscule, chaîne **globale** depuis `FiscalHashing.genesisHash`.
- JET `ARCHIVE_CREATED` (poste de la clôture, opérateur = responsable) et `ARCHIVE_EXPORTED` (poste et opérateur nuls), charges utiles camelCase dans l'ordre du .NET.
- Dates fiscales écrites par `FiscalHashing.storedText`, jamais `SQLDate.format` ; horodatage de création arrondi à la milliseconde (`FiscalHashing.roundedToMilliseconds`).
- Toutes les méthodes d'archive sont réservées aux responsables (`requireManager`, comme la politique `RequireManagerOrAdmin` du groupe `/api/fiscal`). Messages d'erreur en français.
- ZIP écrit d'abord en `{archiveId}.tmp` dans le dossier d'archives, renommé après le COMMIT ; supprimé si la transaction échoue.
- Après chaque tâche : mettre à jour `docs/plans/ipad-standalone-3b-progress.md` (statut, nombres de tests .NET / PosKit, findings ouverts).
- Tests : `cd ios/Packages/PosKit && swift test` ; .NET : `dotnet test tests/RestaurantPos.Infrastructure.Tests`. `Directory.Build.props` traite les avertissements .NET en erreurs (lancer `dotnet format RestaurantPos.slnx` si besoin et annuler les reformatages hors sujet).

## Review Focus

- Reçu, Z ou entrée JET horodatés **exactement** à la fin d'une période mensuelle : présents dans l'archive de ce mois **et** dans celle du mois suivant (bornes incluses des deux côtés, comme le .NET) — fixture R5 / D3 / J4 (Task 1), rejouée en Task 5.
- Texte non ASCII ou avec guillemets (responsable « Hélène "Chef" », charge utile JET `café <1> & 'a'+b`) : échappé en `\uXXXX` exactement comme `System.Text.Json` — fixture (Task 1, Task 5).
- Fichier à vérifier vide, tronqué, qui n'est pas un ZIP, ou ZIP compressé (deflate, venant du serveur) : `unknown_archive` (ou 400 si vide côté API), jamais de plantage ni de lecture hors limites (Task 2, Task 6, Task 7).
- Archive refusée (déjà existante, clôture inconnue, serveur non responsable) : ni ligne `FiscalArchives`, ni entrée JET, ni fichier `.tmp` ou `.zip` laissé dans le dossier (Task 7).
- Tests en parallèle : chaque API de test a son propre dossier d'archives ; deux tests qui archivent `2026-09` ne s'écrasent pas (`archivesDirectory` injecté, défaut `<base>-archives/`) (Task 7).

Risques relevés en lisant le code, hors périmètre 3b (à consigner dans le suivi, pas à corriger ici) :
- `DailyFiscalClosures.CreatedAtUtc` existe sur l'iPad (V7) mais pas dans l'entité .NET `DailyFiscalClosure` : colonne propre à l'iPad, absente de `closures.json` (le .NET ne la sérialise pas). À ajouter à la liste des colonnes propres à l'iPad dans `CLAUDE.md`.
- .NET : `Include(r => r.Tenders)` sans tri ; en SQLite l'ordre des règlements dans `receipts.json` n'est pas garanti. L'iPad trie par `rowid` (ordre d'écriture).
- .NET : journal trié `ThenBy(j => j.Id)` ; l'ordre des `Guid` .NET diffère de l'ordre texte SQLite, mais seulement pour des entrées sans `ChainSequence` (historiques serveur). L'iPad n'en a pas.
- .NET : `JsonSerializerOptions.NewLine` vaut `Environment.NewLine` ; un serveur sous Windows produirait des fichiers en `\r\n` (forme identique, octets différents). La fixture normalise en `\n`.
- .NET : `ZipArchive` en lecture ne vérifie probablement pas le CRC (non confirmé) ; le lecteur Swift ne le vérifie pas non plus, pour rendre le même verdict.
- La sauvegarde chiffrée `.posbak` ne contient que la base : après restauration sur un autre iPad, les archives restent en base mais `archiveFile` répond 404. À traiter avec la restauration (sous-projet 5).

---

### Task 1: Fixture .NET `archive_reference.json`

**Files:**
- Create: `tests/RestaurantPos.Infrastructure.Tests/ArchiveReferenceFixtureTests.cs`
- Create (généré): `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/archive_reference.json`
- Create: `docs/plans/ipad-standalone-3b-progress.md`

**Interfaces:**
- Produces: le JSON suivant (clés camelCase), relu par Task 3 et Task 5.
  ```
  { "receipts": [ { "id": uuid, "terminalId": String, "receiptNumber": String, "orderId": uuid, "sequenceNumber": Int, "ttcCents": Int, "htCents": Int,
                    "taxBreakdownJson": String, "htBreakdownJson": String?, "previousSignatureHash": String, "signatureHash": String,
                    "createdAtUtc": String, "voidedReceiptId": uuid?,
                    "tenders": [ { "id": uuid, "method": Int, "amount": Int, "tendered": Int, "changeGiven": Int } ] } ],
    "dailyClosures": [ { "id", "terminalId", "closureSequence", "periodStartUtc", "periodEndUtc", "ttcCents", "htCents", "taxesSummaryJson",
                         "tenderTotalsJson", "perpetualGrandTotalCents", "previousSignatureHash", "signatureHash", "sealedByUserName", "sealedByUserId" } ],
    "periodClosures": [ { "id", "terminalId", "periodType": Int, "periodKey", "closureSequence", "periodStartUtc", "periodEndUtc", "ttcCents", "htCents",
                          "taxesSummaryJson", "tenderTotalsJson", "perpetualGrandTotalCents", "dailyClosureCount", "previousSignatureHash",
                          "signatureHash", "sealedByUserId", "sealedByUserName", "createdAtUtc" } ],
    "journalEntries": [ { "id", "localSequence", "terminalId"?, "occurredAtUtc", "idempotencyKey", "eventType", "payloadJson", "entryHash",
                          "chainSequence", "previousHash"?, "operatorId"? } ],
    "archives": [ { "periodClosureId": uuid, "fileName": String,
                    "files": { "receipts", "closures", "periodClosures", "journal", "manifest" : String },
                    "sha256": { "receipts", "closures", "periodClosures", "journal" : String } } ],
    "signatureVectors": [ { "previousHash", "periodType": Int, "periodKey", "fileSha256", "createdAtUtc", "signatureHash" } ] }
  ```
  Dates d'entrée au format `yyyy-MM-ddTHH:mm:ss.fffZ` (celui de `FiscalHashing.storedText`). Textes des fichiers normalisés en `\n`. Dans `files.manifest`, les valeurs de `archiveId` et `createdAtUtc` sont remplacées par `*`.

- [ ] **Step 1: Vérifier les noms réels côté .NET**

Run: `grep -n "public FiscalArchiveService" -A5 src/RestaurantPos.Infrastructure/Services/FiscalArchiveService.cs` et `grep -n "record FiscalArchiveDto" -A3 src/RestaurantPos.Application/Common/Interfaces/IFiscalArchiveService.cs`
Expected: constructeur `(AppDbContext, IFiscalJournal?, IConfiguration?, string? archivesDirectory)`, DTO avec `Id` et `FileName`. Si un nom diffère, adapter le test, jamais le service.

- [ ] **Step 2: Écrire le test générateur**

Le test amorce une base EF InMemory avec des données déterministes, appelle le vrai `FiscalArchiveService.CreateArchiveAsync` pour la mensuelle `2025-03` puis l'annuelle `2025`, ouvre chaque ZIP, contrôle les compteurs du manifeste, et écrit la fixture si `WRITE_ARCHIVE_FIXTURE=1` (sinon la compare). Les données couvrent : bornes exactes de début et de fin, autre poste (`T02`), annulation, millisecondes non nulles, champs nuls, texte non ASCII et guillemets.

```csharp
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
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        if (Environment.GetEnvironmentVariable("WRITE_ARCHIVE_FIXTURE") == "1")
        {
            File.WriteAllText(FixturePath(), json);
        }

        File.ReadAllText(FixturePath()).ReplaceLineEndings().Should().Be(json.ReplaceLineEndings(),
            "la fixture iPad doit refléter le .NET ; régénérer avec WRITE_ARCHIVE_FIXTURE=1");
    }
}
```

- [ ] **Step 3: Générer la fixture**

Run: `WRITE_ARCHIVE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter ArchiveReferenceFixtureTests`
Expected: PASS ; `archive_reference.json` existe. Contrôle à l'œil dans `files.receipts` de la mensuelle : `"TotalTtcAmount": {` suivi de `"AmountInCents"` et `"Currency": "EUR"`, `"CreatedAtUtc": "2025-03-15T12:34:56.12+00:00"`, `"IsVoid": false`, `"HtBreakdownJson": null` pour R4, `"Tenders": []` pour R5, guids en minuscules ; dans `files.journal`, `\u00E9`, `\u003C`, `\u0026`, `\u0027`, `\u002B` et `"TerminalId": null`. **Ces textes font foi** : si l'un d'eux contredit les Global Constraints (forme de `Money`, format des dates, `IsVoid` présent), c'est le Swift des tâches suivantes qu'il faut adapter, et la contrainte à corriger dans ce plan. Si un compteur échoue, revoir les données, jamais le service.

- [ ] **Step 4: Relancer sans régénération**

Run: `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter ArchiveReferenceFixtureTests`
Expected: PASS (fixture stable : identifiants d'archive et horodatages masqués).

- [ ] **Step 5: Créer le suivi**

`docs/plans/ipad-standalone-3b-progress.md` :

```markdown
# iPad standalone 3b — suivi d'exécution (archives fiscales)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-14-ipad-standalone-3b-archives.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `archive_reference.json` | fait | ? | ? | - | aucun |
| 2 ZIP stocké (`ZipStoreWriter` / `ZipStoreReader`) | à faire | | | - | |
| 3 Signature d'archive | à faire | | | - | |
| 4 Migration V9 (`FiscalArchives`) | à faire | | | - | |
| 5 Contenu de l'archive (`IndentedJSON`, sélection) | à faire | | | - | |
| 6 Dépôt : chaîne, liste, vérification | à faire | | | - | |
| 7 API `LocalPosAPI+Archives` | à faire | | | - | |
| 8 Vérification globale et documentation | à faire | | | - | |
```

Remplacer les `?` par les nombres de tests relevés (`dotnet test tests/RestaurantPos.Infrastructure.Tests` et `swift test`).

- [ ] **Step 6: Commit**

```bash
git add tests/RestaurantPos.Infrastructure.Tests/ArchiveReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/archive_reference.json docs/plans/ipad-standalone-3b-progress.md
git commit -m "test(fiscal): fixture .NET des archives fiscales pour l'iPad autonome"
```

---

### Task 2: ZIP « stocké » (`ZipStoreWriter`, `ZipStoreReader`, `CRC32`)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/ZipStore.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/ZipStoreTests.swift`

**Interfaces:**
- Produces:
  ```swift
  enum CRC32 { static func checksum(_ data: Data) -> UInt32 }
  enum ZipStoreWriter { static func archive(_ entries: [(name: String, data: Data)], modifiedAt: Date) -> Data }
  enum ZipStoreReader {
      static func names(in zip: Data) -> [String]?             // nil si ce n'est pas un ZIP lisible
      static func entry(named name: String, in zip: Data) -> Data? // nil si absente, compressée (méthode ≠ 0) ou hors limites
  }
  #if os(macOS)
  func runUnzip(_ arguments: [String]) throws -> (status: Int32, output: Data)   // dans ZipStoreTests.swift, réutilisée en Task 7
  #endif
  ```

- [ ] **Step 1: Écrire les tests qui échouent**

`ZipStoreTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("ZIP stocké (sans compression)")
struct ZipStoreTests {
    private static let entries: [(name: String, data: Data)] = [
        ("receipts.json", Data("[]".utf8)),
        ("journal.json", Data("[\n  {\n    \"A\": \"\\u00E9\"\n  }\n]".utf8)),
        ("vide.json", Data()),
    ]
    private static let date = SQLDate.parse("2026-10-14T09:30:42Z")!

    @Test func crc32MatchesTheStandardCheckValue() {
        #expect(CRC32.checksum(Data("123456789".utf8)) == 0xCBF4_3926)
        #expect(CRC32.checksum(Data()) == 0)
    }

    @Test func entriesAreReadBackByNameInOrder() {
        let zip = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        #expect(ZipStoreReader.names(in: zip) == ["receipts.json", "journal.json", "vide.json"])
        for entry in Self.entries {
            #expect(ZipStoreReader.entry(named: entry.name, in: zip) == entry.data)
        }
        #expect(ZipStoreReader.entry(named: "absent.json", in: zip) == nil)
    }

    @Test func theLayoutIsAStoredZipWithAUtcDosTimestamp() {
        let zip = [UInt8](ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date))
        #expect(Array(zip.prefix(4)) == [0x50, 0x4B, 0x03, 0x04])
        #expect(zip[8] == 0 && zip[9] == 0) // méthode 0 : stocké
        #expect(zip[10] == 0xD5 && zip[11] == 0x4B) // 09:30:42 → (9 << 11) | (30 << 5) | 21 = 0x4BD5
        #expect(zip[12] == 0x4E && zip[13] == 0x5D) // 2026-10-14 → (46 << 9) | (10 << 5) | 14 = 0x5D4E
        #expect(Array(zip.suffix(22).prefix(4)) == [0x50, 0x4B, 0x05, 0x06])
    }

    @Test func garbageAndTruncatedDataAreNotReadAndDoNotCrash() {
        #expect(ZipStoreReader.names(in: Data()) == nil)
        #expect(ZipStoreReader.entry(named: "manifest.json", in: Data("ceci n'est pas un zip, mais un texte assez long".utf8)) == nil)
        var truncated = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        truncated.removeLast(10)
        #expect(ZipStoreReader.entry(named: "receipts.json", in: truncated) == nil)
        var headless = ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date)
        headless.removeFirst(40) // décalages du répertoire central désormais hors limites
        #expect(ZipStoreReader.entry(named: "journal.json", in: headless) == nil)
    }

    #if os(macOS)
    @Test(.enabled(if: FileManager.default.isExecutableFile(atPath: "/usr/bin/unzip")))
    func anIndependentUnzipAcceptsAndExtractsTheArchive() throws {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("zip-\(UUID().uuidString).zip")
        try ZipStoreWriter.archive(Self.entries, modifiedAt: Self.date).write(to: url)
        defer { try? FileManager.default.removeItem(at: url) }
        #expect(try runUnzip(["-t", url.path]).status == 0)
        #expect(try runUnzip(["-p", url.path, "journal.json"]).output == Self.entries[1].data)
    }
    #endif
}

#if os(macOS)
/// Lance `/usr/bin/unzip`, lecteur ZIP indépendant de PosKit ; renvoie le code de sortie et la sortie standard brute.
func runUnzip(_ arguments: [String]) throws -> (status: Int32, output: Data) {
    let process = Process()
    process.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
    process.arguments = arguments
    let pipe = Pipe()
    process.standardOutput = pipe
    process.standardError = FileHandle.nullDevice
    try process.run()
    let output = pipe.fileHandleForReading.readDataToEndOfFile()
    process.waitUntilExit()
    return (process.terminationStatus, output)
}
#endif
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter ZipStoreTests`
Expected: erreur de compilation (`CRC32`, `ZipStoreWriter` absents).

- [ ] **Step 3: Implémenter**

`ZipStore.swift` :

```swift
import Foundation

/// CRC-32 IEEE 802.3 (polynôme réfléchi 0xEDB88320), celui du format ZIP.
enum CRC32 {
    private static let table: [UInt32] = (0..<256).map { index in
        var c = UInt32(index)
        for _ in 0..<8 { c = (c & 1) != 0 ? 0xEDB8_8320 ^ (c >> 1) : c >> 1 }
        return c
    }

    static func checksum(_ data: Data) -> UInt32 {
        var crc: UInt32 = 0xFFFF_FFFF
        for byte in data { crc = table[Int((crc ^ UInt32(byte)) & 0xFF)] ^ (crc >> 8) }
        return crc ^ 0xFFFF_FFFF
    }
}

/// Archive ZIP « stockée » (méthode 0, sans compression) : en-têtes locaux, répertoire central, fin de répertoire. Lisible par tout outil.
/// ponytail: ni ZIP64 ni compression ; limite 4 Gio et 65 535 entrées (une archive fiscale mensuelle pèse quelques Mo).
enum ZipStoreWriter {
    static func archive(_ entries: [(name: String, data: Data)], modifiedAt: Date) -> Data {
        let (time, date) = dosDateTime(modifiedAt)
        var body = ByteWriter(), central = ByteWriter()
        for entry in entries {
            let name = Array(entry.name.utf8)
            let size = UInt32(entry.data.count)
            let crc = CRC32.checksum(entry.data)
            let offset = UInt32(body.bytes.count)

            body.u32(0x0403_4B50); body.u16(20); body.u16(0); body.u16(0); body.u16(time); body.u16(date)
            body.u32(crc); body.u32(size); body.u32(size); body.u16(UInt16(name.count)); body.u16(0)
            body.raw(name); body.raw([UInt8](entry.data))

            central.u32(0x0201_4B50); central.u16(20); central.u16(20); central.u16(0); central.u16(0); central.u16(time); central.u16(date)
            central.u32(crc); central.u32(size); central.u32(size); central.u16(UInt16(name.count)); central.u16(0); central.u16(0)
            central.u16(0); central.u16(0); central.u32(0); central.u32(offset)
            central.raw(name)
        }
        let centralOffset = UInt32(body.bytes.count)
        body.raw(central.bytes)
        body.u32(0x0605_4B50); body.u16(0); body.u16(0); body.u16(UInt16(entries.count)); body.u16(UInt16(entries.count))
        body.u32(UInt32(central.bytes.count)); body.u32(centralOffset); body.u16(0)
        return Data(body.bytes)
    }

    /// Date et heure MS-DOS, en UTC (secondes paires).
    private static func dosDateTime(_ date: Date) -> (time: UInt16, date: UInt16) {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .gmt
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
        let year = min(max((c.year ?? 1980) - 1980, 0), 127)
        let time = ((c.hour ?? 0) << 11) | ((c.minute ?? 0) << 5) | ((c.second ?? 0) / 2)
        let day = (year << 9) | ((c.month ?? 1) << 5) | (c.day ?? 1)
        return (UInt16(time), UInt16(day))
    }
}

/// Lecture minimale d'un ZIP stocké, pour retrouver `manifest.json` à la vérification. Ne vérifie pas le CRC (le .NET non plus).
/// Toute incohérence (signature absente, décalage hors limites, entrée compressée) donne `nil`, jamais de plantage.
enum ZipStoreReader {
    private struct Entry {
        let name: String
        let method: UInt16
        let size: Int
        let localOffset: Int
    }

    static func names(in zip: Data) -> [String]? {
        centralEntries([UInt8](zip))?.map(\.name)
    }

    static func entry(named name: String, in zip: Data) -> Data? {
        let bytes = [UInt8](zip)
        guard let entry = centralEntries(bytes)?.first(where: { $0.name == name }), entry.method == 0,
              u32(bytes, entry.localOffset) == 0x0403_4B50,
              let nameLength = u16(bytes, entry.localOffset + 26), let extraLength = u16(bytes, entry.localOffset + 28)
        else { return nil }
        let start = entry.localOffset + 30 + Int(nameLength) + Int(extraLength)
        guard start + entry.size <= bytes.count else { return nil }
        return Data(bytes[start..<(start + entry.size)])
    }

    private static func centralEntries(_ bytes: [UInt8]) -> [Entry]? {
        guard bytes.count >= 22 else { return nil }
        let lowest = max(0, bytes.count - 22 - 0xFFFF)
        var end = bytes.count - 22
        while end >= lowest, u32(bytes, end) != 0x0605_4B50 { end -= 1 }
        guard end >= lowest, let count = u16(bytes, end + 10), let directory = u32(bytes, end + 16) else { return nil }
        var offset = Int(directory)
        var entries: [Entry] = []
        for _ in 0..<Int(count) {
            guard u32(bytes, offset) == 0x0201_4B50,
                  let method = u16(bytes, offset + 10), let size = u32(bytes, offset + 20),
                  let nameLength = u16(bytes, offset + 28), let extraLength = u16(bytes, offset + 30), let commentLength = u16(bytes, offset + 32),
                  let local = u32(bytes, offset + 42), offset + 46 + Int(nameLength) <= bytes.count
            else { return nil }
            let name = String(decoding: bytes[(offset + 46)..<(offset + 46 + Int(nameLength))], as: UTF8.self)
            entries.append(Entry(name: name, method: method, size: Int(size), localOffset: Int(local)))
            offset += 46 + Int(nameLength) + Int(extraLength) + Int(commentLength)
        }
        return entries
    }

    private static func u16(_ b: [UInt8], _ at: Int) -> UInt16? {
        guard at >= 0, at + 2 <= b.count else { return nil }
        return UInt16(b[at]) | UInt16(b[at + 1]) << 8
    }

    private static func u32(_ b: [UInt8], _ at: Int) -> UInt32? {
        guard at >= 0, at + 4 <= b.count else { return nil }
        return UInt32(b[at]) | UInt32(b[at + 1]) << 8 | UInt32(b[at + 2]) << 16 | UInt32(b[at + 3]) << 24
    }
}

/// Écriture petit-boutiste, sans longues chaînes de `+` (temps de vérification de types).
private struct ByteWriter {
    var bytes: [UInt8] = []

    mutating func u16(_ v: UInt16) { bytes.append(UInt8(v & 0xFF)); bytes.append(UInt8(v >> 8)) }
    mutating func u32(_ v: UInt32) { for shift in stride(from: 0, to: 32, by: 8) { bytes.append(UInt8((v >> UInt32(shift)) & 0xFF)) } }
    mutating func raw(_ b: [UInt8]) { bytes.append(contentsOf: b) }
}
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter ZipStoreTests`
Expected: PASS (le test `unzip` passe sur macOS ; il est ignoré si `/usr/bin/unzip` est absent).

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): ZIP stocké avec CRC32 et lecture du répertoire central"
```

---

### Task 3: Signature d'archive et SHA-256 de fichier

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift` (après `periodClosureHash`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalHashingTests.swift`

**Interfaces:**
- Consumes: `archive_reference.json` → `signatureVectors` (Task 1).
- Produces:
  ```swift
  FiscalHashing.archiveHash(previousHash: String, periodType: Int, periodKey: String, fileSha256: String, createdAt: Date) -> String // hex minuscule
  FiscalHashing.sha256Hex(_ data: Data) -> String // hex minuscule
  ```

- [ ] **Step 1: Écrire les tests qui échouent**

Dans la suite existante de `FiscalHashingTests.swift` :

```swift
    @Test func archiveHashMatchesTheDotNetVectors() throws {
        struct Vectors: Decodable {
            struct Vector: Decodable {
                var previousHash: String
                var periodType: Int
                var periodKey: String
                var fileSha256: String
                var createdAtUtc: String
                var signatureHash: String
            }
            var signatureVectors: [Vector]
        }
        let vectors = try JSONDecoder().decode(Vectors.self, from: ContractDecodingTests.fixture("archive_reference")).signatureVectors
        #expect(vectors.count == 2)
        for v in vectors {
            let createdAt = try #require(SQLDate.parse(v.createdAtUtc))
            #expect(FiscalHashing.archiveHash(
                previousHash: v.previousHash, periodType: v.periodType, periodKey: v.periodKey, fileSha256: v.fileSha256, createdAt: createdAt
            ) == v.signatureHash)
        }
    }

    @Test func archiveHashJoinsTheFieldsLikeTheDotNet() {
        let createdAt = SQLDate.parse("2026-01-05T09:00:01.120Z")!
        let raw = "\(FiscalHashing.genesisHash)|2|2025|abc|2026-01-05T09:00:01.1200000+00:00"
        #expect(FiscalHashing.archiveHash(previousHash: FiscalHashing.genesisHash, periodType: 2, periodKey: "2025", fileSha256: "abc", createdAt: createdAt)
            == FiscalHashing.sha256(raw, lowercase: true))
    }

    @Test func sha256HexIsLowercase() {
        #expect(FiscalHashing.sha256Hex(Data("abc".utf8)) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
    }
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter FiscalHashingTests`
Expected: erreur de compilation (`archiveHash`, `sha256Hex` absents).

- [ ] **Step 3: Implémenter**

Dans `FiscalHashing.swift`, après `periodClosureHash` :

```swift
    /// `ComputeArchiveHash` : hexadécimal minuscule, `periodType` entier, date de création au format « O ». Chaîne globale (toutes archives).
    static func archiveHash(previousHash: String, periodType: Int, periodKey: String, fileSha256: String, createdAt: Date) -> String {
        sha256("\(previousHash)|\(periodType)|\(periodKey)|\(fileSha256)|\(timestampText(createdAt))", lowercase: true)
    }

    /// SHA-256 d'octets bruts en hexadécimal minuscule (`Convert.ToHexStringLower(SHA256.HashData(…))`).
    static func sha256Hex(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }
```

- [ ] **Step 4: Vérifier**

Run: `swift test --filter FiscalHashingTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): signature des archives fiscales (miroir de ComputeArchiveHash)"
```

---

### Task 4: Migration V9 `FiscalArchives`

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV9.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift:5`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalSchemaTests.swift`

**Interfaces:**
- Produces: table `FiscalArchives` (colonnes de `Program.cs`), `LocalMigrator.schemaV9`, `LocalMigrator.steps.count == 9`.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalFiscalSchemaTests` :

```swift
    private static let insertArchive = """
    INSERT INTO FiscalArchives (Id, PeriodClosureId, PeriodType, PeriodKey, FileName, FileSha256, FileSizeBytes, ArchiveSequence,
        PreviousSignatureHash, SignatureHash, CreatedByUserId, CreatedAtUtc)
    VALUES ('A1', 'P1', 1, '2025-01', 'archive-T01-M-2025-01.zip', 'abc', 10, 1, 'GENESIS', 'SIG', 'U1', '2026-02-01T10:00:00.000Z')
    """

    @Test func migrationV9CreatesTheFiscalArchivesTable() throws {
        let db = try makeDB()
        #expect(LocalMigrator.steps.count >= 9)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE type = 'table' AND name = 'FiscalArchives'").count == 1)
    }

    @Test func fiscalArchivesAreAppendOnly() throws {
        let db = try makeDB()
        try db.run(Self.insertArchive)
        #expect(throws: (any Error).self) { try db.run("UPDATE FiscalArchives SET FileSha256 = 'x' WHERE Id = 'A1'") }
        #expect(throws: (any Error).self) { try db.run("DELETE FROM FiscalArchives WHERE Id = 'A1'") }
    }

    @Test func oneArchivePerClosureAndOneArchivePerSequenceNumber() throws {
        let db = try makeDB()
        try db.run(Self.insertArchive)
        #expect(throws: (any Error).self) { // même clôture, autre séquence
            try db.run(Self.insertArchive.replacingOccurrences(of: "'A1', 'P1'", with: "'A2', 'P1'").replacingOccurrences(of: ", 10, 1, ", with: ", 10, 2, "))
        }
        #expect(throws: (any Error).self) { // autre clôture, même séquence
            try db.run(Self.insertArchive.replacingOccurrences(of: "'A1', 'P1'", with: "'A3', 'P3'"))
        }
    }

    @Test func upgradingAV8DatabaseAddsTheFiscalArchivesTable() throws {
        let older = try SQLiteDatabase(path: ":memory:")
        for script in LocalMigrator.steps.prefix(8) { try older.exec(script) }
        try older.exec("PRAGMA user_version = 8")
        try LocalMigrator.migrate(older)
        #expect(try older.userVersion() == LocalMigrator.steps.count)
        #expect(try older.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'FiscalArchives'").count == 1)
    }
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFiscalSchemaTests`
Expected: FAIL (`no such table: FiscalArchives`).

- [ ] **Step 3: Créer la migration**

`LocalSchemaV9.swift` :

```swift
import Foundation

extension LocalMigrator {
    /// Archives fiscales NF525 : même table et mêmes colonnes que `FiscalArchives` dans `Program.cs` / `AppDbContext`. Chaîne globale (pas de poste).
    static let schemaV9 = """
    CREATE TABLE FiscalArchives (
        Id TEXT NOT NULL PRIMARY KEY,
        PeriodClosureId TEXT NOT NULL,
        PeriodType INTEGER NOT NULL,
        PeriodKey TEXT NOT NULL,
        FileName TEXT NOT NULL,
        FileSha256 TEXT NOT NULL,
        FileSizeBytes INTEGER NOT NULL,
        ArchiveSequence INTEGER NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        CreatedByUserId TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_FiscalArchives_PeriodClosureId ON FiscalArchives (PeriodClosureId);
    CREATE UNIQUE INDEX IX_FiscalArchives_ArchiveSequence ON FiscalArchives (ArchiveSequence);

    CREATE TRIGGER TR_FiscalArchives_NoUpdate BEFORE UPDATE ON FiscalArchives
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal archives are append-only and immutable.'); END;
    CREATE TRIGGER TR_FiscalArchives_NoDelete BEFORE DELETE ON FiscalArchives
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal archives are append-only and immutable.'); END;
    """
}
```

Dans `LocalMigrator.swift` ligne 5, ajouter `schemaV9` à la fin du tableau `steps`.

- [ ] **Step 4: Vérifier**

Run: `swift test`
Expected: PASS (toute la suite, y compris les tests qui comparent `steps.count`).

- [ ] **Step 5: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): migration V9, table FiscalArchives immuable"
```

---

### Task 5: Contenu de l'archive (`IndentedJSON`, sélection, cinq fichiers) rejoué sur la fixture

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/IndentedJSON.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+ArchiveContent.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+PeriodClosures.swift` (ajout `periodClosure(id:)`)
- Create (test): `ios/Packages/PosKit/Tests/PosKitTests/Local/ArchiveReferenceSupport.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveContentReferenceTests.swift`

**Interfaces:**
- Consumes: `FiscalJSON.string/guid/dateTime` (existants), `FiscalHashing.storedText`, `FiscalHashing.sha256Hex` (Task 3), `PeriodClosureRecord`, `FiscalPeriodType.storedValue` (3a), fixture (Task 1).
- Produces:
  ```swift
  enum IndentedJSON { case scalar(String), object([(String, IndentedJSON)]), array([IndentedJSON])
      var text: String
      static func string(_: String?) -> IndentedJSON; static func guid(_: UUID?) -> IndentedJSON; static func int(_: Int?) -> IndentedJSON
      static func bool(_: Bool) -> IndentedJSON; static func date(_: Date?) -> IndentedJSON; static func money(_ cents: Int?) -> IndentedJSON }
  struct ArchiveContent { let receipts, closures, periodClosures, journal, manifest: Data; var entries: [(name: String, data: Data)] }
  extension LocalFiscalRepository {
      func periodClosure(id: UUID) throws -> PeriodClosureRecord?
      func archiveContent(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> ArchiveContent
      static func archiveFileName(_ closure: PeriodClosureRecord) -> String
  }
  // Tests : struct ArchiveReference (Decodable) avec static func load() et func seed(_ db: SQLiteDatabase), func maskedManifest(_ text: String) -> String
  ```

- [ ] **Step 1: Écrire le support de fixture**

`ArchiveReferenceSupport.swift` :

```swift
import Foundation
@testable import PosKit

/// `archive_reference.json` (généré par le .NET, Task 1) : données d'entrée et fichiers d'archive attendus.
struct ArchiveReference: Decodable {
    struct Tender: Decodable {
        var id: UUID
        var method: Int
        var amount: Int
        var tendered: Int
        var changeGiven: Int
    }

    struct Receipt: Decodable {
        var id: UUID
        var terminalId: String
        var receiptNumber: String
        var orderId: UUID
        var sequenceNumber: Int
        var ttcCents: Int
        var htCents: Int
        var taxBreakdownJson: String
        var htBreakdownJson: String?
        var previousSignatureHash: String
        var signatureHash: String
        var createdAtUtc: String
        var voidedReceiptId: UUID?
        var tenders: [Tender]
    }

    struct Daily: Decodable {
        var id: UUID
        var terminalId: String
        var closureSequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesSummaryJson: String
        var tenderTotalsJson: String
        var perpetualGrandTotalCents: Int
        var previousSignatureHash: String
        var signatureHash: String
        var sealedByUserName: String
        var sealedByUserId: UUID
    }

    struct Period: Decodable {
        var id: UUID
        var terminalId: String
        var periodType: Int
        var periodKey: String
        var closureSequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesSummaryJson: String
        var tenderTotalsJson: String
        var perpetualGrandTotalCents: Int
        var dailyClosureCount: Int
        var previousSignatureHash: String
        var signatureHash: String
        var sealedByUserId: UUID
        var sealedByUserName: String
        var createdAtUtc: String
    }

    struct Journal: Decodable {
        var id: UUID
        var localSequence: Int
        var terminalId: String?
        var occurredAtUtc: String
        var idempotencyKey: String
        var eventType: String
        var payloadJson: String
        var entryHash: String
        var chainSequence: Int
        var previousHash: String?
        var operatorId: UUID?
    }

    struct Files: Decodable {
        var receipts: String
        var closures: String
        var periodClosures: String
        var journal: String
        var manifest: String?
    }

    struct Archive: Decodable {
        var periodClosureId: UUID
        var fileName: String
        var files: Files
        var sha256: Files
    }

    var receipts: [Receipt]
    var dailyClosures: [Daily]
    var periodClosures: [Period]
    var journalEntries: [Journal]
    var archives: [Archive]

    static func load() throws -> ArchiveReference {
        try JSONDecoder().decode(ArchiveReference.self, from: ContractDecodingTests.fixture("archive_reference"))
    }

    /// Insère toutes les lignes d'entrée telles que le .NET les a reçues (dates déjà au format `storedText`).
    func seed(_ db: SQLiteDatabase) throws {
        for r in receipts {
            try db.run(
                """
                INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                    HtBreakdownJson, PreviousSignatureHash, SignatureHash, CreatedAtUtc, VoidedReceiptId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(r.id), .text(r.terminalId), .text(r.receiptNumber), .uuid(r.orderId), .integer(r.sequenceNumber), .integer(r.ttcCents),
                 .integer(r.htCents), .text(r.taxBreakdownJson), .string(r.htBreakdownJson), .text(r.previousSignatureHash), .text(r.signatureHash),
                 .text(r.createdAtUtc), .uuid(r.voidedReceiptId)]
            )
            for t in r.tenders {
                try db.run(
                    "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                    [.uuid(t.id), .uuid(r.id), .integer(t.method), .integer(t.amount), .integer(t.tendered), .integer(t.changeGiven)]
                )
            }
        }
        for d in dailyClosures {
            try db.run(
                """
                INSERT INTO DailyFiscalClosures (Id, TerminalId, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalSalesTtc, TotalSalesHt,
                    TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, PreviousSignatureHash, SignatureHash, SealedByUserName, SealedByUserId, CreatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(d.id), .text(d.terminalId), .integer(d.closureSequence), .text(d.periodStartUtc), .text(d.periodEndUtc), .integer(d.ttcCents),
                 .integer(d.htCents), .text(d.taxesSummaryJson), .text(d.tenderTotalsJson), .integer(d.perpetualGrandTotalCents),
                 .text(d.previousSignatureHash), .text(d.signatureHash), .text(d.sealedByUserName), .uuid(d.sealedByUserId), .text(d.periodEndUtc)]
            )
        }
        for p in periodClosures {
            try db.run(
                """
                INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                    TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                    SealedByUserName, CreatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(p.id), .text(p.terminalId), .integer(p.periodType), .text(p.periodKey), .integer(p.closureSequence), .text(p.periodStartUtc),
                 .text(p.periodEndUtc), .integer(p.ttcCents), .integer(p.htCents), .text(p.taxesSummaryJson), .text(p.tenderTotalsJson),
                 .integer(p.perpetualGrandTotalCents), .integer(p.dailyClosureCount), .text(p.previousSignatureHash), .text(p.signatureHash),
                 .uuid(p.sealedByUserId), .text(p.sealedByUserName), .text(p.createdAtUtc)]
            )
        }
        for j in journalEntries {
            try db.run(
                """
                INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence,
                    PreviousHash, OperatorId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(j.id), .integer(j.localSequence), .string(j.terminalId), .text(j.occurredAtUtc), .text(j.idempotencyKey), .text(j.eventType),
                 .text(j.payloadJson), .text(j.entryHash), .integer(j.chainSequence), .string(j.previousHash), .uuid(j.operatorId)]
            )
        }
    }
}

/// Masque les deux valeurs propres à chaque archive (identifiant, horodatage), comme le générateur .NET.
func maskedManifest(_ text: String) -> String {
    text.replacingOccurrences(of: "\"(archiveId|createdAtUtc)\": \"[^\"]*\"", with: "\"$1\": \"*\"", options: .regularExpression)
}
```

- [ ] **Step 2: Écrire les tests qui échouent**

`LocalArchiveContentReferenceTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Archives : contenu identique au .NET")
struct LocalArchiveContentReferenceTests {
    private static func text(_ data: Data) -> String { String(decoding: data, as: UTF8.self) }

    @Test func eachArchiveOfTheFixtureHasTheSameFiveFiles() throws {
        let reference = try ArchiveReference.load()
        #expect(reference.archives.count == 2)
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try reference.seed(db)
        let repo = LocalFiscalRepository(db: db)

        for expected in reference.archives {
            let context = Comment(rawValue: expected.fileName)
            let closure = try #require(try repo.periodClosure(id: expected.periodClosureId), context)
            let content = try repo.archiveContent(for: closure, archiveId: UUID(), createdAt: Date())
            #expect(Self.text(content.receipts) == expected.files.receipts, context)
            #expect(Self.text(content.closures) == expected.files.closures, context)
            #expect(Self.text(content.periodClosures) == expected.files.periodClosures, context)
            #expect(Self.text(content.journal) == expected.files.journal, context)
            #expect(FiscalHashing.sha256Hex(content.receipts) == expected.sha256.receipts, context)
            #expect(FiscalHashing.sha256Hex(content.closures) == expected.sha256.closures, context)
            #expect(FiscalHashing.sha256Hex(content.periodClosures) == expected.sha256.periodClosures, context)
            #expect(FiscalHashing.sha256Hex(content.journal) == expected.sha256.journal, context)
            #expect(maskedManifest(Self.text(content.manifest)) == expected.files.manifest, context)
            #expect(LocalFiscalRepository.archiveFileName(closure) == expected.fileName, context)
            #expect(content.entries.map(\.name) == ["receipts.json", "closures.json", "period-closures.json", "journal.json", "manifest.json"])
        }
    }

    @Test func theManifestCarriesTheArchiveIdAndCreationDate() throws {
        let reference = try ArchiveReference.load()
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try reference.seed(db)
        let repo = LocalFiscalRepository(db: db)
        let closure = try #require(try repo.periodClosure(id: reference.archives[0].periodClosureId))
        let archiveId = UUID()
        let manifest = Self.text(try repo.archiveContent(for: closure, archiveId: archiveId, createdAt: SQLDate.parse("2026-10-14T09:30:00.250Z")!).manifest)
        #expect(manifest.contains("\"archiveId\": \"\(FiscalHashing.guidText(archiveId))\""))
        #expect(manifest.contains("\"createdAtUtc\": \"2026-10-14T09:30:00.25+00:00\""))
    }

    @Test func indentationFollowsSystemTextJson() {
        let json = IndentedJSON.array([
            .object([("A", .int(1)), ("B", .array([])), ("C", .object([])), ("D", .object([("E", .string("é\""))])), ("F", .guid(nil))]),
        ])
        #expect(json.text == "[\n  {\n    \"A\": 1,\n    \"B\": [],\n    \"C\": {},\n    \"D\": {\n      \"E\": \"\\u00E9\\u0022\"\n    },\n    \"F\": null\n  }\n]")
        #expect(IndentedJSON.array([]).text == "[]")
        #expect(IndentedJSON.money(-250).text == "{\n  \"AmountInCents\": -250,\n  \"Currency\": \"EUR\"\n}")
    }

    @Test func unsafeCharactersOfTheFileNameAreReplaced() {
        let closure = PeriodClosureRecord(
            id: UUID(), terminalId: "T0/1 é", periodType: .annual, periodKey: "2025", sequence: 1, periodStartUtc: Date(), periodEndUtc: Date(),
            totalTtcCents: 0, totalHtCents: 0, taxesSummaryJson: "{}", tenderTotalsJson: "{}", perpetualGrandTotalCents: 0, dailyClosureCount: 0,
            previousSignatureHash: "", signatureHash: "", sealedByUserId: UUID(), sealedByUserName: "", createdAtUtc: Date()
        )
        #expect(LocalFiscalRepository.archiveFileName(closure) == "archive-T0_1__-A-2025.zip")
    }
}
```

- [ ] **Step 3: Vérifier l'échec**

Run: `swift test --filter LocalArchiveContentReferenceTests`
Expected: erreur de compilation (`IndentedJSON`, `archiveContent`, `periodClosure(id:)` absents).

- [ ] **Step 4: Écrire `IndentedJSON`**

`IndentedJSON.swift` :

```swift
import Foundation

/// JSON indenté comme `JsonSerializer` avec `WriteIndented = true` et l'encodeur par défaut : deux espaces par niveau, `\n`,
/// `"clé": valeur`, `[]` et `{}` vides sur une ligne, pas de saut de ligne final. Les scalaires sont déjà du JSON (`FiscalJSON`).
enum IndentedJSON {
    case scalar(String)
    case object([(String, IndentedJSON)])
    case array([IndentedJSON])

    var text: String { render(level: 0) }

    private func render(level: Int) -> String {
        let inner = String(repeating: "  ", count: level + 1)
        let outer = String(repeating: "  ", count: level)
        switch self {
        case .scalar(let json):
            return json
        case .object(let fields):
            guard !fields.isEmpty else { return "{}" }
            let lines = fields.map { inner + FiscalJSON.string($0.0) + ": " + $0.1.render(level: level + 1) }
            return "{\n" + lines.joined(separator: ",\n") + "\n" + outer + "}"
        case .array(let items):
            guard !items.isEmpty else { return "[]" }
            let lines = items.map { inner + $0.render(level: level + 1) }
            return "[\n" + lines.joined(separator: ",\n") + "\n" + outer + "]"
        }
    }

    static func string(_ value: String?) -> IndentedJSON { .scalar(value.map(FiscalJSON.string) ?? "null") }
    static func guid(_ value: UUID?) -> IndentedJSON { .scalar(value.map(FiscalJSON.guid) ?? "null") }
    static func int(_ value: Int?) -> IndentedJSON { .scalar(value.map { "\($0)" } ?? "null") }
    static func bool(_ value: Bool) -> IndentedJSON { .scalar(value ? "true" : "false") }
    static func date(_ value: Date?) -> IndentedJSON { .scalar(value.map(FiscalJSON.dateTime) ?? "null") }

    /// `Money` du .NET (`readonly record struct`) : `{ "AmountInCents": …, "Currency": "EUR" }`.
    static func money(_ cents: Int?) -> IndentedJSON {
        .object([("AmountInCents", .int(cents ?? 0)), ("Currency", .string("EUR"))])
    }
}
```

- [ ] **Step 5: Ajouter `periodClosure(id:)`**

Dans `LocalFiscalRepository+PeriodClosures.swift`, après `periodClosures(terminalId:type:)` :

```swift
    func periodClosure(id: UUID) throws -> PeriodClosureRecord? {
        try db.query("SELECT * FROM PeriodClosures WHERE Id = ?", [.uuid(id)]).first.flatMap(periodClosureRecord)
    }
```

- [ ] **Step 6: Écrire la sélection et la sérialisation**

`LocalFiscalRepository+ArchiveContent.swift` :

```swift
import Foundation

/// Les cinq fichiers d'une archive fiscale, en UTF-8 sans BOM.
struct ArchiveContent {
    let receipts: Data
    let closures: Data
    let periodClosures: Data
    let journal: Data
    let manifest: Data

    /// Entrées du ZIP, dans l'ordre du .NET.
    var entries: [(name: String, data: Data)] {
        [("receipts.json", receipts), ("closures.json", closures), ("period-closures.json", periodClosures),
         ("journal.json", journal), ("manifest.json", manifest)]
    }
}

extension LocalFiscalRepository {
    /// Contenu de l'archive d'une clôture de période (`FiscalArchiveService.CreateArchiveAsync`) : reçus du poste créés dans `[début ; fin]`
    /// (bornes incluses) avec leurs règlements, Z dont la période commence dans `[début ; fin]`, la clôture elle-même (plus ses mensuelles
    /// pour une annuelle), entrées du JET de **tous** les postes dans `[début ; fin]`. Propriétés et ordre des entités .NET.
    /// ponytail: tout en mémoire (quelques milliers de reçus par mois) ; passer à une écriture en flux au-delà.
    func archiveContent(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> ArchiveContent {
        let start = SQLValue.text(FiscalHashing.storedText(closure.periodStartUtc))
        let end = SQLValue.text(FiscalHashing.storedText(closure.periodEndUtc))
        let terminal = SQLValue.text(closure.terminalId)

        let receiptRows = try db.query(
            "SELECT * FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc >= ? AND CreatedAtUtc <= ? ORDER BY SequenceNumber",
            [terminal, start, end]
        )
        let receipts = try receiptRows.map { (row: SQLRow) throws -> IndentedJSON in
            // Ordre d'écriture des règlements (le .NET ne trie pas l'Include).
            let tenders = try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ? ORDER BY rowid", [row["Id"]])
            return Self.receiptJSON(row, tenders: tenders.map(Self.tenderJSON))
        }
        let closures = try db.query(
            "SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? AND PeriodStartUtc >= ? AND PeriodStartUtc <= ? ORDER BY ClosureSequence",
            [terminal, start, end]
        ).map(Self.dailyClosureJSON)
        let periods = try db.query(
            """
            SELECT * FROM PeriodClosures WHERE TerminalId = ?
              AND (Id = ? OR (? = 2 AND PeriodType = 1 AND PeriodStartUtc >= ? AND PeriodEndUtc <= ?))
            ORDER BY PeriodType, ClosureSequence
            """,
            [terminal, .uuid(closure.id), .integer(closure.periodType.storedValue), start, end]
        ).map(Self.periodClosureJSON)
        let journal = try db.query(
            "SELECT * FROM JournalEntries WHERE OccurredAtUtc >= ? AND OccurredAtUtc <= ? ORDER BY COALESCE(ChainSequence, 0), Id",
            [start, end]
        ).map(Self.journalJSON)

        let receiptsData = Data(IndentedJSON.array(receipts).text.utf8)
        let closuresData = Data(IndentedJSON.array(closures).text.utf8)
        let periodsData = Data(IndentedJSON.array(periods).text.utf8)
        let journalData = Data(IndentedJSON.array(journal).text.utf8)

        let counts: [(String, IndentedJSON)] = [
            ("receipts", .int(receipts.count)), ("closures", .int(closures.count)),
            ("periodClosures", .int(periods.count)), ("journalEntries", .int(journal.count)),
        ]
        let files: [(String, IndentedJSON)] = [
            ("receipts.json", .string(FiscalHashing.sha256Hex(receiptsData))),
            ("closures.json", .string(FiscalHashing.sha256Hex(closuresData))),
            ("period-closures.json", .string(FiscalHashing.sha256Hex(periodsData))),
            ("journal.json", .string(FiscalHashing.sha256Hex(journalData))),
        ]
        let manifest: [(String, IndentedJSON)] = [
            ("archiveId", .guid(archiveId)),
            ("periodClosureId", .guid(closure.id)),
            ("terminalId", .string(closure.terminalId)),
            ("periodType", .int(closure.periodType.storedValue)),
            ("periodKey", .string(closure.periodKey)),
            ("periodStartUtc", .date(closure.periodStartUtc)),
            ("periodEndUtc", .date(closure.periodEndUtc)),
            ("createdAtUtc", .date(createdAt)),
            ("counts", .object(counts)),
            ("files", .object(files)),
        ]
        return ArchiveContent(
            receipts: receiptsData, closures: closuresData, periodClosures: periodsData, journal: journalData,
            manifest: Data(IndentedJSON.object(manifest).text.utf8)
        )
    }

    /// `archive-{poste}-{M|A}-{clé}.zip`, caractères hors `[A-Za-z0-9_.-]` remplacés par `_` (`SafeFileNamePart` du .NET, unité UTF-16).
    static func archiveFileName(_ closure: PeriodClosureRecord) -> String {
        func safe(_ part: String) -> String {
            part.replacingOccurrences(of: "[^A-Za-z0-9_.-]", with: "_", options: .regularExpression)
        }
        return "archive-\(safe(closure.terminalId))-\(closure.periodType == .monthly ? "M" : "A")-\(safe(closure.periodKey)).zip"
    }

    // MARK: Entités .NET (ordre de déclaration des propriétés)

    /// `FiscalReceipt` (avec `IsVoid`, colonne historique toujours sérialisée par le .NET).
    private static func receiptJSON(_ row: SQLRow, tenders: [IndentedJSON]) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("ReceiptNumber", .string(row.string("ReceiptNumber"))),
            ("OrderId", .guid(row.uuid("OrderId"))),
            ("SequenceNumber", .int(row.int("SequenceNumber"))),
            ("TotalTtcAmount", .money(row.int("TotalTtcAmount"))),
            ("TotalHtAmount", .money(row.int("TotalHtAmount"))),
            ("TaxBreakdownJson", .string(row.string("TaxBreakdownJson"))),
            ("HtBreakdownJson", .string(row.string("HtBreakdownJson"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("CreatedAtUtc", .date(row.date("CreatedAtUtc"))),
            ("IsVoid", .bool(row.bool("IsVoid"))),
            ("VoidedReceiptId", .guid(row.uuid("VoidedReceiptId"))),
            ("Tenders", .array(tenders)),
        ]
        return .object(fields)
    }

    /// `PaymentTender`.
    private static func tenderJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("FiscalReceiptId", .guid(row.uuid("FiscalReceiptId"))),
            ("Method", .int(row.int("Method"))),
            ("Amount", .money(row.int("Amount"))),
            ("Tendered", .money(row.int("Tendered"))),
            ("ChangeGiven", .money(row.int("ChangeGiven"))),
        ]
        return .object(fields)
    }

    /// `DailyFiscalClosure` (sans `CreatedAtUtc`, colonne propre à l'iPad que l'entité .NET n'a pas).
    private static func dailyClosureJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("ClosureSequence", .int(row.int("ClosureSequence"))),
            ("PeriodStartUtc", .date(row.date("PeriodStartUtc"))),
            ("PeriodEndUtc", .date(row.date("PeriodEndUtc"))),
            ("TotalSalesTtc", .money(row.int("TotalSalesTtc"))),
            ("TotalSalesHt", .money(row.int("TotalSalesHt"))),
            ("TaxesSummaryJson", .string(row.string("TaxesSummaryJson"))),
            ("TenderTotalsJson", .string(row.string("TenderTotalsJson"))),
            ("PerpetualGrandTotalCents", .int(row.int("PerpetualGrandTotalCents"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("SealedByUserName", .string(row.string("SealedByUserName"))),
            ("SealedByUserId", .guid(row.uuid("SealedByUserId"))),
        ]
        return .object(fields)
    }

    /// `FiscalPeriodClosure`.
    private static func periodClosureJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("PeriodType", .int(row.int("PeriodType"))),
            ("PeriodKey", .string(row.string("PeriodKey"))),
            ("ClosureSequence", .int(row.int("ClosureSequence"))),
            ("PeriodStartUtc", .date(row.date("PeriodStartUtc"))),
            ("PeriodEndUtc", .date(row.date("PeriodEndUtc"))),
            ("TotalTtcAmount", .money(row.int("TotalTtcAmount"))),
            ("TotalHtAmount", .money(row.int("TotalHtAmount"))),
            ("TaxesSummaryJson", .string(row.string("TaxesSummaryJson"))),
            ("TenderTotalsJson", .string(row.string("TenderTotalsJson"))),
            ("PerpetualGrandTotalCents", .int(row.int("PerpetualGrandTotalCents"))),
            ("DailyClosureCount", .int(row.int("DailyClosureCount"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("SealedByUserId", .guid(row.uuid("SealedByUserId"))),
            ("SealedByUserName", .string(row.string("SealedByUserName"))),
            ("CreatedAtUtc", .date(row.date("CreatedAtUtc"))),
        ]
        return .object(fields)
    }

    /// `TransactionJournalEntry`.
    private static func journalJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("LocalSequence", .int(row.int("LocalSequence"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("OccurredAtUtc", .date(row.date("OccurredAtUtc"))),
            ("IdempotencyKey", .string(row.string("IdempotencyKey"))),
            ("EventType", .string(row.string("EventType"))),
            ("PayloadJson", .string(row.string("PayloadJson"))),
            ("EntryHash", .string(row.string("EntryHash"))),
            ("ChainSequence", .int(row.int("ChainSequence"))),
            ("PreviousHash", .string(row.string("PreviousHash"))),
            ("OperatorId", .guid(row.uuid("OperatorId"))),
        ]
        return .object(fields)
    }
}
```

- [ ] **Step 7: Vérifier**

Run: `swift test --filter LocalArchiveContentReferenceTests`
Expected: PASS. En cas d'écart, imprimer les deux textes et comparer ligne à ligne : d'abord la sélection (nombre d'éléments, bornes), puis le format des dates (`FiscalJSON.dateTime`), puis l'ordre ou la présence des propriétés. La fixture fait foi : adapter le Swift, jamais la fixture.

- [ ] **Step 8: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): contenu des archives fiscales identique au .NET (fixture)"
```

---

### Task 6: Dépôt des archives : chaîne, JET, liste, vérification

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+Archives.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift` (ajout `archiveCreated`, `archiveExported`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalArchiveRepositoryTests.swift`

**Interfaces:**
- Consumes: `archiveContent`, `archiveFileName`, `periodClosure(id:)` (Task 5), `ZipStoreWriter` / `ZipStoreReader` (Task 2), `FiscalHashing.archiveHash` / `sha256Hex` (Task 3), table `FiscalArchives` (Task 4), `appendJournal` (existant).
- Produces:
  ```swift
  enum FiscalArchiveError: Error, Equatable { case exists }
  extension LocalFiscalRepository {
      func archiveZip(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> Data
      func insertArchive(id: UUID, closure: PeriodClosureRecord, fileName: String, fileSha256: String, fileSizeBytes: Int, userId: UUID, createdAt: Date) throws -> FiscalArchive
      func fiscalArchives() throws -> [FiscalArchive]                // la plus récente d'abord
      func fiscalArchive(id: UUID) throws -> FiscalArchive?
      func fiscalArchive(periodClosureId: UUID) throws -> FiscalArchive?
      func verifyArchive(data: Data, fileName: String?) throws -> ArchiveVerificationResult
  }
  LocalJournalEvents.archiveCreated == "ARCHIVE_CREATED", LocalJournalEvents.archiveExported == "ARCHIVE_EXPORTED"
  ```
  `insertArchive` n'ouvre pas de transaction (l'appelant le fait) et refuse un doublon par `FiscalArchiveError.exists`.

- [ ] **Step 1: Écrire les tests qui échouent**

`LocalFiscalArchiveRepositoryTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Archives fiscales : dépôt (chaîne, JET, vérification)")
struct LocalFiscalArchiveRepositoryTests {
    private static let manager = UUID()

    private struct Fixture {
        let db: SQLiteDatabase
        let repo: LocalFiscalRepository
        let january: PeriodClosureRecord
        let february: PeriodClosureRecord
    }

    private static func insertClosure(_ db: SQLiteDatabase, key: String, sequence: Int, start: String, end: String) throws -> UUID {
        let id = UUID()
        try db.run(
            """
            INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                SealedByUserName, CreatedAtUtc)
            VALUES (?, 'T01', 1, ?, ?, ?, ?, 0, 0, '{}', '{}', 0, 0, 'GENESIS', 'SIG', ?, 'Chef', ?)
            """,
            [.uuid(id), .text(key), .integer(sequence), .text(start), .text(end), .uuid(manager), .text(end)]
        )
        return id
    }

    private static func makeFixture(path: String = ":memory:") throws -> Fixture {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        let jan = try insertClosure(db, key: "2025-01", sequence: 1, start: "2025-01-01T00:00:00.000Z", end: "2025-02-01T00:00:00.000Z")
        let feb = try insertClosure(db, key: "2025-02", sequence: 2, start: "2025-02-01T00:00:00.000Z", end: "2025-03-01T00:00:00.000Z")
        let repo = LocalFiscalRepository(db: db)
        return Fixture(db: db, repo: repo, january: try repo.periodClosure(id: jan)!, february: try repo.periodClosure(id: feb)!)
    }

    /// Construit le ZIP et enregistre l'archive, comme `LocalPosAPI.createArchive` (sans fichier sur disque).
    @discardableResult
    private static func archive(_ f: Fixture, _ closure: PeriodClosureRecord, at iso: String) throws -> (archive: FiscalArchive, zip: Data) {
        let now = SQLDate.parse(iso)!
        let id = UUID()
        let zip = try f.repo.archiveZip(for: closure, archiveId: id, createdAt: now)
        let record = try f.db.transaction {
            try f.repo.insertArchive(
                id: id, closure: closure, fileName: LocalFiscalRepository.archiveFileName(closure), fileSha256: FiscalHashing.sha256Hex(zip),
                fileSizeBytes: zip.count, userId: manager, createdAt: now
            )
        }
        return (record, zip)
    }

    @Test func archivesFormOneGlobalChainListedNewestFirst() throws {
        let f = try Self.makeFixture()
        let first = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z").archive
        let second = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.120Z").archive
        #expect(first.archiveSequence == 1 && second.archiveSequence == 2)
        #expect(first.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(second.previousSignatureHash == first.signatureHash)
        #expect(second.signatureHash == FiscalHashing.archiveHash(
            previousHash: first.signatureHash, periodType: 1, periodKey: "2025-02", fileSha256: second.fileSha256, createdAt: second.createdAtUtc))
        #expect(try f.repo.fiscalArchives().map(\.archiveSequence) == [2, 1])
        #expect(try f.repo.fiscalArchive(periodClosureId: f.january.id)?.id == first.id)
        #expect(second.fileName == "archive-T01-M-2025-02.zip")
    }

    @Test func aSecondArchiveForTheSameClosureIsRefused() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        #expect(throws: FiscalArchiveError.exists) { try Self.archive(f, f.january, at: "2025-02-03T10:00:00.000Z") }
        #expect(try f.repo.fiscalArchives().count == 1)
        #expect(try f.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").count == 1)
    }

    @Test func theCreationIsJournaledWithTheDotNetPayload() throws {
        let f = try Self.makeFixture()
        let a = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z").archive
        let row = try #require(try f.db.query("SELECT * FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").first)
        #expect(row.string("PayloadJson") ==
            "{\"archiveId\":\"\(FiscalHashing.guidText(a.id))\",\"periodClosureId\":\"\(FiscalHashing.guidText(f.january.id))\",\"periodType\":1,"
            + "\"periodKey\":\"2025-01\",\"fileName\":\"archive-T01-M-2025-01.zip\",\"fileSha256\":\"\(a.fileSha256)\",\"fileSizeBytes\":\(a.fileSizeBytes),"
            + "\"archiveSequence\":1,\"signatureHash\":\"\(a.signatureHash)\"}")
        #expect(row.string("TerminalId") == "T01")
        #expect(row.uuid("OperatorId") == Self.manager)
    }

    @Test func anUntouchedArchiveIsValid() throws {
        let f = try Self.makeFixture()
        let (a, zip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        #expect(try f.repo.verifyArchive(data: zip, fileName: "renommée.zip") == ArchiveVerificationResult(isValid: true, archiveId: a.id, reason: nil))
    }

    @Test func oneFlippedByteIsAHashMismatch() throws {
        let f = try Self.makeFixture()
        let (a, zip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        var tampered = zip
        tampered[43] ^= 0xFF // 1er octet des données de receipts.json (en-tête local 30 + nom 13) : le manifeste reste lisible
        #expect(try f.repo.verifyArchive(data: tampered, fileName: "autre.zip") == ArchiveVerificationResult(isValid: false, archiveId: a.id, reason: "hash_mismatch"))
    }

    @Test func withoutManifestTheArchiveIsFoundByHashThenByFileName() throws {
        let f = try Self.makeFixture()
        let now = SQLDate.parse("2025-02-02T10:00:00.000Z")!
        let bare = ZipStoreWriter.archive([("a.json", Data("[]".utf8))], modifiedAt: now)
        let a = try f.db.transaction {
            try f.repo.insertArchive(id: UUID(), closure: f.january, fileName: "archive-T01-M-2025-01.zip", fileSha256: FiscalHashing.sha256Hex(bare),
                                     fileSizeBytes: bare.count, userId: Self.manager, createdAt: now)
        }
        #expect(try f.repo.verifyArchive(data: bare, fileName: "x.zip").isValid)
        var tampered = bare
        tampered[tampered.count - 1] ^= 0x01
        #expect(try f.repo.verifyArchive(data: tampered, fileName: a.fileName) == ArchiveVerificationResult(isValid: false, archiveId: a.id, reason: "hash_mismatch"))
        #expect(try f.repo.verifyArchive(data: tampered, fileName: "x.zip") == ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive"))
    }

    @Test func emptyOrForeignDataIsAnUnknownArchive() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let unknown = ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive")
        #expect(try f.repo.verifyArchive(data: Data(), fileName: "archive-T01-M-2025-01.zip") == unknown)
        #expect(try f.repo.verifyArchive(data: Data("pas un zip".utf8), fileName: "") == unknown)
        #expect(try f.repo.verifyArchive(data: Data("pas un zip".utf8), fileName: nil) == unknown)
    }

    @Test func aTamperedSignatureOrLinkIsAChainBreak() throws {
        let f = try Self.makeFixture(path: temporaryDatabasePath())
        let (first, firstZip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let (second, secondZip) = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z")
        // Falsification hors application : le trigger d'immuabilité est contourné sur cette base de test.
        try f.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try f.db.run("UPDATE FiscalArchives SET SignatureHash = ? WHERE Id = ?", [.text(String(repeating: "0", count: 64)), .uuid(first.id)])
        #expect(try f.repo.verifyArchive(data: firstZip, fileName: nil) == ArchiveVerificationResult(isValid: false, archiveId: first.id, reason: "chain_break"))
        #expect(try f.repo.verifyArchive(data: secondZip, fileName: nil) == ArchiveVerificationResult(isValid: false, archiveId: second.id, reason: "chain_break"))
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalFiscalArchiveRepositoryTests`
Expected: erreur de compilation (`archiveZip`, `insertArchive`, `FiscalArchiveError` absents).

- [ ] **Step 3: Ajouter les événements JET**

Dans `LocalJournalEvents.swift` :

```swift
    static let archiveCreated = "ARCHIVE_CREATED"
    static let archiveExported = "ARCHIVE_EXPORTED"
```

- [ ] **Step 4: Écrire le dépôt**

`LocalFiscalRepository+Archives.swift` :

```swift
import Foundation

/// Refus métier d'une archive : une seule archive par clôture (`ArchiveExistsException`, code `archive_exists`).
enum FiscalArchiveError: Error, Equatable {
    case exists
}

extension LocalFiscalRepository {
    /// ZIP stocké des cinq fichiers de l'archive, horodaté à la création.
    func archiveZip(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> Data {
        ZipStoreWriter.archive(try archiveContent(for: closure, archiveId: archiveId, createdAt: createdAt).entries, modifiedAt: createdAt)
    }

    /// Enregistre l'archive en fin de chaîne globale et journalise `ARCHIVE_CREATED` (charge utile du .NET). N'ouvre pas de transaction.
    func insertArchive(
        id: UUID, closure: PeriodClosureRecord, fileName: String, fileSha256: String, fileSizeBytes: Int, userId: UUID, createdAt: Date
    ) throws -> FiscalArchive {
        guard try fiscalArchive(periodClosureId: closure.id) == nil else { throw FiscalArchiveError.exists }
        let last = try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence DESC LIMIT 1").first.flatMap(archiveRecord)
        let sequence = (last?.archiveSequence ?? 0) + 1
        let previousHash = last?.signatureHash ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(createdAt)
        let signature = FiscalHashing.archiveHash(
            previousHash: previousHash, periodType: closure.periodType.storedValue, periodKey: closure.periodKey,
            fileSha256: fileSha256, createdAt: timestamp
        )
        try db.run(
            """
            INSERT INTO FiscalArchives (Id, PeriodClosureId, PeriodType, PeriodKey, FileName, FileSha256, FileSizeBytes, ArchiveSequence,
                PreviousSignatureHash, SignatureHash, CreatedByUserId, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                .uuid(id), .uuid(closure.id), .integer(closure.periodType.storedValue), .text(closure.periodKey), .text(fileName),
                .text(fileSha256), .integer(fileSizeBytes), .integer(sequence), .text(previousHash), .text(signature), .uuid(userId),
                .text(FiscalHashing.storedText(timestamp)),
            ]
        )
        try appendJournal(
            eventType: LocalJournalEvents.archiveCreated,
            payloadJson: FiscalJSON.object([
                ("archiveId", FiscalJSON.guid(id)),
                ("periodClosureId", FiscalJSON.guid(closure.id)),
                ("periodType", "\(closure.periodType.storedValue)"),
                ("periodKey", FiscalJSON.string(closure.periodKey)),
                ("fileName", FiscalJSON.string(fileName)),
                ("fileSha256", FiscalJSON.string(fileSha256)),
                ("fileSizeBytes", "\(fileSizeBytes)"),
                ("archiveSequence", "\(sequence)"),
                ("signatureHash", FiscalJSON.string(signature)),
            ]),
            terminalId: closure.terminalId, operatorId: userId, now: timestamp
        )
        return FiscalArchive(
            id: id, periodClosureId: closure.id, periodType: closure.periodType, periodKey: closure.periodKey, fileName: fileName,
            fileSha256: fileSha256, fileSizeBytes: fileSizeBytes, archiveSequence: sequence, previousSignatureHash: previousHash,
            signatureHash: signature, createdByUserId: userId, createdAtUtc: timestamp
        )
    }

    func fiscalArchives() throws -> [FiscalArchive] {
        try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence DESC").compactMap(archiveRecord)
    }

    func fiscalArchive(id: UUID) throws -> FiscalArchive? {
        try firstArchive(where: "Id = ?", .uuid(id))
    }

    func fiscalArchive(periodClosureId: UUID) throws -> FiscalArchive? {
        try firstArchive(where: "PeriodClosureId = ?", .uuid(periodClosureId))
    }

    /// `VerifyArchiveAsync` : retrouve l'archive par l'identifiant du manifeste, puis la clôture du manifeste, puis le SHA-256 du fichier,
    /// puis le nom de fichier ; contrôle l'empreinte, recalcule la signature et le lien avec l'archive précédente (ou la genèse).
    func verifyArchive(data: Data, fileName: String?) throws -> ArchiveVerificationResult {
        guard !data.isEmpty else { return ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive") }
        let sha = FiscalHashing.sha256Hex(data)
        var manifestArchiveId: UUID?
        var manifestClosureId: UUID?
        if let manifest = ZipStoreReader.entry(named: "manifest.json", in: data),
           let root = (try? JSONSerialization.jsonObject(with: manifest)) as? [String: Any] {
            manifestArchiveId = (root["archiveId"] as? String).flatMap { UUID(uuidString: $0) }
            manifestClosureId = (root["periodClosureId"] as? String).flatMap { UUID(uuidString: $0) }
        }

        var found = try manifestArchiveId.flatMap { try fiscalArchive(id: $0) }
        if found == nil, let manifestClosureId { found = try fiscalArchive(periodClosureId: manifestClosureId) }
        if found == nil { found = try firstArchive(where: "lower(FileSha256) = ?", .text(sha)) }
        if found == nil, let fileName, !fileName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            found = try firstArchive(where: "FileName = ?", .text(fileName))
        }
        guard let archive = found else { return ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive") }

        guard archive.fileSha256.lowercased() == sha else {
            return ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "hash_mismatch")
        }
        let computed = FiscalHashing.archiveHash(
            previousHash: archive.previousSignatureHash, periodType: archive.periodType == .monthly ? 1 : 2, periodKey: archive.periodKey,
            fileSha256: archive.fileSha256, createdAt: archive.createdAtUtc
        )
        let chainBreak = ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "chain_break")
        guard archive.signatureHash.lowercased() == computed else { return chainBreak }
        if archive.archiveSequence > 1 {
            let previous = try firstArchive(where: "ArchiveSequence = ?", .integer(archive.archiveSequence - 1))
            guard previous?.signatureHash.lowercased() == archive.previousSignatureHash.lowercased() else { return chainBreak }
        } else if archive.previousSignatureHash.lowercased() != FiscalHashing.genesisHash.lowercased() {
            return chainBreak
        }
        return ArchiveVerificationResult(isValid: true, archiveId: archive.id, reason: nil)
    }

    private func firstArchive(where clause: String, _ value: SQLValue) throws -> FiscalArchive? {
        try db.query("SELECT * FROM FiscalArchives WHERE \(clause) LIMIT 1", [value]).first.flatMap(archiveRecord)
    }

    private func archiveRecord(_ row: SQLRow) -> FiscalArchive? {
        guard let id = row.uuid("Id"), let closureId = row.uuid("PeriodClosureId") else { return nil }
        return FiscalArchive(
            id: id, periodClosureId: closureId, periodType: row.int("PeriodType") == 2 ? .annual : .monthly, periodKey: row.string("PeriodKey") ?? "",
            fileName: row.string("FileName") ?? "", fileSha256: row.string("FileSha256") ?? "", fileSizeBytes: row.int("FileSizeBytes") ?? 0,
            archiveSequence: row.int("ArchiveSequence") ?? 0, previousSignatureHash: row.string("PreviousSignatureHash") ?? "",
            signatureHash: row.string("SignatureHash") ?? "", createdByUserId: row.uuid("CreatedByUserId") ?? UUID(),
            createdAtUtc: row.date("CreatedAtUtc") ?? Date(timeIntervalSince1970: 0)
        )
    }
}
```

- [ ] **Step 5: Vérifier**

Run: `swift test --filter LocalFiscalArchiveRepositoryTests`
Expected: PASS. Janvier n'a aucun reçu : `receipts.json` vaut `[]` (octets 43 et 44), d'où l'indice 43.

- [ ] **Step 6: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): chaîne des archives fiscales, JET ARCHIVE_CREATED et vérification"
```

---

### Task 7: API `LocalPosAPI+Archives` (création, liste, fichier, vérification)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (propriété et paramètre `archivesDirectory`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift:11-13` (retirer les trois stubs)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift`

**Interfaces:**
- Consumes: Task 6 (dépôt), Task 2 (`runUnzip` en test), 3a (`executePeriodClosure`, `zClosure`).
- Produces (signatures du protocole `PosAPI` inchangées, plus une méthode propre au mode autonome) :
  ```swift
  public init(path: String, seed: LocalSeedMode = .demo, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .autoupdatingCurrent, archivesDirectory: URL? = nil) throws
  let archivesDirectory: URL
  static func defaultArchivesDirectory(databasePath: String) -> URL   // "<dossier>/<base>-archives", dossier temporaire unique pour ":memory:"
  public func archives() async throws -> [FiscalArchive]
  public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive
  public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult
  public func archiveFile(id: UUID) async throws -> URL               // hors protocole ; utilisé par le partage du lot 3d
  ```
  Erreurs : clôture inconnue `APIError.notFound("Clôture de période introuvable.")` ; doublon `APIError.server(status: 409, message: "Une archive existe déjà pour cette clôture.")` (même texte que `InMemoryPosAPI`) ; fichier vide `APIError.server(status: 400, message: "Aucun fichier reçu.")` ; fichier absent `APIError.notFound("Fichier d'archive introuvable.")`.

- [ ] **Step 1: Écrire les tests qui échouent**

`LocalArchiveAPITests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : archives fiscales")
struct LocalArchiveAPITests {
    private struct Context {
        let api: LocalPosAPI
        let db: SQLiteDatabase
        let folder: URL
        let clock: LocalTestClock
        let operatorId: UUID?
    }

    /// API sur fichier avec son propre dossier d'archives (les tests tournent en parallèle), horloge de test et calendrier UTC.
    private func makeContext(pin: String = "1234") async throws -> Context {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let path = temporaryDatabasePath()
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("archives-\(UUID().uuidString)", isDirectory: true)
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar, archivesDirectory: folder)
        let login = try await api.login(pin: pin)
        return Context(api: api, db: try SQLiteDatabase(path: path), folder: folder, clock: clock, operatorId: login.operatorId)
    }

    /// Septembre 2026 : une vente, son Z, puis la clôture mensuelle.
    private func closeSeptember(_ c: Context) async throws -> FiscalPeriodClosure {
        let burger = try await localProduct(c.api, "Burger Gourmet Rossini")
        try await seatTable(c.api, "T1", items: [localInput(burger)])
        let order = try #require(try await c.api.activeOrder(table: "T1"))
        _ = try await c.api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        c.clock.set("2026-09-10T21:00:00Z")
        _ = try await c.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        c.clock.set("2026-10-02T09:00:00Z")
        return try await c.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
    }

    private func folderContents(_ c: Context) -> [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: c.folder.path)) ?? []).sorted()
    }

    @Test func theArchiveIsWrittenSignedAndListed() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        let archive = try await c.api.createArchive(periodClosureId: closure.id)

        #expect(archive.fileName == "archive-T01-M-2026-09.zip")
        #expect(archive.archiveSequence == 1)
        #expect(archive.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(archive.createdByUserId == c.operatorId)
        #expect(folderContents(c) == ["archive-T01-M-2026-09.zip"]) // pas de .tmp laissé
        let data = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        #expect(FiscalHashing.sha256Hex(data) == archive.fileSha256)
        #expect(data.count == archive.fileSizeBytes)
        #expect(ZipStoreReader.names(in: data) == ["receipts.json", "closures.json", "period-closures.json", "journal.json", "manifest.json"])
        #expect(try await c.api.archives().map(\.id) == [archive.id])
    }

    #if os(macOS)
    @Test(.enabled(if: FileManager.default.isExecutableFile(atPath: "/usr/bin/unzip")))
    func anIndependentUnzipReadsTheArchiveAndTheManifestHashesMatch() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let path = c.folder.appendingPathComponent(archive.fileName).path
        #expect(try runUnzip(["-t", path]).status == 0)
        let manifestData = try runUnzip(["-p", path, "manifest.json"]).output
        let manifest = try #require(try JSONSerialization.jsonObject(with: manifestData) as? [String: Any])
        #expect(manifest["archiveId"] as? String == FiscalHashing.guidText(archive.id))
        #expect((manifest["counts"] as? [String: Int])?["receipts"] == 1)
        let files = try #require(manifest["files"] as? [String: String])
        #expect(files.count == 4)
        for (name, sha) in files {
            #expect(FiscalHashing.sha256Hex(try runUnzip(["-p", path, name]).output) == sha, Comment(rawValue: name))
        }
    }
    #endif

    @Test func archivesChainAcrossClosures() async throws {
        let c = try await makeContext()
        let september = try await closeSeptember(c)
        c.clock.set("2026-11-02T09:00:00Z")
        let october = try await c.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-10")
        let first = try await c.api.createArchive(periodClosureId: september.id)
        let second = try await c.api.createArchive(periodClosureId: october.id)
        #expect(second.archiveSequence == 2)
        #expect(second.previousSignatureHash == first.signatureHash)
        #expect(try await c.api.archives().map(\.archiveSequence) == [2, 1])
    }

    @Test func aSecondArchiveIsRefusedAndWritesNothing() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        _ = try await c.api.createArchive(periodClosureId: closure.id)
        await #expect(throws: APIError.server(status: 409, message: "Une archive existe déjà pour cette clôture.")) {
            _ = try await c.api.createArchive(periodClosureId: closure.id)
        }
        #expect(try c.db.query("SELECT 1 AS x FROM FiscalArchives").count == 1)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").count == 1)
        #expect(folderContents(c) == ["archive-T01-M-2026-09.zip"])
    }

    @Test func anUnknownClosureIsNotFoundAndWritesNothing() async throws {
        let c = try await makeContext()
        await #expect(throws: APIError.notFound("Clôture de période introuvable.")) {
            _ = try await c.api.createArchive(periodClosureId: UUID())
        }
        #expect(try c.db.query("SELECT 1 AS x FROM FiscalArchives").isEmpty)
        #expect(folderContents(c).isEmpty)
    }

    @Test func verificationUsesTheFileContent() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let data = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        #expect(try await c.api.verifyArchive(data: data, fileName: archive.fileName).isValid)
        var tampered = data
        tampered[43] ^= 0xFF // 1er octet des données de receipts.json
        let result = try await c.api.verifyArchive(data: tampered, fileName: archive.fileName)
        #expect(result == ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "hash_mismatch"))
        await #expect(throws: APIError.server(status: 400, message: "Aucun fichier reçu.")) {
            _ = try await c.api.verifyArchive(data: Data(), fileName: archive.fileName)
        }
    }

    @Test func theFileIsServedAndTheExportJournaledWithoutTerminalOrOperator() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        let archive = try await c.api.createArchive(periodClosureId: closure.id)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(url.standardizedFileURL == c.folder.appendingPathComponent(archive.fileName).standardizedFileURL)
        let row = try #require(try c.db.query("SELECT * FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").first)
        #expect(row.string("PayloadJson") ==
            "{\"archiveId\":\"\(FiscalHashing.guidText(archive.id))\",\"periodClosureId\":\"\(FiscalHashing.guidText(closure.id))\","
            + "\"fileName\":\"archive-T01-M-2026-09.zip\",\"fileSha256\":\"\(archive.fileSha256)\"}")
        #expect(row.string("TerminalId") == nil && row.string("OperatorId") == nil)

        try FileManager.default.removeItem(at: url)
        await #expect(throws: APIError.notFound("Fichier d'archive introuvable.")) { _ = try await c.api.archiveFile(id: archive.id) }
        await #expect(throws: APIError.notFound("Fichier d'archive introuvable.")) { _ = try await c.api.archiveFile(id: UUID()) }
    }

    @Test func aWaiterCannotUseArchives() async throws {
        let c = try await makeContext(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await c.api.archives() }
        await #expect(throws: APIError.self) { _ = try await c.api.createArchive(periodClosureId: UUID()) }
        await #expect(throws: APIError.self) { _ = try await c.api.verifyArchive(data: Data("x".utf8), fileName: "a.zip") }
        await #expect(throws: APIError.self) { _ = try await c.api.archiveFile(id: UUID()) }
    }

    @Test func theDefaultArchivesFolderSitsNextToTheDatabase() {
        #expect(LocalPosAPI.defaultArchivesDirectory(databasePath: "/x/RestaurantPOS/pos-local.sqlite").path == "/x/RestaurantPOS/pos-local-archives")
        let a = LocalPosAPI.defaultArchivesDirectory(databasePath: ":memory:")
        let b = LocalPosAPI.defaultArchivesDirectory(databasePath: ":memory:")
        #expect(a != b)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `swift test --filter LocalArchiveAPITests`
Expected: erreur de compilation (paramètre `archivesDirectory` et `archiveFile` absents).

- [ ] **Step 3: Injecter le dossier d'archives**

Dans `LocalPosAPI.swift`, ajouter la propriété après `let calendar: Calendar` :

```swift
    /// Dossier des fichiers d'archive fiscale (`.zip`) ; injectable pour les tests.
    let archivesDirectory: URL
```

Remplacer l'initialiseur par :

```swift
    /// `path` : fichier SQLite à créer ou rouvrir, ou `":memory:"` (tests). `archivesDirectory` : dossier des archives fiscales,
    /// par défaut `<base>-archives/` à côté du fichier SQLite.
    public init(
        path: String, seed: LocalSeedMode = .demo, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .autoupdatingCurrent,
        archivesDirectory: URL? = nil
    ) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db, mode: seed)
        self.db = db
        self.clock = clock
        self.calendar = calendar
        self.archivesDirectory = archivesDirectory ?? Self.defaultArchivesDirectory(databasePath: path)
    }

    /// `<dossier de la base>/<nom de la base>-archives` ; un dossier temporaire unique pour une base en mémoire.
    static func defaultArchivesDirectory(databasePath path: String) -> URL {
        guard path != ":memory:" else {
            return FileManager.default.temporaryDirectory.appendingPathComponent("pos-archives-\(UUID().uuidString)", isDirectory: true)
        }
        let file = URL(fileURLWithPath: path)
        return file.deletingLastPathComponent().appendingPathComponent(file.deletingPathExtension().lastPathComponent + "-archives", isDirectory: true)
    }
```

- [ ] **Step 4: Écrire l'API**

Supprimer les trois lignes `archives`, `createArchive` et `verifyArchive` de `LocalPosAPI+Unsupported.swift`. Créer `LocalPosAPI+Archives.swift` :

```swift
import Foundation

extension LocalPosAPI {
    private static let archiveExists = APIError.server(status: 409, message: "Une archive existe déjà pour cette clôture.")
    private static let archiveFileMissing = APIError.notFound("Fichier d'archive introuvable.")

    /// Archives fiscales, la plus récente d'abord (`GET /api/fiscal/archives`). Réservé aux responsables.
    public func archives() async throws -> [FiscalArchive] {
        try requireManager()
        return try fiscalRepository.fiscalArchives()
    }

    /// Crée l'archive d'une clôture de période (`POST /api/fiscal/archives`) : ZIP écrit en `.tmp`, ligne et JET dans une transaction,
    /// puis renommage. Un refus (clôture inconnue, archive existante) n'écrit rien.
    public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive {
        let manager = try requireManager()
        let fiscal = fiscalRepository
        let now = FiscalHashing.roundedToMilliseconds(clock())
        guard let closure = try fiscal.periodClosure(id: periodClosureId), closure.terminalId == Self.standaloneTerminalId else {
            throw APIError.notFound("Clôture de période introuvable.")
        }
        guard try fiscal.fiscalArchive(periodClosureId: periodClosureId) == nil else { throw Self.archiveExists }

        let archiveId = UUID()
        let zip = try fiscal.archiveZip(for: closure, archiveId: archiveId, createdAt: now)
        let fileName = LocalFiscalRepository.archiveFileName(closure)
        let folder = archivesDirectory
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let temporary = folder.appendingPathComponent("\(FiscalHashing.guidText(archiveId)).tmp")
        try zip.write(to: temporary, options: .withoutOverwriting)

        let archive: FiscalArchive
        do {
            archive = try db.transaction {
                try fiscal.insertArchive(
                    id: archiveId, closure: closure, fileName: fileName, fileSha256: FiscalHashing.sha256Hex(zip),
                    fileSizeBytes: zip.count, userId: manager.id, createdAt: now
                )
            }
        } catch FiscalArchiveError.exists {
            try? FileManager.default.removeItem(at: temporary)
            throw Self.archiveExists
        } catch {
            try? FileManager.default.removeItem(at: temporary)
            throw error
        }
        // Comme le .NET (`File.Move(..., overwrite: true)`) : le fichier définitif n'apparaît qu'après le COMMIT.
        let target = folder.appendingPathComponent(fileName)
        try? FileManager.default.removeItem(at: target)
        try FileManager.default.moveItem(at: temporary, to: target)
        return archive
    }

    /// Vérifie une archive (`POST /api/fiscal/archives/verify`) : `unknown_archive`, `hash_mismatch`, `chain_break` ou valide.
    /// Fichier vide refusé en 400, comme la route .NET.
    public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult {
        try requireManager()
        guard !data.isEmpty else { throw APIError.server(status: 400, message: "Aucun fichier reçu.") }
        return try fiscalRepository.verifyArchive(data: data, fileName: fileName)
    }

    /// Fichier d'une archive (`GET /api/fiscal/archives/{id}/file`), pour le partage. Journalise `ARCHIVE_EXPORTED` sans poste ni opérateur,
    /// comme le .NET ; un échec du journal ne bloque pas l'export.
    public func archiveFile(id: UUID) async throws -> URL {
        try requireManager()
        let fiscal = fiscalRepository
        guard let archive = try fiscal.fiscalArchive(id: id) else { throw Self.archiveFileMissing }
        let folder = archivesDirectory.standardizedFileURL
        let file = folder.appendingPathComponent(archive.fileName).standardizedFileURL
        guard file.path.hasPrefix(folder.path + "/"), FileManager.default.fileExists(atPath: file.path) else { throw Self.archiveFileMissing }
        let now = clock()
        _ = try? db.transaction {
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.archiveExported,
                payloadJson: FiscalJSON.object([
                    ("archiveId", FiscalJSON.guid(archive.id)),
                    ("periodClosureId", FiscalJSON.guid(archive.periodClosureId)),
                    ("fileName", FiscalJSON.string(archive.fileName)),
                    ("fileSha256", FiscalJSON.string(archive.fileSha256)),
                ]),
                terminalId: nil, operatorId: nil, now: now
            )
        }
        return file
    }
}
```

- [ ] **Step 5: Vérifier**

Run: `swift test --filter LocalArchiveAPITests` puis `swift test`
Expected: PASS. Si `zip.write(to:options:)` refuse `.withoutOverwriting` sur iOS, garder l'option (un `.tmp` existant signale un identifiant réutilisé, à ne pas écraser).

- [ ] **Step 6: Commit**

```bash
git add ios/Packages/PosKit
git commit -m "feat(ios): API des archives fiscales en mode autonome (création, liste, fichier, vérification)"
```

---

### Task 8: Vérification globale, suivi et documentation

**Files:**
- Modify: `docs/plans/ipad-standalone-3b-progress.md`
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift » et paragraphe sur le mode autonome)

- [ ] **Step 1: Suites complètes**

Run: `cd ios/Packages/PosKit && swift test` puis `cd ../../.. && dotnet test RestaurantPos.slnx`
Expected: tout PASS ; relever les nombres de tests (PosKit, .NET par projet).

- [ ] **Step 2: Vérifier que l'app compile**

Run: `cd ios && xcodegen generate && xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' build | tail -5`
Expected: `** BUILD SUCCEEDED **` (nouveau paramètre d'init avec valeur par défaut, aucun appel à modifier). Si le nom du projet ou du schéma diffère, le lire dans `ios/project.yml`.

- [ ] **Step 3: Compléter le suivi**

Dans `docs/plans/ipad-standalone-3b-progress.md` : statut et nombres de chaque tâche, findings de revue ouverts, puis une section « Risques hors 3b » reprenant les six puces de la Review Focus de ce plan.

- [ ] **Step 4: Compléter `CLAUDE.md`**

Dans le paragraphe « Parité fiscale .NET/Swift », ajouter :

« Les archives fiscales (table `FiscalArchives`, migration V9) sont portées dans `LocalFiscalRepository+ArchiveContent` (sélection et JSON indenté identique à `System.Text.Json`, via `IndentedJSON`), `LocalFiscalRepository+Archives` (chaîne globale, JET `ARCHIVE_CREATED`, vérification) et `LocalPosAPI+Archives` ; le ZIP est « stocké » (`ZipStoreWriter`, sans dépendance) et écrit dans `<base>-archives/` à côté de la base. `archive_reference.json` est **généré par le .NET** (`WRITE_ARCHIVE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter ArchiveReferenceFixtureTests`) et rejoué par `LocalArchiveContentReferenceTests` : les quatre fichiers de données sont identiques octet pour octet, le manifeste à l'identifiant et à l'horodatage près. Toute modification de `FiscalArchiveService`, de `ComputeArchiveHash` ou d'une entité sérialisée (`FiscalReceipt`, `PaymentTender`, `DailyFiscalClosure`, `FiscalPeriodClosure`, `TransactionJournalEntry`, `Money`) impose de la régénérer. »

Dans le paragraphe du mode autonome, compléter la phrase sur les tables propres à l'iPad : « la colonne `DailyFiscalClosures.CreatedAtUtc` est propre à l'iPad (absente de l'entité .NET, donc de `closures.json`) ».

- [ ] **Step 5: Commit, puis revue de branche**

```bash
git add -A docs CLAUDE.md
git commit -m "docs: suivi du lot 3b, règle de parité des archives fiscales"
```

Lancer ensuite la revue de la branche (`superpowers:requesting-code-review`) avant de fusionner.
