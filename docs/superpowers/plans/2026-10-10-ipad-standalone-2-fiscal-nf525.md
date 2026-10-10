# iPad standalone 2 : fiscal NF525 et sauvegarde chiffrée, plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le mode autonome de l'iPad émet de vrais reçus fiscaux NF525 signés comme le backend .NET (terminal `T01`), avec annulation, duplicata, JET et immuabilité SQL, et protège la chaîne par une sauvegarde chiffrée (export manuel, quotidienne automatique, restauration).

**Architecture:** Un moteur pur `FiscalHashing` + `FiscalReceiptMath` (port exact des formules et des montants du `CheckoutPaymentService` .NET) et un dépôt `LocalFiscalRepository` qui écrit reçu, règlements et journal dans la transaction de l'appelant. Une migration V5 crée `FiscalReceipts`, `PaymentTenders` et `JournalEntries` avec des triggers qui interdisent `UPDATE`/`DELETE`, puis retire `NonFiscalPayments`. La parité avec le .NET est prouvée par une fixture `fiscal_reference.json` générée par un test .NET et rejouée en Swift. La sauvegarde est un fichier AES-GCM dont la clé dérive d'une phrase secrète (PBKDF2).

**Tech Stack:** Swift 6 (PosKit, Swift Testing, CryptoKit, CommonCrypto, SQLite3), SwiftUI (iPadOS 17+), .NET 9 (xUnit, FluentAssertions, EF InMemory) pour la fixture. Aucune dépendance ajoutée.

**Spec:** `docs/superpowers/specs/2026-10-10-ipad-standalone-2-fiscal-design.md` (parente : `2026-10-06-ipad-standalone-design.md`). Le plan corrige deux points de la spec (tâche 10) : la table du JET s'appelle `JournalEntries` (nom du `DbSet` .NET) et il n'y a pas de colonne `DuplicateNumber` sur les reçus (le .NET numérote les duplicata par le JET).

## Global Constraints

- Swift 6, iPadOS 17+, logique dans le package `ios/Packages/PosKit` ; les vues SwiftUI (`ios/RestaurantPOS/`) ne parlent qu'aux stores et contrôleurs, jamais au réseau.
- Tests unitaires : Swift Testing (`@Suite`, `@Test`, `#expect`), jamais XCTest dans PosKit. Tests UI : XCTest/XCUITest dans `ios/RestaurantPOSUITests`.
- Montants en **centimes entiers**. Arrondi identique au .NET : `Money.roundAwayFromZero` (`MidpointRounding.AwayFromZero`) sauf là où le .NET appelle `Math.Round` sans mode (arrondi au pair, `.bankers`).
- Migrations : un **nouveau** script (`schemaV5`), jamais de modification d'un script publié ; `schemaV5` n'est publié qu'à la fusion de la branche, il peut donc encore évoluer pendant ce plan.
- Le poste autonome est toujours le terminal `T01` (`LocalPosAPI.standaloneTerminalId`).
- Identifiants : `Guid` stockés `TEXT` en **majuscules** (`UUID.uuidString`) ; dans une signature ou un JSON signé, un `Guid` s'écrit en **minuscules** (`Guid.ToString()`).
- Horodatage signé : `DateTimeOffset.ToString("O")` du .NET, soit `yyyy-MM-ddTHH:mm:ss.fffffff+00:00` (7 décimales, `+00:00`, pas `Z`). Côté Swift la date signée est arrondie à la milliseconde et stockée en texte ISO (`SQLDate`, 3 décimales, `Z`) ; l'horodatage signé se **recalcule** depuis la valeur stockée (`FiscalHashing.timestampText`).
- Signatures : SHA-256 UTF-8, hexadécimal **majuscule** pour les reçus, **minuscule** pour le JET ; départ `GENESIS_` + 64 zéros.
- Chaînes d'interface : clés du catalogue `ios/RestaurantPOS/Resources/Localizable.xcstrings`, avec `en`, `fr`, `ar` à l'état `translated` (le test `everyAppCatalogKeyIsTranslated` le vérifie) ; chiffres occidentaux forcés par l'application (ne pas formater de nombres à la main, utiliser `.formatted`).
- Éviter les noms de types qui entrent en collision avec SwiftUI/Foundation (`Category`, `GridLayout`…) et découper les grosses `body` SwiftUI.
- `ios/RestaurantPOS.xcodeproj` est généré : après tout ajout de fichier dans `ios/RestaurantPOS/`, lancer `cd ios && xcodegen generate`. Ne jamais éditer le `.xcodeproj`.
- .NET : `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` ; lancer `dotnet format RestaurantPos.slnx --include <fichiers touchés>` avant de compiler, ne pas reformater d'autres fichiers.
- Suivi : après chaque tâche, mettre à jour `docs/plans/ipad-standalone-2-progress.md` (statut, nombre de tests .NET / PosKit / UI, findings de revue ouverts). Avant de marquer une tâche faite, lancer toute la suite PosKit (`cd ios && ./scripts/test.sh unit`) ; les tâches 9 et 10 lancent aussi les tests UI.
- Base de référence avant la tâche 1 : PosKit 335, UI 47 (tous verts). Le nombre de tests .NET est relevé à l'étape 6 de la tâche 1.

## Review Focus

Entrées ou conditions que la spec laisse implicites et qu'une personne qui encaisse rencontrera ; chacune a son test dans la tâche indiquée.

1. Texte non ASCII, guillemets, `+`, `<`, `&` dans le JET (nom d'opérateur « Zoë », motif « Dérogation +10 % d'été ») : le JSON signé doit être octet pour octet celui de `System.Text.Json`, sinon la chaîne se vérifie côté iPad mais pas côté .NET (tâches 1 et 6).
2. Paiement en plusieurs fois, avec pourboire, remise globale en pourcentage ou en montant fixe, taux réduit à emporter : la TVA et le HT du reçu suivent exactement le .NET, **y compris ses incohérences actuelles** (le JSON de TVA ignore le taux à emporter et la remise globale ; le HT ignore la remise en montant fixe). Le plan les reproduit pour la parité et les consigne dans `CLAUDE.md` ; les corriger est une décision séparée, à prendre des deux côtés (tâche 2).
3. Commande à total nul (offerte, 100 % remisée) et paiement à règlements vides sur une commande à solde non nul (doit rester refusé) (tâche 5).
4. Annulation : reçu d'un autre terminal, annulation d'une annulation, double annulation (tâches 4 et 5).
5. Sauvegarde : mauvaise phrase, fichier tronqué ou modifié, fichier qui n'est pas une sauvegarde, dossier devenu indisponible (aucun fichier partiel), restauration d'une base dont la chaîne est rompue (tâches 7 et 8).
6. Deux reçus dans la même milliseconde : la séquence, pas l'horloge, ordonne la chaîne (tâche 4).

---

### Task 1: `FiscalHashing`, `FiscalJSON` et vecteurs de hachage du .NET

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift`
- Create: `tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json` (généré)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalHashingTests.swift`
- Create: `docs/plans/ipad-standalone-2-progress.md`

**Interfaces:**
- Consumes: `FiscalHashing` (.NET, `src/RestaurantPos.Infrastructure/Services/FiscalHashing.cs`), `SQLDate` (PosKit).
- Produces (internes à PosKit) :
  - `enum FiscalHashing` : `static let genesisHash: String`, `static func receiptHash(previousHash: String, terminalId: String, sequence: Int, amountCents: Int, timestamp: String, taxBreakdownJson: String) -> String`, `static func jetHash(previousHash: String, chainSequence: Int, eventType: String, timestamp: String, terminalId: String?, operatorId: UUID?, payloadJson: String) -> String`, `static func guidText(_ id: UUID) -> String`, `static func roundedToMilliseconds(_ date: Date) -> Date`, `static func timestampText(_ date: Date) -> String`.
  - `enum FiscalJSON` : `static func string(_ value: String) -> String`, `static func guid(_ id: UUID) -> String`, `static func dateTime(_ date: Date) -> String`, `static func object(_ fields: [(String, String)]) -> String` (valeurs déjà au format JSON).
  - Fixture `fiscal_reference.json` : `{"hashVectors": {genesisHash, receipts[], jet[], jsonStrings[], dateTimes[]}}` (la tâche 2 y ajoute `taxScenarios`).

- [ ] **Step 1: Écrire le test .NET qui produit et contrôle la fixture**

Créer `tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs` :
```csharp
using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture de référence partagée avec l'iPad (mode autonome) : le Swift rejoue ces vecteurs et doit obtenir les mêmes signatures.
/// Régénérer avec <c>WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests</c>
/// après toute modification fiscale du .NET, puis rejouer les tests PosKit.
/// </summary>
public class FiscalReferenceFixtureTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "fiscal_reference.json");
    }

    private static DateTimeOffset At(int hour, int minute, int second, int millisecond) =>
        new(2026, 10, 10, hour, minute, second, millisecond, TimeSpan.Zero);

    private static object HashVectors()
    {
        string genesis = NF525FiscalAuditService.GenesisHash;
        var operatorId = Guid.Parse("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9");
        var documentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var receiptInputs = new (long Sequence, long Amount, DateTimeOffset At, string Tax)[]
        {
            (1, 1250, At(9, 0, 0, 123), "{\"10\":114}"),
            (2, 4000, At(9, 5, 30, 7), "{\"10\":364,\"5.5\":88}"),
            (3, -1250, At(9, 6, 0, 999), "{\"10\":-114}"),
        };
        var receipts = new List<object>();
        string previous = genesis;
        foreach (var r in receiptInputs)
        {
            string hash = FiscalHashing.ComputeReceiptHash(previous, "T01", r.Sequence, r.Amount, r.At, r.Tax);
            receipts.Add(new
            {
                previousHash = previous,
                terminalId = "T01",
                sequence = r.Sequence,
                amountCents = r.Amount,
                timestamp = r.At.ToString("O"),
                taxBreakdownJson = r.Tax,
                hash,
            });
            previous = hash;
        }

        var jetInputs = new (string EventType, DateTimeOffset At, string? Terminal, Guid? Operator, string Payload)[]
        {
            ("RECEIPT_VOIDED", At(9, 6, 0, 999), "T01", operatorId,
                JsonSerializer.Serialize(new { OriginalReceiptId = documentId, TotalTtcCents = 1250L })),
            ("EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED", At(9, 7, 0, 0), "T01", operatorId,
                JsonSerializer.Serialize(new { Operator = "Zoë \"Chef\" <A&B>", Reason = "Dérogation +10% d'été", DurationMinutes = 60 })),
            ("DUPLICATE_PRINTED", At(9, 8, 0, 500), null, null,
                JsonSerializer.Serialize(new { documentId, documentType = "receipt", duplicateNumber = 1, printQueued = false })),
        };
        var jet = new List<object>();
        previous = genesis;
        long chainSequence = 0;
        foreach (var e in jetInputs)
        {
            chainSequence++;
            string hash = FiscalHashing.ComputeJetHash(previous, chainSequence, e.EventType, e.At, e.Terminal, e.Operator, e.Payload);
            jet.Add(new
            {
                previousHash = previous,
                chainSequence,
                eventType = e.EventType,
                timestamp = e.At.ToString("O"),
                terminalId = e.Terminal,
                operatorId = e.Operator?.ToString(),
                payloadJson = e.Payload,
                hash,
            });
            previous = hash;
        }

        var jsonStrings = new[] { "plain", "Zoë \"Chef\" <A&B>", "Dérogation +10% d'été", "a\\b\tc\nd", "pizza 🍕 `x`" }
            .Select(s => new { input = s, expected = JsonSerializer.Serialize(s) })
            .ToList();

        var dateTimes = new[] { At(9, 0, 0, 0), At(9, 0, 0, 120), At(9, 0, 0, 123) }
            .Select(d => new { timestamp = d.ToString("O"), expected = JsonSerializer.Serialize(d) })
            .ToList();

        return new { genesisHash = genesis, receipts, jet, jsonStrings, dateTimes };
    }

    public static Task<string> BuildAsync() =>
        Task.FromResult(JsonSerializer.Serialize(new { hashVectors = HashVectors() }, Options));

    [Fact]
    public async Task FixtureMatchesTheCurrentBackend()
    {
        string actual = (await BuildAsync()).ReplaceLineEndings("\n") + "\n";
        string path = FixturePath();
        if (Environment.GetEnvironmentVariable("WRITE_FISCAL_FIXTURE") == "1")
        {
            await File.WriteAllTextAsync(path, actual);
        }

        (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n").Should().Be(
            actual,
            "la fixture fiscale doit refléter le .NET : WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests");
    }
}
```

- [ ] **Step 2: Générer la fixture puis vérifier qu'elle est stable**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs
WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests
dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests
git status --short
```
Expected : les deux exécutions `Passed: 1` ; `git status` montre le seul fichier de test et la fixture `fiscal_reference.json` (aucun fichier reformaté en plus : sinon `git checkout` ces fichiers). Dans la fixture : les `timestamp` se terminent par `…0000+00:00`, les `hash` des reçus sont en majuscules, ceux du JET en minuscules, `operatorId` est en minuscules.

- [ ] **Step 3: Écrire le test Swift (il échoue : `FiscalHashing` n'existe pas)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalHashingTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Parité de hachage fiscal avec le .NET")
struct FiscalHashingTests {
    struct Reference: Decodable {
        struct Receipt: Decodable {
            let previousHash: String
            let terminalId: String
            let sequence: Int
            let amountCents: Int
            let timestamp: String
            let taxBreakdownJson: String
            let hash: String
        }
        struct Jet: Decodable {
            let previousHash: String
            let chainSequence: Int
            let eventType: String
            let timestamp: String
            let terminalId: String?
            let operatorId: String?
            let payloadJson: String
            let hash: String
        }
        struct Expectation: Decodable {
            let input: String?
            let timestamp: String?
            let expected: String
        }
        struct HashVectors: Decodable {
            let genesisHash: String
            let receipts: [Receipt]
            let jet: [Jet]
            let jsonStrings: [Expectation]
            let dateTimes: [Expectation]
        }
        let hashVectors: HashVectors
    }

    static func reference() throws -> Reference.HashVectors {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fiscal_reference")).hashVectors
    }

    /// `2026-10-10T09:00:00.1230000+00:00` → date à la milliseconde.
    static func date(_ text: String) throws -> Date {
        try #require(SQLDate.parse(text.replacingOccurrences(of: "0000+00:00", with: "Z")))
    }

    @Test func genesisHashMatches() throws {
        #expect(FiscalHashing.genesisHash == (try Self.reference()).genesisHash)
    }

    @Test func receiptHashesMatchTheBackend() throws {
        for vector in try Self.reference().receipts {
            let date = try Self.date(vector.timestamp)
            #expect(FiscalHashing.timestampText(date) == vector.timestamp)
            let hash = FiscalHashing.receiptHash(
                previousHash: vector.previousHash, terminalId: vector.terminalId, sequence: vector.sequence,
                amountCents: vector.amountCents, timestamp: FiscalHashing.timestampText(date), taxBreakdownJson: vector.taxBreakdownJson
            )
            #expect(hash == vector.hash)
        }
    }

    @Test func journalHashesMatchTheBackend() throws {
        for vector in try Self.reference().jet {
            let date = try Self.date(vector.timestamp)
            let hash = FiscalHashing.jetHash(
                previousHash: vector.previousHash, chainSequence: vector.chainSequence, eventType: vector.eventType,
                timestamp: FiscalHashing.timestampText(date), terminalId: vector.terminalId,
                operatorId: vector.operatorId.flatMap { UUID(uuidString: $0) }, payloadJson: vector.payloadJson
            )
            #expect(hash == vector.hash)
        }
    }

    @Test func jsonStringsAreEscapedLikeSystemTextJson() throws {
        for vector in try Self.reference().jsonStrings {
            #expect(FiscalJSON.string(try #require(vector.input)) == vector.expected)
        }
    }

    @Test func dateTimesAreWrittenLikeSystemTextJson() throws {
        for vector in try Self.reference().dateTimes {
            #expect(FiscalJSON.dateTime(try Self.date(try #require(vector.timestamp))) == vector.expected)
        }
    }

    @Test func guidsAreLowercaseInSignedJson() {
        let id = UUID(uuidString: "0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9")!
        #expect(FiscalHashing.guidText(id) == "0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9")
        #expect(FiscalJSON.object([("Id", FiscalJSON.guid(id)), ("N", "3")]) == "{\"Id\":\"0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9\",\"N\":3}")
    }

    @Test func millisecondRoundingIsIdempotentAcrossAStoredDate() throws {
        let now = Date(timeIntervalSince1970: 1_760_087_880.1234)
        let rounded = FiscalHashing.roundedToMilliseconds(now)
        let stored = try #require(SQLDate.parse(SQLDate.format(rounded)))
        #expect(FiscalHashing.timestampText(stored) == FiscalHashing.timestampText(rounded))
        #expect(FiscalHashing.roundedToMilliseconds(stored) == stored)
    }
}
```

- [ ] **Step 4: Lancer le test pour le voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter FiscalHashingTests`
Expected : erreur de compilation « cannot find 'FiscalHashing' in scope ».

- [ ] **Step 5: Implémenter `FiscalHashing` et `FiscalJSON`**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift` :
```swift
import CryptoKit
import Foundation

/// Miroir de `FiscalHashing.cs` : SHA-256 d'une chaîne UTF-8 séparée par `|`. Reçus en hexadécimal majuscule, journal (JET) en minuscule.
enum FiscalHashing {
    static let genesisHash = "GENESIS_" + String(repeating: "0", count: 64)

    static func sha256(_ raw: String, lowercase: Bool) -> String {
        SHA256.hash(data: Data(raw.utf8)).map { String(format: lowercase ? "%02x" : "%02X", $0) }.joined()
    }

    static func receiptHash(previousHash: String, terminalId: String, sequence: Int, amountCents: Int, timestamp: String, taxBreakdownJson: String) -> String {
        sha256("\(previousHash)|\(terminalId)|\(sequence)|\(amountCents)|\(timestamp)|\(taxBreakdownJson)", lowercase: false)
    }

    static func jetHash(previousHash: String, chainSequence: Int, eventType: String, timestamp: String, terminalId: String?, operatorId: UUID?, payloadJson: String) -> String {
        let operatorText = operatorId.map(guidText) ?? ""
        return sha256("\(previousHash)|\(chainSequence)|\(eventType)|\(timestamp)|\(terminalId ?? "")|\(operatorText)|\(payloadJson)", lowercase: true)
    }

    /// `Guid.ToString()` : minuscules.
    static func guidText(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// Date signée : arrondie à la milliseconde (la base ne garde que 3 décimales), de façon idempotente.
    static func roundedToMilliseconds(_ date: Date) -> Date {
        Date(timeIntervalSince1970: Double(Int64((date.timeIntervalSince1970 * 1000).rounded())) / 1000)
    }

    /// `DateTimeOffset.ToString("O")` d'une date UTC : `2026-10-10T09:00:00.1230000+00:00`.
    static func timestampText(_ date: Date) -> String {
        let milliseconds = Int64((date.timeIntervalSince1970 * 1000).rounded())
        let seconds = Int64((Double(milliseconds) / 1000).rounded(.down))
        let fraction = Int(milliseconds - seconds * 1000)
        let base = Date(timeIntervalSince1970: Double(seconds)).formatted(Date.ISO8601FormatStyle()) // 2026-10-10T09:00:00Z
        return String(base.dropLast()) + String(format: ".%03d0000+00:00", fraction)
    }
}

/// JSON écrit comme `System.Text.Json` par défaut : le texte signé doit être identique octet pour octet à celui du .NET.
enum FiscalJSON {
    /// Chaîne : ASCII imprimable seul, le reste (et `" < > & ' + \``) en `\uXXXX`.
    static func string(_ value: String) -> String {
        var out = "\""
        for unit in value.utf16 {
            switch unit {
            case 0x22: out += "\\u0022"
            case 0x5C: out += "\\\\"
            case 0x08: out += "\\b"
            case 0x09: out += "\\t"
            case 0x0A: out += "\\n"
            case 0x0C: out += "\\f"
            case 0x0D: out += "\\r"
            case 0x26, 0x27, 0x2B, 0x3C, 0x3E, 0x60: out += String(format: "\\u%04X", UInt32(unit))
            case 0x20...0x7E: out.unicodeScalars.append(UnicodeScalar(UInt8(unit)))
            default: out += String(format: "\\u%04X", UInt32(unit))
            }
        }
        return out + "\""
    }

    static func guid(_ id: UUID) -> String { "\"" + FiscalHashing.guidText(id) + "\"" }

    /// `DateTimeOffset` UTC : décimales sans zéros de fin, `+00:00` (ex. `"2026-10-10T09:00:00.12+00:00"`).
    static func dateTime(_ date: Date) -> String {
        let text = FiscalHashing.timestampText(date)
        guard let dot = text.firstIndex(of: ".") else { return "\"\(text)\"" }
        let head = text[..<dot]
        let digits = text[text.index(after: dot)...].prefix(7)
        let trimmed = String(digits.reversed().drop { $0 == "0" }.reversed())
        return "\"" + head + (trimmed.isEmpty ? "" : "." + trimmed) + "+00:00\""
    }

    /// Objet compact, dans l'ordre donné ; chaque valeur est déjà du JSON.
    static func object(_ fields: [(String, String)]) -> String {
        "{" + fields.map { "\"\($0.0)\":\($0.1)" }.joined(separator: ",") + "}"
    }
}
```

- [ ] **Step 6: Lancer les tests Swift et la suite .NET complète, créer le suivi**

Run :
```bash
cd ios/Packages/PosKit && swift test --filter FiscalHashingTests
cd ../.. && ./scripts/test.sh unit 2>&1 | tail -5
cd .. && dotnet test RestaurantPos.slnx 2>&1 | tail -8
```
Expected : `FiscalHashingTests` verts (7 tests) ; si `jsonStringsAreEscapedLikeSystemTextJson` échoue, comparer les deux chaînes dans la sortie et corriger `FiscalJSON.string` d'après la fixture (elle fait foi) ; PosKit = 335 + 7 = 342 ; noter le total .NET (`Passed: N`).

Créer `docs/plans/ipad-standalone-2-progress.md` :
```markdown
# iPad standalone 2 — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-10-ipad-standalone-2-fiscal-nf525.md`

Base de référence (avant tâche 1) : PosKit 335 · UI 47 · .NET <N relevé à l'étape 6>

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 FiscalHashing et vecteurs .NET | fait | <N> | 342 | - | aucun |
```
Remplacer `<N>` par le total relevé.

- [ ] **Step 7: Commit**

```bash
git add tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json ios/Packages/PosKit/Sources/PosKit/Local/FiscalHashing.swift ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalHashingTests.swift docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): hachage fiscal NF525 et fixture de référence générée par le .NET"
```

---

### Task 2: `FiscalReceiptMath` (montants, TVA et HT du reçu) et scénarios du .NET

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalReceiptMath.swift`
- Modify: `tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json` (régénéré)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalReceiptMathTests.swift`

**Interfaces:**
- Consumes: `OrderMath` (`totals`, `lineTotal`, `split`, `effectiveTaxRate`), `CartLine`, `ActiveOrder`, `Money.roundAwayFromZero`.
- Produces :
  - `struct FiscalReceiptAmounts: Equatable { let ttcCents: Int; let htCents: Int; let taxBreakdownJson: String }`
  - `enum FiscalReceiptMath` : `static func amounts(order: ActiveOrder, totalPaidCents: Int, tipCents: Int, priorFiscalTtcCents: Int) -> FiscalReceiptAmounts` ; `static func negatedTaxJson(_ json: String) -> String`.

- [ ] **Step 1: Ajouter les scénarios au générateur .NET**

Dans `FiscalReferenceFixtureTests.cs` : ajouter les `using` (`System.Globalization`, `Microsoft.EntityFrameworkCore`, `RestaurantPos.Application.Common.Interfaces`, `RestaurantPos.Domain.Entities`, `RestaurantPos.Domain.ValueObjects`, `RestaurantPos.Infrastructure.Persistence`), supprimer l'ancienne `BuildAsync` à une ligne et ajouter :
```csharp
    private sealed record LineSpec(long UnitPriceCents, int Quantity, decimal Rate, decimal? TakeawayRate = null, decimal DiscountPercent = 0, bool IsComp = false, long ModifiersExtraCents = 0);

    private sealed record Pay(PaymentMethod Method, long Amount, long Tendered);

    private sealed record ScenarioSpec(
        string Name,
        OrderDestination Destination,
        LineSpec[] Lines,
        Pay[][] Payments,
        DiscountType? GlobalType = null,
        decimal GlobalValue = 0,
        long TipCents = 0,
        int? VoidPayment = null);

    private static readonly ScenarioSpec[] Scenarios =
    [
        new("simple-eat-in", OrderDestination.EatIn, [new(2000, 2, 10m)], [[new(PaymentMethod.Cash, 4000, 5000)]], VoidPayment: 0),
        new("multi-rate", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m), new(450, 3, 5.5m)], [[new(PaymentMethod.CreditCard, 4380, 4380)]]),
        new("takeaway-reduced-rate", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m)], [[new(PaymentMethod.Cash, 1000, 1000)]]),
        new("global-percent-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [[new(PaymentMethod.Cash, 2700, 2700)]], DiscountType.Percentage, 10m),
        new("global-fixed-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [[new(PaymentMethod.Cash, 2700, 3000)]], DiscountType.FixedAmount, 3m),
        new("split-two-payments", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m)], [[new(PaymentMethod.CreditCard, 1000, 1000)], [new(PaymentMethod.Cash, 2030, 2030)]], VoidPayment: 1),
        new("tip", OrderDestination.EatIn, [new(1250, 1, 10m)], [[new(PaymentMethod.Cash, 1450, 2000)]], TipCents: 200),
        new("line-discount-comp-and-modifiers", OrderDestination.EatIn, [new(1000, 2, 10m, DiscountPercent: 10m), new(500, 1, 10m, IsComp: true), new(300, 1, 20m, ModifiersExtraCents: 50)], [[new(PaymentMethod.Cash, 2150, 2150)]]),
        new("zero-total", OrderDestination.EatIn, [new(500, 1, 10m, IsComp: true)], [[]]),
    ];

    private static string Rate(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static async Task<object> RunScenarioAsync(ScenarioSpec spec)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FiscalReference_" + Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new AppDbContext(options);
        var service = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));

        var order = new Order { TableNumber = "T01", Destination = spec.Destination, TipAmount = Money.FromCents(spec.TipCents) };
        foreach (var l in spec.Lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductName = "Produit",
                Quantity = l.Quantity,
                UnitPrice = Money.FromCents(l.UnitPriceCents),
                TaxRatePercent = l.Rate,
                TaxRateTakeawayPercent = l.TakeawayRate,
                DiscountPercent = l.DiscountPercent,
                IsComp = l.IsComp,
                ModifiersPriceExtra = Money.FromCents(l.ModifiersExtraCents),
            });
        }

        if (spec.GlobalType is { } type)
        {
            order.GlobalDiscountType = type;
            order.GlobalDiscountValue = spec.GlobalValue;
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var payments = new List<object>();
        var receiptIds = new List<Guid>();
        foreach (var tenders in spec.Payments)
        {
            var result = await service.ProcessPaymentTendersAsync(
                order.Id, "T01", tenders.Select(t => new PaymentTenderRequest(t.Method, t.Amount, t.Tendered)).ToList());
            result.IsSuccess.Should().BeTrue();
            var receipt = await db.FiscalReceipts.SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber);
            receiptIds.Add(receipt.Id);
            payments.Add(new
            {
                tenders = tenders.Select(t => new { method = (int)t.Method, amountCents = t.Amount, tenderedCents = t.Tendered }).ToList(),
                expected = new
                {
                    totalTtcCents = receipt.TotalTtcAmount.AmountInCents,
                    totalHtCents = receipt.TotalHtAmount.AmountInCents,
                    taxBreakdownJson = receipt.TaxBreakdownJson,
                    changeGivenCents = result.ChangeGivenCents,
                    remainingBalanceCents = result.RemainingBalanceCents,
                },
            });
        }

        object? voided = null;
        if (spec.VoidPayment is { } index)
        {
            (await service.VoidReceiptAsync(receiptIds[index], "T01", Guid.NewGuid())).IsSuccess.Should().BeTrue();
            var voidReceipt = await db.FiscalReceipts.SingleAsync(r => r.VoidedReceiptId == receiptIds[index]);
            voided = new
            {
                paymentIndex = index,
                totalTtcCents = voidReceipt.TotalTtcAmount.AmountInCents,
                totalHtCents = voidReceipt.TotalHtAmount.AmountInCents,
                taxBreakdownJson = voidReceipt.TaxBreakdownJson,
            };
        }

        return new
        {
            name = spec.Name,
            destination = (int)spec.Destination,
            globalDiscountType = spec.GlobalType.HasValue ? (int?)spec.GlobalType.Value : null,
            globalDiscountValue = Rate(spec.GlobalValue),
            tipCents = spec.TipCents,
            lines = spec.Lines.Select(l => new
            {
                unitPriceCents = l.UnitPriceCents,
                quantity = l.Quantity,
                taxRatePercent = Rate(l.Rate),
                takeawayRatePercent = l.TakeawayRate is { } t ? Rate(t) : null,
                discountPercent = Rate(l.DiscountPercent),
                isComp = l.IsComp,
                modifiersExtraCents = l.ModifiersExtraCents,
            }).ToList(),
            payments,
            voided,
        };
    }

    public static async Task<string> BuildAsync()
    {
        var taxScenarios = new List<object>();
        foreach (var spec in Scenarios)
        {
            taxScenarios.Add(await RunScenarioAsync(spec));
        }

        return JsonSerializer.Serialize(new { hashVectors = HashVectors(), taxScenarios }, Options);
    }
```

- [ ] **Step 2: Régénérer la fixture**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs
WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests
dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests
```
Expected : `Passed: 1` deux fois. Dans la fixture : `takeaway-reduced-rate` a `taxBreakdownJson` `{"10":91}` (taux 10, pas 5,5) ; `zero-total` a `{"10":0}` et `totalTtcCents` 0 ; `split-two-payments` a deux paiements et un bloc `voided`. Les hashes de `hashVectors` n'ont pas changé.

- [ ] **Step 3: Écrire le test Swift (il échoue : `FiscalReceiptMath` n'existe pas)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalReceiptMathTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Montants fiscaux du reçu : parité avec CheckoutPaymentService (.NET)")
struct FiscalReceiptMathTests {
    struct Reference: Decodable {
        struct Line: Decodable {
            let unitPriceCents: Int
            let quantity: Int
            let taxRatePercent: String
            let takeawayRatePercent: String?
            let discountPercent: String
            let isComp: Bool
            let modifiersExtraCents: Int
        }
        struct Tender: Decodable { let method: Int; let amountCents: Int; let tenderedCents: Int }
        struct Expected: Decodable {
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
        }
        struct Payment: Decodable { let tenders: [Tender]; let expected: Expected }
        struct Voided: Decodable {
            let paymentIndex: Int
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
        }
        struct Scenario: Decodable {
            let name: String
            let destination: Int
            let globalDiscountType: Int?
            let globalDiscountValue: String
            let tipCents: Int
            let lines: [Line]
            let payments: [Payment]
            let voided: Voided?
        }
        let taxScenarios: [Scenario]
    }

    static func scenarios() throws -> [Reference.Scenario] {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fiscal_reference")).taxScenarios
    }

    static func order(_ scenario: Reference.Scenario) throws -> ActiveOrder {
        let lines = scenario.lines.map { line in
            OrderLine(
                productId: UUID(), productName: "Produit", quantity: line.quantity, unitPrice: Money(cents: line.unitPriceCents),
                taxRatePercent: Decimal(string: line.taxRatePercent)!, isComp: line.isComp,
                discountPercent: Decimal(string: line.discountPercent)!, modifiersPriceExtra: Money(cents: line.modifiersExtraCents),
                taxRateTakeawayPercent: line.takeawayRatePercent.flatMap { Decimal(string: $0) }
            )
        }
        return ActiveOrder(
            tableNumber: "T01", lines: lines, globalDiscountType: scenario.globalDiscountType.flatMap { DiscountType(rawValue: $0) },
            globalDiscountValue: Decimal(string: scenario.globalDiscountValue)!, destination: try #require(OrderDestination(rawValue: scenario.destination))
        )
    }

    @Test func everyScenarioMatchesTheBackend() throws {
        let scenarios = try Self.scenarios()
        #expect(scenarios.count >= 9)
        for scenario in scenarios {
            let order = try Self.order(scenario)
            var priorFiscalTtc = 0
            for (index, payment) in scenario.payments.enumerated() {
                let paid = payment.tenders.reduce(0) { $0 + $1.amountCents }
                let amounts = FiscalReceiptMath.amounts(order: order, totalPaidCents: paid, tipCents: scenario.tipCents, priorFiscalTtcCents: priorFiscalTtc)
                #expect(amounts.ttcCents == payment.expected.totalTtcCents, "\(scenario.name)[\(index)] TTC")
                #expect(amounts.htCents == payment.expected.totalHtCents, "\(scenario.name)[\(index)] HT")
                #expect(amounts.taxBreakdownJson == payment.expected.taxBreakdownJson, "\(scenario.name)[\(index)] TVA")
                priorFiscalTtc += amounts.ttcCents
            }
            if let voided = scenario.voided {
                let original = scenario.payments[voided.paymentIndex].expected
                #expect(-original.totalTtcCents == voided.totalTtcCents && -original.totalHtCents == voided.totalHtCents, "\(scenario.name) annulation")
                #expect(FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson) == voided.taxBreakdownJson, "\(scenario.name) annulation TVA")
            }
        }
    }

    @Test func negatingAnEmptyOrZeroBreakdownIsStable() {
        #expect(FiscalReceiptMath.negatedTaxJson("{}") == "{}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":0}") == "{\"10\":0}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":364,\"5.5\":88}") == "{\"10\":-364,\"5.5\":-88}")
    }
}
```

- [ ] **Step 4: Lancer le test pour le voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter FiscalReceiptMathTests`
Expected : erreur de compilation « cannot find 'FiscalReceiptMath' in scope ».

- [ ] **Step 5: Implémenter `FiscalReceiptMath`**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/FiscalReceiptMath.swift` :
```swift
import Foundation

struct FiscalReceiptAmounts: Equatable {
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
}

/// Port exact des montants d'un reçu dans `CheckoutPaymentService.ProcessPaymentTendersAsync` (.NET), **incohérences comprises** :
/// - le JSON de TVA somme `OrderItem.TaxAmount` par `TaxRatePercent` de la ligne (jamais le taux à emporter, jamais la remise globale) ;
/// - le HT vient de `Order.CalculateTaxBreakdown` (taux effectif ; remise globale en pourcentage seulement, pas en montant fixe) ;
/// - pour un règlement partiel, TVA et HT sont mis à l'échelle par `fiscalPaid / orderTtc` avec `Math.Round` sans mode (arrondi au pair).
/// Corriger une de ces règles se fait des deux côtés (voir `CLAUDE.md`).
enum FiscalReceiptMath {
    /// - Parameters:
    ///   - totalPaidCents: somme des règlements de ce reçu (pourboire compris, rendu monnaie exclu).
    ///   - tipCents: pourboire enregistré sur la commande.
    ///   - priorFiscalTtcCents: TTC des reçus antérieurs de la commande, hors annulations et hors reçus annulés.
    static func amounts(order: ActiveOrder, totalPaidCents: Int, tipCents: Int, priorFiscalTtcCents: Int) -> FiscalReceiptAmounts {
        let cart = order.lines.map(CartLine.init(serverLine:))
        let orderTtc = OrderMath.totals(lines: cart, destination: order.destination, discount: order.globalDiscount).totalTtc.cents
        let totalDue = orderTtc + tipCents
        let hasTip = tipCents > 0
        let fiscalPaid = hasTip ? min(totalPaidCents, max(0, orderTtc - priorFiscalTtcCents)) : totalPaidCents
        let isPartial = hasTip ? (priorFiscalTtcCents > 0 || fiscalPaid < orderTtc) : totalPaidCents < totalDue
        let ratio: Decimal = (orderTtc > 0 && isPartial) ? Decimal(fiscalPaid) / Decimal(orderTtc) : 1

        var rates: [Decimal] = [] // ordre d'apparition, comme le `Dictionary` .NET
        var vat: [Decimal: Int] = [:]
        for line in cart {
            let itemVat = OrderMath.split(ttc: OrderMath.lineTotal(line), ratePercent: line.taxRatePercent).vat.cents
            let scaled = ratio == 1 ? itemVat : bankersRound(Decimal(itemVat) * ratio)
            if vat[line.taxRatePercent] == nil { rates.append(line.taxRatePercent) }
            vat[line.taxRatePercent, default: 0] += scaled
        }
        let json = "{" + rates.map { "\"\($0)\":\(vat[$0] ?? 0)" }.joined(separator: ",") + "}"

        let orderHt = htCents(cart, destination: order.destination, discount: order.globalDiscount)
        return FiscalReceiptAmounts(
            ttcCents: ratio == 1 ? orderTtc : fiscalPaid,
            htCents: ratio == 1 ? orderHt : bankersRound(Decimal(orderHt) * ratio),
            taxBreakdownJson: json
        )
    }

    /// Annulation : toutes les valeurs du JSON de TVA changent de signe, clés et ordre inchangés.
    static func negatedTaxJson(_ json: String) -> String {
        guard let regex = try? NSRegularExpression(pattern: "\"([^\"]+)\":(-?[0-9]+)") else { return json }
        let range = NSRange(json.startIndex..., in: json)
        let pairs = regex.matches(in: json, range: range).compactMap { match -> String? in
            guard let keyRange = Range(match.range(at: 1), in: json), let valueRange = Range(match.range(at: 2), in: json),
                  let value = Int(json[valueRange]) else { return nil }
            return "\"\(json[keyRange])\":\(-value)"
        }
        return "{" + pairs.joined(separator: ",") + "}"
    }

    /// `Order.TotalHt` : somme des bases HT par taux effectif.
    private static func htCents(_ cart: [CartLine], destination: OrderDestination, discount: GlobalDiscount?) -> Int {
        var groups: [Decimal: Int] = [:]
        for line in cart {
            groups[OrderMath.effectiveTaxRate(line, destination: destination), default: 0] += OrderMath.lineTotal(line).cents
        }
        return groups.reduce(0) { total, group in
            var groupTtc = group.value
            if let discount, discount.type == .percentage, discount.value > 0 {
                groupTtc = Money.roundAwayFromZero(Decimal(groupTtc) * (1 - discount.value / 100))
            }
            return total + OrderMath.split(ttc: Money(cents: max(0, groupTtc)), ratePercent: group.key).ht.cents
        }
    }

    /// `Math.Round(decimal)` du .NET : arrondi au pair.
    static func bankersRound(_ value: Decimal) -> Int {
        var input = value
        var result = Decimal()
        NSDecimalRound(&result, &input, 0, .bankers)
        return NSDecimalNumber(decimal: result).intValue
    }
}
```

- [ ] **Step 6: Lancer les tests**

Run : `cd ios/Packages/PosKit && swift test --filter "FiscalReceiptMathTests|FiscalHashingTests"`
Expected : tout vert. En cas d'écart sur un scénario, le message `#expect` nomme le scénario et le champ : corriger `FiscalReceiptMath` d'après la fixture, jamais l'inverse. Puis `cd ios && ./scripts/test.sh unit | tail -3` : 342 + 2 = 344.

- [ ] **Step 7: Suivi et commit**

Ajouter la ligne 2 au tableau de `docs/plans/ipad-standalone-2-progress.md` (fait, .NET inchangé, PosKit 344).
```bash
git add tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json ios/Packages/PosKit/Sources/PosKit/Local/FiscalReceiptMath.swift ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalReceiptMathTests.swift docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): montants fiscaux du reçu identiques au .NET (TVA, HT, règlements partiels)"
```

---

### Task 3: Migration V5 : tables fiscales et triggers d'immuabilité

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV5.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift` (liste `steps`)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalSchemaTests.swift`

**Interfaces:**
- Consumes: `LocalMigrator`, `SQLiteDatabase`.
- Produces: tables `FiscalReceipts`, `PaymentTenders`, `JournalEntries` (mêmes noms et colonnes que `AppDbContext`) ; `LocalMigrator.steps.count == 5`. `NonFiscalPayments` existe encore (retirée à la tâche 5, quand plus rien ne l'écrit).

- [ ] **Step 1: Écrire le test (il échoue : tables absentes)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalSchemaTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v5 : tables fiscales et immuabilité")
struct LocalFiscalSchemaTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    static let insertReceipt = """
    INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
        PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId)
    VALUES ('R1', 'T01', 'T01-000001', 'O1', 1, 1000, 909, '{}', 'GENESIS', 'SIG', '2026-10-10T09:00:00.000Z', 0, NULL)
    """

    @Test func migrationCreatesTheFiscalTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 5)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["FiscalReceipts", "PaymentTenders", "JournalEntries"] { #expect(tables.contains(expected)) }
    }

    @Test func upgradingAV4DatabaseKeepsItsData() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        for script in [LocalMigrator.schemaV1, LocalMigrator.schemaV2, LocalMigrator.schemaV3, LocalMigrator.schemaV4] { try db.exec(script) }
        try db.exec("PRAGMA user_version = 4")
        try db.run("INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES ('keep', '', 7)")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(try db.query("SELECT LastSequence FROM LocalCounters WHERE Name = 'keep'").first?.int("LastSequence") == 7)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'FiscalReceipts'").count == 1)
    }

    @Test func receiptSequencesAreUniquePerTerminal() throws {
        let db = try makeDB()
        try db.run(Self.insertReceipt)
        #expect(throws: SQLiteError.self) {
            try db.run(Self.insertReceipt.replacingOccurrences(of: "'R1'", with: "'R2'"))
        }
    }

    @Test func fiscalRowsCannotBeUpdatedOrDeleted() throws {
        let db = try makeDB()
        try db.run(Self.insertReceipt)
        try db.run("INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES ('P1', 'R1', 0, 1000, 1000, 0)")
        try db.run(
            """
            INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence, PreviousHash, OperatorId)
            VALUES ('J1', 1, 'T01', '2026-10-10T09:00:00.000Z', 'k', 'X', '{}', 'h', 1, 'GENESIS', NULL)
            """
        )
        let attempts: [(String, String)] = [
            ("UPDATE FiscalReceipts SET TotalTtcAmount = 1 WHERE Id = 'R1'", "Fiscal receipts are append-only and immutable"),
            ("DELETE FROM FiscalReceipts WHERE Id = 'R1'", "Fiscal receipts are append-only and immutable"),
            ("UPDATE PaymentTenders SET Amount = 1 WHERE Id = 'P1'", "Payment tenders are immutable"),
            ("DELETE FROM PaymentTenders WHERE Id = 'P1'", "Payment tenders are immutable"),
            ("UPDATE JournalEntries SET PayloadJson = '{\"x\":1}' WHERE Id = 'J1'", "Chained journal entries are immutable"),
            ("DELETE FROM JournalEntries WHERE Id = 'J1'", "Chained journal entries are immutable"),
        ]
        for (sql, message) in attempts {
            do {
                try db.run(sql)
                Issue.record("« \(sql) » aurait dû être refusé")
            } catch let error as SQLiteError {
                #expect(error.message.contains("NF525 INV-3") && error.message.contains(message), "\(sql) : \(error.message)")
            }
        }
        #expect(try db.query("SELECT TotalTtcAmount FROM FiscalReceipts").first?.int("TotalTtcAmount") == 1000)
    }
}
```

- [ ] **Step 2: Lancer le test pour le voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter LocalFiscalSchemaTests`
Expected : échecs (`steps.count >= 5` faux, tables absentes).

- [ ] **Step 3: Écrire le schéma V5**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV5.swift` :
```swift
import Foundation

extension LocalMigrator {
    /// Chaîne fiscale NF525 : mêmes tables et colonnes que `AppDbContext` (`FiscalReceipts`, `PaymentTenders`, `JournalEntries` = JET).
    /// Les triggers reprennent `FiscalImmutabilityInterceptor` (mêmes messages) : aucune ligne fiscale ne peut être modifiée ni supprimée.
    /// `IsVoid` est conservée pour la parité du schéma et n'est jamais écrite ; une annulation est un reçu lié par `VoidedReceiptId`.
    /// Les clôtures Z, de période et les archives viennent au sous-projet 3.
    static let schemaV5 = """
    CREATE TABLE FiscalReceipts (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        ReceiptNumber TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        SequenceNumber INTEGER NOT NULL,
        TotalTtcAmount INTEGER NOT NULL,
        TotalHtAmount INTEGER NOT NULL,
        TaxBreakdownJson TEXT NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        IsVoid INTEGER NOT NULL DEFAULT 0,
        VoidedReceiptId TEXT
    );
    CREATE UNIQUE INDEX IX_FiscalReceipts_TerminalId_SequenceNumber ON FiscalReceipts (TerminalId, SequenceNumber);
    CREATE INDEX IX_FiscalReceipts_VoidedReceiptId ON FiscalReceipts (VoidedReceiptId);
    CREATE INDEX IX_FiscalReceipts_OrderId ON FiscalReceipts (OrderId);

    CREATE TABLE PaymentTenders (
        Id TEXT NOT NULL PRIMARY KEY,
        FiscalReceiptId TEXT NOT NULL REFERENCES FiscalReceipts (Id),
        Method INTEGER NOT NULL,
        Amount INTEGER NOT NULL,
        Tendered INTEGER NOT NULL,
        ChangeGiven INTEGER NOT NULL
    );
    CREATE INDEX IX_PaymentTenders_FiscalReceiptId ON PaymentTenders (FiscalReceiptId);

    CREATE TABLE JournalEntries (
        Id TEXT NOT NULL PRIMARY KEY,
        LocalSequence INTEGER NOT NULL,
        TerminalId TEXT,
        OccurredAtUtc TEXT NOT NULL,
        IdempotencyKey TEXT NOT NULL,
        EventType TEXT NOT NULL,
        PayloadJson TEXT NOT NULL,
        EntryHash TEXT NOT NULL,
        ChainSequence INTEGER,
        PreviousHash TEXT,
        OperatorId TEXT
    );
    CREATE UNIQUE INDEX IX_JournalEntries_ChainSequence ON JournalEntries (ChainSequence) WHERE ChainSequence IS NOT NULL;

    CREATE TRIGGER TR_FiscalReceipts_NoUpdate BEFORE UPDATE ON FiscalReceipts
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal receipts are append-only and immutable.'); END;
    CREATE TRIGGER TR_FiscalReceipts_NoDelete BEFORE DELETE ON FiscalReceipts
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal receipts are append-only and immutable.'); END;
    CREATE TRIGGER TR_PaymentTenders_NoUpdate BEFORE UPDATE ON PaymentTenders
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Payment tenders are immutable.'); END;
    CREATE TRIGGER TR_PaymentTenders_NoDelete BEFORE DELETE ON PaymentTenders
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Payment tenders are immutable.'); END;
    CREATE TRIGGER TR_JournalEntries_NoUpdate BEFORE UPDATE ON JournalEntries WHEN OLD.ChainSequence IS NOT NULL
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Chained journal entries are immutable.'); END;
    CREATE TRIGGER TR_JournalEntries_NoDelete BEFORE DELETE ON JournalEntries WHEN OLD.ChainSequence IS NOT NULL
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Chained journal entries are immutable.'); END;
    """
}
```
Dans `LocalMigrator.swift`, remplacer `static let steps: [String] = [schemaV1, schemaV2, schemaV3, schemaV4]` par `static let steps: [String] = [schemaV1, schemaV2, schemaV3, schemaV4, schemaV5]`.

- [ ] **Step 4: Lancer les tests**

Run : `cd ios && ./scripts/test.sh unit 2>&1 | tail -5`
Expected : tout vert, PosKit 344 + 4 = 348. Si un test existant compte `steps.count` en dur (`== 4`), le remplacer par `LocalMigrator.steps.count` (`grep -rn "user_version\|steps.count" ios/Packages/PosKit/Tests`).

- [ ] **Step 5: Suivi et commit**

Ajouter la ligne 3 au suivi (PosKit 348).
```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV5.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalSchemaTests.swift docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): schéma v5, tables fiscales NF525 et triggers d'immuabilité"
```

---

### Task 4: `LocalFiscalRepository` : reçus chaînés, annulation, duplicata, JET, vérification

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalRepositoryTests.swift`

**Interfaces:**
- Consumes: `FiscalHashing`, `FiscalJSON`, `FiscalReceiptAmounts`, `FiscalReceiptMath.negatedTaxJson`, `SQLiteDatabase`, `PaymentMethod`.
- Produces :
  - `enum LocalJournalEvents` : `receiptVoided`, `duplicatePrinted`, `heldOrderVoided`, `happyHourActivated`, `happyHourStopped`, `backupCompleted`, `backupFailed`.
  - `struct FiscalTenderInput { let method: PaymentMethod; let amount: Int; let tendered: Int; let change: Int }`
  - `struct FiscalReceiptRecord: Equatable { id, orderId, terminalId, receiptNumber, sequence, ttcCents, htCents, taxBreakdownJson, signature }`
  - `struct LocalFiscalRepository { let db: SQLiteDatabase }` avec, **toutes à appeler dans la transaction de l'appelant** : `paidCents(orderId:)`, `priorFiscalTtcCents(orderId:)`, `appendReceipt(orderId:terminalId:amounts:tenders:now:)`, `voidReceipt(originalId:terminalId:operatorId:now:) -> FiscalReceiptRecord?` (nil = refusé, rien écrit), `receipt(id:)`, `receipt(identifier:)`, `appendJournal(eventType:payloadJson:terminalId:operatorId:now:)`, `lastJournalDate(eventType:)`, `nextDuplicateNumber(documentId:)`, `recordDuplicate(of:operatorId:now:) -> Int`, `verifyChains() -> [String]`.

- [ ] **Step 1: Écrire les tests (ils échouent : types absents)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalRepositoryTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalFiscalRepository : chaîne fiscale")
struct LocalFiscalRepositoryTests {
    private func makeRepository() throws -> (LocalFiscalRepository, SQLiteDatabase) {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return (LocalFiscalRepository(db: db), db)
    }

    private static let amounts = FiscalReceiptAmounts(ttcCents: 1250, htCents: 1136, taxBreakdownJson: "{\"10\":114}")
    private let cash = [FiscalTenderInput(method: .cash, amount: 1250, tendered: 2000, change: 750)]
    private let at = Date(timeIntervalSince1970: 1_760_087_880.123)

    @discardableResult
    private func append(_ repo: LocalFiscalRepository, order: UUID = UUID(), terminal: String = "T01", now: Date? = nil) throws -> FiscalReceiptRecord {
        try repo.appendReceipt(orderId: order, terminalId: terminal, amounts: Self.amounts, tenders: cash, now: now ?? at)
    }

    @Test func receiptsAreChainedAndSignedLikeTheBackend() throws {
        let (repo, db) = try makeRepository()
        let first = try append(repo)
        let second = try append(repo)
        #expect(first.receiptNumber == "T01-000001" && second.receiptNumber == "T01-000002" && second.sequence == 2)
        let rows = try db.query("SELECT * FROM FiscalReceipts ORDER BY SequenceNumber")
        #expect(rows[0].string("PreviousSignatureHash") == FiscalHashing.genesisHash)
        #expect(rows[1].string("PreviousSignatureHash") == first.signature)
        let expected = FiscalHashing.receiptHash(
            previousHash: FiscalHashing.genesisHash, terminalId: "T01", sequence: 1, amountCents: 1250,
            timestamp: FiscalHashing.timestampText(FiscalHashing.roundedToMilliseconds(at)), taxBreakdownJson: "{\"10\":114}"
        )
        #expect(first.signature == expected && rows[0].string("SignatureHash") == expected)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ?", [.uuid(first.id)]).first)
        #expect(tender.int("Amount") == 1250 && tender.int("Tendered") == 2000 && tender.int("ChangeGiven") == 750 && tender.int("Method") == 0)
    }

    @Test func eachTerminalHasItsOwnChain() throws {
        let (repo, _) = try makeRepository()
        try append(repo)
        let other = try append(repo, terminal: "T02")
        #expect(other.sequence == 1 && other.receiptNumber == "T02-000001")
    }

    @Test func twoReceiptsInTheSameMillisecondStayOrderedBySequence() throws {
        let (repo, _) = try makeRepository()
        let a = try append(repo, now: at), b = try append(repo, now: at)
        #expect(a.sequence == 1 && b.sequence == 2)
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func paidAmountIsTheNetOfTendersAndVoids() throws {
        let (repo, _) = try makeRepository()
        let order = UUID()
        #expect(try repo.paidCents(orderId: order) == 0 && repo.priorFiscalTtcCents(orderId: order) == 0)
        let sale = try append(repo, order: order)
        #expect(try repo.paidCents(orderId: order) == 1250 && repo.priorFiscalTtcCents(orderId: order) == 1250)
        _ = try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at)
        #expect(try repo.paidCents(orderId: order) == 0 && repo.priorFiscalTtcCents(orderId: order) == 0)
    }

    @Test func voidingWritesANegativeLinkedReceiptAndAJournalEntry() throws {
        let (repo, db) = try makeRepository()
        let sale = try append(repo)
        let operatorId = UUID()
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: operatorId, now: at))
        #expect(voided.receiptNumber == "T01-VOID-000002" && voided.ttcCents == -1250 && voided.htCents == -1136 && voided.taxBreakdownJson == "{\"10\":-114}")
        let row = try #require(try db.query("SELECT * FROM FiscalReceipts WHERE Id = ?", [.uuid(voided.id)]).first)
        #expect(row.uuid("VoidedReceiptId") == sale.id && row.int("IsVoid") == 0)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ?", [.uuid(voided.id)]).first)
        #expect(tender.int("Amount") == -1250 && tender.int("Tendered") == -2000 && tender.int("ChangeGiven") == -750)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries").first)
        #expect(entry.string("EventType") == "RECEIPT_VOIDED" && entry.int("ChainSequence") == 1 && entry.uuid("OperatorId") == operatorId)
        let payload = try #require(entry.string("PayloadJson"))
        #expect(payload.contains("\"OriginalReceiptNumber\":\"T01-000001\"") && payload.contains("\"VoidReceiptNumber\":\"T01-VOID-000002\"") && payload.contains("\"TotalTtcCents\":1250"))
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func voidsAreRefusedWithoutWritingAnything() throws {
        let (repo, db) = try makeRepository()
        let sale = try append(repo)
        let otherTerminal = try append(repo, terminal: "T02")
        #expect(try repo.voidReceipt(originalId: otherTerminal.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // autre terminal
        #expect(try repo.voidReceipt(originalId: UUID(), terminalId: "T01", operatorId: UUID(), now: at) == nil) // inconnu
        let voided = try #require(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at))
        #expect(try repo.voidReceipt(originalId: sale.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // déjà annulé
        #expect(try repo.voidReceipt(originalId: voided.id, terminalId: "T01", operatorId: UUID(), now: at) == nil) // annulation d'une annulation
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 3)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n") == 1)
    }

    @Test func aFailureWhileWritingTendersRollsTheWholeReceiptBack() throws {
        let (repo, db) = try makeRepository()
        try db.exec("CREATE TEMP TRIGGER fail_tender BEFORE INSERT ON main.PaymentTenders BEGIN SELECT RAISE(ABORT, 'boom'); END;")
        #expect(throws: SQLiteError.self) { try db.transaction { try append(repo) } }
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 0)
    }

    @Test func journalEntriesAreChainedAndSignedLikeTheBackend() throws {
        let (repo, db) = try makeRepository()
        let operatorId = UUID()
        try repo.appendJournal(eventType: "TEST", payloadJson: FiscalJSON.object([("Reason", FiscalJSON.string("Dérogation +10 %"))]), terminalId: "T01", operatorId: operatorId, now: at)
        try repo.appendJournal(eventType: "TEST2", payloadJson: "{}", terminalId: nil, operatorId: nil, now: at)
        let rows = try db.query("SELECT * FROM JournalEntries ORDER BY ChainSequence")
        let firstHash = FiscalHashing.jetHash(
            previousHash: FiscalHashing.genesisHash, chainSequence: 1, eventType: "TEST",
            timestamp: FiscalHashing.timestampText(FiscalHashing.roundedToMilliseconds(at)), terminalId: "T01", operatorId: operatorId,
            payloadJson: "{\"Reason\":\"D\\u00E9rogation \\u002B10 %\"}"
        )
        #expect(rows[0].string("EntryHash") == firstHash && rows[1].string("PreviousHash") == firstHash && rows[1].int("ChainSequence") == 2)
        #expect(try repo.verifyChains().isEmpty)
    }

    @Test func duplicatesAreNumberedFromTheJournalPerDocument() throws {
        let (repo, db) = try makeRepository()
        let a = try append(repo), b = try append(repo)
        #expect(try repo.nextDuplicateNumber(documentId: a.id) == 1)
        #expect(try repo.recordDuplicate(of: a, operatorId: UUID(), now: at) == 1)
        #expect(try repo.recordDuplicate(of: a, operatorId: UUID(), now: at) == 2)
        #expect(try repo.nextDuplicateNumber(documentId: b.id) == 1)
        let payload = try #require(try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'DUPLICATE_PRINTED' ORDER BY ChainSequence").first?.string("PayloadJson"))
        #expect(payload.contains("\"documentType\":\"receipt\"") && payload.contains("\"duplicateNumber\":1") && payload.contains("\"printQueued\":false") && payload.contains("\"receiptNumber\":\"T01-000001\""))
    }

    @Test func receiptsAreFoundByIdOrNumber() throws {
        let (repo, _) = try makeRepository()
        let sale = try append(repo)
        #expect(try repo.receipt(identifier: sale.id.uuidString)?.id == sale.id)
        #expect(try repo.receipt(identifier: sale.id.uuidString.lowercased())?.id == sale.id)
        #expect(try repo.receipt(identifier: "T01-000001")?.id == sale.id)
        #expect(try repo.receipt(identifier: "nope") == nil)
    }

    @Test func verifyChainsDetectsTamperingAndGaps() throws {
        let (repo, db) = try makeRepository()
        try append(repo)
        try append(repo)
        try append(repo)
        try repo.appendJournal(eventType: "TEST", payloadJson: "{}", terminalId: "T01", operatorId: nil, now: at)
        #expect(try repo.verifyChains().isEmpty)

        try db.exec("DROP TRIGGER TR_FiscalReceipts_NoUpdate; DROP TRIGGER TR_FiscalReceipts_NoDelete; DROP TRIGGER TR_JournalEntries_NoUpdate")
        try db.run("UPDATE FiscalReceipts SET TotalTtcAmount = 1 WHERE SequenceNumber = 2")
        try db.run("UPDATE JournalEntries SET PayloadJson = '{\"x\":1}'")
        let issues = try repo.verifyChains()
        #expect(issues.contains { $0.contains("signature_mismatch") && $0.contains("T01") })
        #expect(issues.contains { $0.contains("jet") && $0.contains("signature_mismatch") })

        try db.run("DELETE FROM FiscalReceipts WHERE SequenceNumber = 2")
        #expect(try repo.verifyChains().contains { $0.contains("sequence_gap") })
    }
}
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter LocalFiscalRepositoryTests`
Expected : erreur de compilation « cannot find 'LocalFiscalRepository' in scope ».

- [ ] **Step 3: Écrire les constantes d'événements**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift` :
```swift
import Foundation

/// Types d'événements du JET : mêmes valeurs que `JournalEventTypes.cs`. `BACKUP_*` n'existent que sur l'iPad autonome.
enum LocalJournalEvents {
    static let receiptVoided = "RECEIPT_VOIDED"
    static let duplicatePrinted = "DUPLICATE_PRINTED"
    static let heldOrderVoided = "EVENT_HELD_ORDER_VOIDED"
    static let happyHourActivated = "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED"
    static let happyHourStopped = "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED"
    static let backupCompleted = "BACKUP_COMPLETED"
    static let backupFailed = "BACKUP_FAILED"
}
```

- [ ] **Step 4: Implémenter le dépôt**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift` :
```swift
import Foundation

struct FiscalTenderInput {
    let method: PaymentMethod
    let amount: Int
    let tendered: Int
    let change: Int
}

struct FiscalReceiptRecord: Equatable {
    let id: UUID
    let orderId: UUID
    let terminalId: String
    let receiptNumber: String
    let sequence: Int
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
    let signature: String
}

/// Écrit la chaîne fiscale NF525 (reçus par terminal, journal global). **Aucune méthode n'ouvre de transaction** : l'appelant (`LocalPosAPI`)
/// enveloppe la vente, le reçu et le journal dans un seul `db.transaction`, ce qui sérialise aussi la lecture de fin de chaîne.
struct LocalFiscalRepository {
    let db: SQLiteDatabase

    // MARK: Règlements

    /// Réglé sur la commande : somme nette des règlements (les annulations sont négatives).
    func paidCents(orderId: UUID) throws -> Int {
        try db.query(
            "SELECT COALESCE(SUM(t.Amount), 0) AS total FROM PaymentTenders t JOIN FiscalReceipts r ON r.Id = t.FiscalReceiptId WHERE r.OrderId = ?",
            [.uuid(orderId)]
        ).first?.int("total") ?? 0
    }

    /// TTC des reçus de la commande, hors annulations et hors reçus annulés (`priorFiscalTtcCents` du .NET).
    func priorFiscalTtcCents(orderId: UUID) throws -> Int {
        try db.query(
            """
            SELECT COALESCE(SUM(TotalTtcAmount), 0) AS total FROM FiscalReceipts
            WHERE OrderId = ? AND VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL)
            """,
            [.uuid(orderId)]
        ).first?.int("total") ?? 0
    }

    // MARK: Reçus

    @discardableResult
    func appendReceipt(orderId: UUID, terminalId: String, amounts: FiscalReceiptAmounts, tenders: [FiscalTenderInput], now: Date) throws -> FiscalReceiptRecord {
        try insertReceipt(orderId: orderId, terminalId: terminalId, amounts: amounts, tenders: tenders, voidedReceiptId: nil, now: now)
    }

    /// Contre-passation. Renvoie `nil` sans rien écrire si le reçu est inconnu, d'un autre terminal, lui-même une annulation ou déjà annulé.
    func voidReceipt(originalId: UUID, terminalId: String, operatorId: UUID, now: Date) throws -> FiscalReceiptRecord? {
        guard let original = try receipt(id: originalId), original.terminalId == terminalId,
              try db.query("SELECT VoidedReceiptId FROM FiscalReceipts WHERE Id = ?", [.uuid(originalId)]).first?.uuid("VoidedReceiptId") == nil,
              try db.query("SELECT 1 AS x FROM FiscalReceipts WHERE VoidedReceiptId = ?", [.uuid(originalId)]).isEmpty
        else { return nil }
        let tenders = try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ? ORDER BY rowid", [.uuid(originalId)]).map {
            FiscalTenderInput(
                method: PaymentMethod(rawValue: $0.int("Method") ?? 0) ?? .cash,
                amount: -($0.int("Amount") ?? 0), tendered: -($0.int("Tendered") ?? 0), change: -($0.int("ChangeGiven") ?? 0)
            )
        }
        let amounts = FiscalReceiptAmounts(
            ttcCents: -original.ttcCents, htCents: -original.htCents, taxBreakdownJson: FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson)
        )
        let voided = try insertReceipt(orderId: original.orderId, terminalId: terminalId, amounts: amounts, tenders: tenders, voidedReceiptId: originalId, now: now)
        try appendJournal(
            eventType: LocalJournalEvents.receiptVoided,
            payloadJson: FiscalJSON.object([
                ("OriginalReceiptId", FiscalJSON.guid(original.id)),
                ("OriginalReceiptNumber", FiscalJSON.string(original.receiptNumber)),
                ("VoidReceiptId", FiscalJSON.guid(voided.id)),
                ("VoidReceiptNumber", FiscalJSON.string(voided.receiptNumber)),
                ("TotalTtcCents", "\(original.ttcCents)"),
            ]),
            terminalId: terminalId, operatorId: operatorId, now: now
        )
        return voided
    }

    func receipt(id: UUID) throws -> FiscalReceiptRecord? {
        try db.query("SELECT * FROM FiscalReceipts WHERE Id = ?", [.uuid(id)]).first.flatMap(record)
    }

    /// Identifiant (GUID) ou numéro de reçu (`T01-000001`), comme `POST /api/checkout/receipts/{receiptId}/reprint`.
    func receipt(identifier: String) throws -> FiscalReceiptRecord? {
        if let id = UUID(uuidString: identifier), let found = try receipt(id: id) { return found }
        return try db.query("SELECT * FROM FiscalReceipts WHERE ReceiptNumber = ?", [.text(identifier)]).first.flatMap(record)
    }

    private func insertReceipt(orderId: UUID, terminalId: String, amounts: FiscalReceiptAmounts, tenders: [FiscalTenderInput], voidedReceiptId: UUID?, now: Date) throws -> FiscalReceiptRecord {
        let tail = try db.query(
            "SELECT SequenceNumber, SignatureHash FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber DESC LIMIT 1", [.text(terminalId)]
        ).first
        let sequence = (tail?.int("SequenceNumber") ?? 0) + 1
        let previousHash = tail?.string("SignatureHash") ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(now)
        let signature = FiscalHashing.receiptHash(
            previousHash: previousHash, terminalId: terminalId, sequence: sequence, amountCents: amounts.ttcCents,
            timestamp: FiscalHashing.timestampText(timestamp), taxBreakdownJson: amounts.taxBreakdownJson
        )
        let number = (voidedReceiptId == nil ? "\(terminalId)-" : "\(terminalId)-VOID-") + String(format: "%06d", sequence)
        let id = UUID()
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)
            """,
            [.uuid(id), .text(terminalId), .text(number), .uuid(orderId), .integer(sequence), .integer(amounts.ttcCents), .integer(amounts.htCents),
             .text(amounts.taxBreakdownJson), .text(previousHash), .text(signature), .date(timestamp), .uuid(voidedReceiptId)]
        )
        for tender in tenders {
            try db.run(
                "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                [.uuid(UUID()), .uuid(id), .integer(tender.method.rawValue), .integer(tender.amount), .integer(tender.tendered), .integer(tender.change)]
            )
        }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: terminalId, receiptNumber: number, sequence: sequence, ttcCents: amounts.ttcCents,
            htCents: amounts.htCents, taxBreakdownJson: amounts.taxBreakdownJson, signature: signature
        )
    }

    private func record(_ row: SQLRow) -> FiscalReceiptRecord? {
        guard let id = row.uuid("Id"), let orderId = row.uuid("OrderId") else { return nil }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: row.string("TerminalId") ?? "", receiptNumber: row.string("ReceiptNumber") ?? "",
            sequence: row.int("SequenceNumber") ?? 0, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
            taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", signature: row.string("SignatureHash") ?? ""
        )
    }

    // MARK: Journal technique (JET)

    func appendJournal(eventType: String, payloadJson: String, terminalId: String?, operatorId: UUID?, now: Date) throws {
        let tail = try db.query("SELECT ChainSequence, EntryHash FROM JournalEntries WHERE ChainSequence IS NOT NULL ORDER BY ChainSequence DESC LIMIT 1").first
        let sequence = (tail?.int("ChainSequence") ?? 0) + 1
        let previousHash = tail?.string("EntryHash") ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(now)
        let hash = FiscalHashing.jetHash(
            previousHash: previousHash, chainSequence: sequence, eventType: eventType, timestamp: FiscalHashing.timestampText(timestamp),
            terminalId: terminalId, operatorId: operatorId, payloadJson: payloadJson
        )
        try db.run(
            """
            INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence, PreviousHash, OperatorId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .integer(sequence), .string(terminalId), .date(timestamp), .text(UUID().uuidString.lowercased()), .text(eventType),
             .text(payloadJson), .text(hash), .integer(sequence), .text(previousHash), .uuid(operatorId)]
        )
    }

    func lastJournalDate(eventType: String) throws -> Date? {
        try db.query("SELECT OccurredAtUtc FROM JournalEntries WHERE EventType = ? ORDER BY ChainSequence DESC LIMIT 1", [.text(eventType)]).first?.date("OccurredAtUtc")
    }

    // MARK: Duplicata

    /// Prochain n° de duplicata du document : le JET fait foi (`DuplicateNumbering.NextAsync` du .NET).
    func nextDuplicateNumber(documentId: UUID) throws -> Int {
        let needle = "%" + FiscalHashing.guidText(documentId) + "%"
        let count = try db.query(
            "SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = ? AND PayloadJson LIKE ?", [.text(LocalJournalEvents.duplicatePrinted), .text(needle)]
        ).first?.int("n") ?? 0
        return count + 1
    }

    /// Consigne un duplicata de reçu (sans impression : `printQueued` faux) et renvoie son numéro.
    @discardableResult
    func recordDuplicate(of receipt: FiscalReceiptRecord, operatorId: UUID?, now: Date) throws -> Int {
        let number = try nextDuplicateNumber(documentId: receipt.id)
        try appendJournal(
            eventType: LocalJournalEvents.duplicatePrinted,
            payloadJson: FiscalJSON.object([
                ("documentId", FiscalJSON.guid(receipt.id)),
                ("documentType", FiscalJSON.string("receipt")),
                ("receiptNumber", FiscalJSON.string(receipt.receiptNumber)),
                ("duplicateNumber", "\(number)"),
                ("printQueued", "false"),
                ("terminalId", FiscalJSON.string(receipt.terminalId)),
            ]),
            terminalId: receipt.terminalId, operatorId: operatorId, now: now
        )
        return number
    }

    // MARK: Vérification

    /// Recalcule les chaînes (reçus par terminal, JET). Renvoie les anomalies (`sequence_gap`, `previous_hash_mismatch`, `signature_mismatch`).
    func verifyChains() throws -> [String] {
        var issues: [String] = []
        let terminals = try db.query("SELECT DISTINCT TerminalId FROM FiscalReceipts ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        for terminal in terminals {
            var previous = FiscalHashing.genesisHash
            var expected = 1
            for row in try db.query("SELECT * FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber", [.text(terminal)]) {
                let sequence = row.int("SequenceNumber") ?? 0
                let label = "receipt \(terminal)#\(sequence)"
                if sequence != expected { issues.append("\(label): sequence_gap (attendu \(expected))"); expected = sequence }
                if row.string("PreviousSignatureHash") != previous { issues.append("\(label): previous_hash_mismatch") }
                let timestamp = row.date("CreatedAtUtc").map(FiscalHashing.timestampText) ?? ""
                let signature = FiscalHashing.receiptHash(
                    previousHash: row.string("PreviousSignatureHash") ?? "", terminalId: terminal, sequence: sequence,
                    amountCents: row.int("TotalTtcAmount") ?? 0, timestamp: timestamp, taxBreakdownJson: row.string("TaxBreakdownJson") ?? ""
                )
                if signature != row.string("SignatureHash") { issues.append("\(label): signature_mismatch") }
                previous = row.string("SignatureHash") ?? ""
                expected += 1
            }
        }
        var previous = FiscalHashing.genesisHash
        var expected = 1
        for row in try db.query("SELECT * FROM JournalEntries WHERE ChainSequence IS NOT NULL ORDER BY ChainSequence") {
            let sequence = row.int("ChainSequence") ?? 0
            let label = "jet#\(sequence)"
            if sequence != expected { issues.append("\(label): sequence_gap (attendu \(expected))"); expected = sequence }
            if row.string("PreviousHash") != previous { issues.append("\(label): previous_hash_mismatch") }
            let hash = FiscalHashing.jetHash(
                previousHash: row.string("PreviousHash") ?? "", chainSequence: sequence, eventType: row.string("EventType") ?? "",
                timestamp: row.date("OccurredAtUtc").map(FiscalHashing.timestampText) ?? "", terminalId: row.string("TerminalId"),
                operatorId: row.uuid("OperatorId"), payloadJson: row.string("PayloadJson") ?? ""
            )
            if hash != row.string("EntryHash") { issues.append("\(label): signature_mismatch") }
            previous = row.string("EntryHash") ?? ""
            expected += 1
        }
        return issues
    }
}
```

- [ ] **Step 5: Lancer les tests**

Run : `cd ios/Packages/PosKit && swift test --filter LocalFiscalRepositoryTests`
Expected : 11 tests verts. Puis `cd ios && ./scripts/test.sh unit | tail -3` : 348 + 11 = 359. Si `verifyChainsDetectsTamperingAndGaps` échoue sur le JET, vérifier que le test retire bien `TR_JournalEntries_NoUpdate` avant l'`UPDATE`.

- [ ] **Step 6: Suivi et commit**

Ajouter la ligne 4 au suivi (PosKit 359).
```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalRepositoryTests.swift docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): dépôt fiscal, reçus chaînés, annulation, duplicata, JET et vérification des chaînes"
```

---

### Task 5: Brancher l'encaissement, la chambre, l'annulation et le duplicata sur la chaîne fiscale

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV5.swift` (ajout de `DROP TABLE NonFiscalPayments;` en tête)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (dépôt `fiscalRepository`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Payments.swift` (`LocalSettlement`, `balance`, `settle`, `pay`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+CounterCheckout.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Hotel.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPaymentRepository.swift` (retrait de `paidCents`, `insertTender`, `nextReceiptNumber`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` (retrait de `reprintReceipt`)
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fiscal.swift` (`voidReceipt`, `reprintReceipt`)
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTestSupport.swift` et les tests existants contenant `NF-T0`, `fiscalSignature == nil` ou `NonFiscalPayments`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalPaymentTests.swift`

**Interfaces:**
- Consumes: `LocalFiscalRepository`, `FiscalReceiptMath`, `FiscalTenderInput`.
- Produces :
  - `LocalPosAPI.fiscalRepository` (interne).
  - `settle(order:tenders:terminalId:)` écrit un reçu fiscal et renvoie `LocalSettlement` avec `fiscalSignature: String` ; `pay` et `counterCheckout` renvoient cette signature.
  - `public func voidReceipt(receiptId: UUID) async throws -> OperationResult` (responsable) ; `public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult` (responsable).
  - `makeLocalAPIWithDatabase(pin:)` (helper de test : API sur fichier + seconde connexion pour inspecter les tables).

- [ ] **Step 1: Mettre à jour les tests existants et le helper**

Run :
```bash
cd ios/Packages/PosKit
grep -rln "NF-T0\|fiscalSignature == nil\|NonFiscalPayments" Tests
```
Dans chaque fichier listé (`LocalCounterCheckoutTests`, `LocalPaymentTests`, `LocalReviewFixTests`, `LocalTerminalTests`, `LocalPaymentStoreTests`…) :
- remplacer `"NF-T01-` par `"T01-` (`"NF-T01-000001"` devient `"T01-000001"`) ;
- remplacer `result.fiscalSignature == nil` par `result.fiscalSignature?.isEmpty == false` ;
- dans `LocalPaymentStoreTests.swift` : retirer `"NonFiscalPayments"` de la liste `expected` de `migrationV3CreatesTheTables`, et **supprimer** le test `paymentRepositoryTracksPaidAmountAndReceiptNumbers` (il teste `insertTender`/`nextReceiptNumber`, retirés à l'étape 4) ; conserver les tests de numéros de retrait et d'avoirs.
Ne pas toucher `LocalSchemaV3.swift` (script publié).

Ajouter à la fin de `Tests/PosKitTests/Local/LocalTestSupport.swift` :
```swift
/// API sur fichier + seconde connexion (WAL) pour inspecter les tables fiscales depuis un test.
func makeLocalAPIWithDatabase(pin: String = "1234") async throws -> (api: LocalPosAPI, db: SQLiteDatabase) {
    let path = temporaryDatabasePath()
    let api = try LocalPosAPI(path: path)
    _ = try await api.login(pin: pin)
    return (api, try SQLiteDatabase(path: path))
}
```

- [ ] **Step 2: Écrire les nouveaux tests (ils échouent)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalPaymentTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : reçus fiscaux, annulation et duplicata")
struct LocalFiscalPaymentTests {
    private func seat(_ api: LocalPosAPI, _ table: String = "T1", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func payingWritesAChainedFiscalReceipt() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950, tendered: 2000)]))
        #expect(result.receiptNumber == "T01-000001" && result.changeGiven == Money(cents: 50))
        let receipt = try #require(try db.query("SELECT * FROM FiscalReceipts").first)
        #expect(receipt.int("TotalTtcAmount") == 1950 && receipt.string("PreviousSignatureHash") == FiscalHashing.genesisHash)
        #expect(receipt.string("SignatureHash") == result.fiscalSignature && receipt.string("TerminalId") == "T01")
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders").first)
        #expect(tender.int("Amount") == 1950 && tender.int("Tendered") == 2000 && tender.int("ChangeGiven") == 50)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'NonFiscalPayments'").isEmpty)
    }

    @Test func splitPaymentsProduceLinkedReceipts() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let first = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        let second = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        #expect(first.receiptNumber == "T01-000001" && second.receiptNumber == "T01-000002" && second.remainingBalance == .zero)
        let rows = try db.query("SELECT * FROM FiscalReceipts ORDER BY SequenceNumber")
        #expect(rows[1].string("PreviousSignatureHash") == first.fiscalSignature)
        #expect(rows.map { $0.int("TotalTtcAmount") ?? 0 }.reduce(0, +) == 1950)
    }

    @Test func aFreeOrderIsClosedWithAZeroReceipt() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        let line = try #require(order.lines.first)
        _ = try await api.compItem(orderId: order.orderId, lineId: line.lineId, reason: "Geste commercial")
        let free = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.pay(localPayment(free, table: "T1", tenders: []))
        #expect(result.receiptNumber == "T01-000001" && result.totalPaid == .zero && result.remainingBalance == .zero)
        #expect(try db.query("SELECT TotalTtcAmount FROM FiscalReceipts").first?.int("TotalTtcAmount") == 0)
        #expect(try await api.tables().first { $0.tableNumber == "T1" }?.status == .free)
    }

    @Test func emptyTendersStayRefusedWhenSomethingIsDue() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        await #expect(throws: APIError.server(status: 400, message: "Échec de l'encaissement.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: []))
        }
        #expect(try db.query("SELECT COUNT(*) AS n FROM FiscalReceipts").first?.int("n") == 0)
    }

    @Test func chargingARoomWritesAFiscalReceiptWithARoomTender() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api, "T3")
        let request = RoomChargeRequest(
            orderId: order.orderId, tableNumber: "T3", roomNumber: "101", guestName: "Jean Dujardin", amount: Money(cents: 1950),
            tipAmount: Money(cents: 100), signatureDataUrl: nil, notes: nil
        )
        _ = try await api.chargeRoom(request)
        let receipt = try #require(try db.query("SELECT * FROM FiscalReceipts").first)
        #expect(receipt.string("ReceiptNumber") == "T01-000001" && receipt.int("TotalTtcAmount") == 1950)
        let tender = try #require(try db.query("SELECT * FROM PaymentTenders").first)
        #expect(tender.int("Method") == PaymentMethod.roomCharge.rawValue && tender.int("Amount") == 2050)
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func voidingAReceiptCancelsTheOrderAndIsJournaled() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts").first?.uuid("Id"))
        let result = try await api.voidReceipt(receiptId: id)
        #expect(result.success && result.message.contains("T01-VOID-000002"))
        #expect(try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(order.orderId)]).first?.int("Status") == LocalOrderStatus.cancelled.rawValue)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = 'RECEIPT_VOIDED'").first?.int("n") == 1)
        await #expect(throws: APIError.self) { try await api.voidReceipt(receiptId: id) }
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func onlyAManagerCanVoidOrReprint() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts").first?.uuid("Id"))
        _ = try await api.login(pin: "2468") // serveur
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.voidReceipt(receiptId: id) }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.reprintReceipt(receiptIdentifier: id.uuidString) }
    }

    @Test func reprintingNumbersTheDuplicatesAndJournalsThemWithoutAPrinter() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let first = try await api.reprintReceipt(receiptIdentifier: "T01-000001")
        let second = try await api.reprintReceipt(receiptIdentifier: "T01-000001")
        #expect(first == ReprintResult(printQueued: false, duplicateNumber: 1) && second.duplicateNumber == 2)
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = 'DUPLICATE_PRINTED'").first?.int("n") == 2)
        await #expect(throws: APIError.self) { try await api.reprintReceipt(receiptIdentifier: "inconnu") }
    }

    @Test func concurrentPaymentsKeepAContiguousChain() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let first = try await seat(api, "T1"), second = try await seat(api, "T2")
        async let a = api.pay(localPayment(first, table: "T1", tenders: [localTender(.cash, 1950)]))
        async let b = api.pay(localPayment(second, table: "T2", tenders: [localTender(.cash, 1950)]))
        _ = try await (a, b)
        #expect(try db.query("SELECT SequenceNumber FROM FiscalReceipts ORDER BY SequenceNumber").compactMap { $0.int("SequenceNumber") } == [1, 2])
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }
}
```
Notes pour l'implémenteur : `compItem(orderId:lineId:reason:)` est le nom supposé de l'API de gratuité de `PosAPI` ; vérifier avec `grep -n "comp" ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift` et adapter l'appel. Si `OperationResult.message` est optionnel, écrire `result.message?.contains(...) == true`.

- [ ] **Step 3: Lancer les tests pour les voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter "LocalFiscalPaymentTests|LocalPaymentTests|LocalCounterCheckoutTests"`
Expected : erreurs de compilation (`voidReceipt` absent) puis, une fois compilé, échecs sur `T01-000001`.

- [ ] **Step 4: Implémenter le branchement**

1. `LocalSchemaV5.swift` : insérer en première ligne du script `DROP TABLE NonFiscalPayments;` (script non publié ; mettre à jour le commentaire : « remplace `NonFiscalPayments` »).
2. `LocalPosAPI.swift` : après `paymentRepository`, ajouter `var fiscalRepository: LocalFiscalRepository { LocalFiscalRepository(db: db) }`.
3. `LocalPaymentRepository.swift` : supprimer `paidCents(orderId:)`, `insertTender(...)` et `nextReceiptNumber(terminalId:)`. Garder `nextPickupNumber`, `pickupPrefix`, `insertCreditVoucher`, `advanceCounter`.
4. `LocalPosAPI+Payments.swift` : remplacer `LocalSettlement`, `balance(of:)` et `settle(...)` :
```swift
struct LocalSettlement {
    let receiptNumber: String
    let fiscalSignature: String
    let totalPaid: Money
    let changeGiven: Money
    let remaining: Money
}
```
```swift
    /// Total TTC de la commande, pourboire déjà enregistré et montant déjà réglé (net des annulations).
    func balance(of order: ActiveOrder) throws -> (total: Money, tip: Money, paid: Money) {
        let total = decorate(order).totalTtcAmount ?? .zero
        let tip = Money(cents: try orderRepository.tipCents(of: order.orderId))
        let paid = Money(cents: try fiscalRepository.paidCents(orderId: order.orderId))
        return (total, tip, paid)
    }
```
```swift
    /// Enregistre des règlements sur une commande et écrit le reçu fiscal (chaîne `T01`). Refuse (sans rien écrire) un montant ≤ 0,
    /// un total supérieur au solde dû ou une commande déjà réglée ou annulée. Une liste de règlements vide n'est acceptée que pour une
    /// commande à total nul (offerte ou remisée à 100 %) : elle est clôturée avec un reçu à zéro. À appeler dans une transaction.
    func settle(order: ActiveOrder, tenders: [(method: PaymentMethod, amount: Money, tendered: Money)], terminalId: String) throws -> LocalSettlement {
        let (total, tip, paid) = try balance(of: order)
        let isFreeOrder = tenders.isEmpty && total + tip == .zero && !order.lines.isEmpty
        guard isFreeOrder || (!tenders.isEmpty && tenders.allSatisfy({ $0.amount > .zero && $0.tendered >= .zero })),
              try orderRepository.status(of: order.orderId)?.isModifiable == true
        else { throw Self.paymentFailed }
        let remainingBefore = (total + tip - paid).clampedAtZero()
        let totalPaid = tenders.reduce(Money.zero) { $0 + $1.amount }
        guard totalPaid <= remainingBefore else { throw Self.paymentFailed }

        let terminal = normalizedTerminal(terminalId)
        let fiscal = fiscalRepository
        let amounts = FiscalReceiptMath.amounts(
            order: order, totalPaidCents: totalPaid.cents, tipCents: tip.cents, priorFiscalTtcCents: try fiscal.priorFiscalTtcCents(orderId: order.orderId)
        )
        // Seules les espèces rendent la monnaie : jamais la valeur faciale d'un titre-restaurant ni une carte sur-saisie.
        var change = Money.zero
        var inputs: [FiscalTenderInput] = []
        for tender in tenders {
            let tenderChange = tender.method == .cash ? (tender.tendered - tender.amount).clampedAtZero() : .zero
            change += tenderChange
            inputs.append(FiscalTenderInput(method: tender.method, amount: tender.amount.cents, tendered: tender.tendered.cents, change: tenderChange.cents))
        }
        let receipt = try fiscal.appendReceipt(orderId: order.orderId, terminalId: terminal, amounts: amounts, tenders: inputs, now: clock())
        let remaining = remainingBefore - totalPaid
        if remaining == .zero { try closeOrder(order, as: .paid) }
        return LocalSettlement(receiptNumber: receipt.receiptNumber, fiscalSignature: receipt.signature, totalPaid: totalPaid, changeGiven: change, remaining: remaining)
    }
```
   Dans `pay`, passer `fiscalSignature: settlement.fiscalSignature` à `PaymentResult(...)` (à la place de `nil`).
5. `LocalPosAPI+CounterCheckout.swift` : dans `CounterCheckoutResult(...)`, remplacer `fiscalSignature: nil` par `fiscalSignature: settlement.fiscalSignature`.
6. `LocalPosAPI+Hotel.swift` : dans `chargeRoom`, remplacer `try closeOrder(order, as: .paid)` par :
```swift
            // Le pourboire de la chambre fait partie de l'encaissement : il est enregistré sur la commande, puis le reçu fiscal est écrit.
            try orders.setTip(orderId: orderId, cents: request.tipAmount.cents)
            _ = try settle(order: order, tenders: [(method: .roomCharge, amount: charge, tendered: charge)], terminalId: Self.standaloneTerminalId)
```
   (`charge` est déjà défini : `request.amount + request.tipAmount`.) Mettre à jour le commentaire de doc : la commande est soldée par un reçu fiscal à règlement « chambre » (le .NET n'écrit pas de reçu pour une chambre : écart assumé, voir la spec).
7. `LocalPosAPI+Unsupported.swift` : supprimer la ligne `reprintReceipt`.
8. Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fiscal.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Annule un reçu par contre-passation (`POST /api/checkout/void/{id}`) : nouveau reçu signé en négatif, entrée JET `RECEIPT_VOIDED`,
    /// commande annulée (comme le .NET, la table n'est pas touchée). Réservé aux responsables ; le reçu doit venir de `T01`.
    public func voidReceipt(receiptId: UUID) async throws -> OperationResult {
        let manager = try requireManager()
        let fiscal = fiscalRepository, orders = orderRepository
        let now = clock()
        return try db.transaction { () throws -> OperationResult in
            guard let original = try fiscal.receipt(id: receiptId),
                  let voided = try fiscal.voidReceipt(originalId: receiptId, terminalId: Self.standaloneTerminalId, operatorId: manager.id, now: now)
            else { throw APIError.server(status: 400, message: "Annulation du reçu impossible.") }
            try orders.setStatus(orderId: original.orderId, .cancelled)
            return OperationResult(success: true, message: "Reçu \(original.receiptNumber) annulé (\(voided.receiptNumber)).")
        }
    }

    /// Duplicata d'un reçu (`POST /api/checkout/receipts/{id}/reprint`). Sans impression (sous-projet 4) : le duplicata est numéroté et
    /// consigné au JET, `printQueued` reste faux. Réservé aux responsables.
    public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult {
        let manager = try requireManager()
        let fiscal = fiscalRepository
        let now = clock()
        return try db.transaction { () throws -> ReprintResult in
            guard let receipt = try fiscal.receipt(identifier: receiptIdentifier) else { throw APIError.notFound("Reçu introuvable.") }
            return ReprintResult(printQueued: false, duplicateNumber: try fiscal.recordDuplicate(of: receipt, operatorId: manager.id, now: now))
        }
    }
}
```

- [ ] **Step 5: Lancer toute la suite PosKit**

Run : `cd ios && ./scripts/test.sh unit 2>&1 | tail -8`
Expected : tout vert. Si des tests existants échouent sur un libellé de reçu (`grep -rn "NF-" ios/Packages/PosKit/Tests`), les corriger comme à l'étape 1. Nombre attendu : 359 + 9 (nouveaux) − 1 (test supprimé) = 367.

- [ ] **Step 6: Suivi et commit**

Ajouter la ligne 5 au suivi (PosKit 367).
```bash
git add -A ios/Packages/PosKit docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): encaissement, chambre, annulation et duplicata écrits dans la chaîne fiscale NF525"
```

---

### Task 6: Audit JET : dérogations Happy Hour et annulations de mise en attente

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalHappyHourRepository.swift` (`insertOverride` renvoie l'id)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+HappyHour.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Counter.swift` (`voidHeldOrder`)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalJournalAuditTests.swift`

**Interfaces:**
- Consumes: `LocalFiscalRepository.appendJournal`, `LocalJournalEvents`, `FiscalJSON`.
- Produces: entrées JET `EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED`, `EVENT_HAPPY_HOUR_OVERRIDE_STOPPED`, `EVENT_HELD_ORDER_VOIDED`, avec les charges utiles du .NET (`HappyHourPricingService`, `CounterSaleEndpoints`).

- [ ] **Step 1: Écrire les tests (ils échouent)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalJournalAuditTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("JET : dérogations Happy Hour et annulations de mise en attente")
struct LocalJournalAuditTests {
    private func entries(_ db: SQLiteDatabase, _ type: String) throws -> [SQLRow] {
        try db.query("SELECT * FROM JournalEntries WHERE EventType = ? ORDER BY ChainSequence", [.text(type)])
    }

    @Test func happyHourOverrideIsJournaledWithTheBackendPayload() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "Dérogation +10 % d'été")
        _ = try await api.stopHappyHourOverride(terminalId: "T01", pin: "1234", reason: "Fin de service")
        let started = try #require(try entries(db, "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED").first)
        let payload = try #require(started.string("PayloadJson"))
        #expect(payload.hasPrefix("{\"SessionId\":\"") && payload.contains("\"DurationMinutes\":30") && payload.contains("\"Reason\":\"D\\u00E9rogation \\u002B10 % d\\u0027\\u00E9t\\u00E9\""))
        #expect(payload.contains("\"Operator\":") && payload.contains("\"ExpiresAtUtc\":\""))
        #expect(started.string("TerminalId") == "T01" && started.uuid("OperatorId") != nil)
        let stopped = try #require(try entries(db, "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED").first)
        #expect(stopped.string("PayloadJson")?.contains("\"Reason\":\"Fin de service\"") == true)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func aRefusedOverrideWritesNothing() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        await #expect(throws: APIError.self) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "0000", minutes: 30, reason: "x") }
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n") == 0)
    }

    @Test func voidingAHeldOrderIsJournaled() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let held = try await api.holdOrder(orderId: order.orderId, tableNumber: "T1", customerLabel: "Client A", terminalId: "T01", operatorId: nil)
        try await api.voidHeldOrder(holdId: held.holdId, supervisorPin: "1234", reason: "Client parti", terminalId: "T01")
        let entry = try #require(try entries(db, "EVENT_HELD_ORDER_VOIDED").first)
        let payload = try #require(entry.string("PayloadJson"))
        #expect(payload.contains("\"HoldId\":\"\(FiscalHashing.guidText(held.holdId))\"") && payload.contains("\"Reason\":\"Client parti\"") && payload.contains("\"VoidedBy\":"))
        #expect(entry.string("TerminalId") == "T01")
    }
}
```
Notes pour l'implémenteur : `holdOrder(...)` est le nom supposé de l'API de mise en attente (`grep -n "func hold" ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift`) ; adapter l'appel et le type de retour (`HeldOrder.holdId`) à la signature réelle. Le PIN `1234` est celui du responsable de démonstration.

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter LocalJournalAuditTests`
Expected : échecs (aucune entrée JET).

- [ ] **Step 3: Implémenter**

1. `LocalHappyHourRepository.insertOverride` : ajouter `@discardableResult`, retour `-> UUID`, générer `let id = UUID()` avant l'`INSERT`, l'utiliser comme premier paramètre (`.uuid(id)` à la place de `.uuid(UUID())`) et terminer par `return id`.
2. `LocalPosAPI+HappyHour.swift` : dans `activateHappyHourOverride`, remplacer le bloc `try db.transaction { ... }` par :
```swift
        let expiresAt = now.addingTimeInterval(TimeInterval(duration * 60))
        let text = reason.isEmpty ? "Dérogation manuelle responsable" : reason
        let fiscal = fiscalRepository
        try db.transaction {
            try repository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
            let sessionId = try repository.insertOverride(
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, operatorName: supervisor.name, startsAt: now, expiresAt: expiresAt, reason: text
            )
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.happyHourActivated,
                payloadJson: FiscalJSON.object([
                    ("SessionId", FiscalJSON.guid(sessionId)), ("DurationMinutes", "\(duration)"), ("Operator", FiscalJSON.string(supervisor.name)),
                    ("Reason", FiscalJSON.string(text)), ("ExpiresAtUtc", FiscalJSON.dateTime(expiresAt)),
                ]),
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, now: now
            )
        }
```
   (retirer la mention « L'audit JET viendra avec le sous-projet fiscal » du commentaire de la fonction). Remplacer le corps de `stopHappyHourOverride` par :
```swift
        let supervisor = try authenticateSupervisor(pin: pin)
        let fiscal = fiscalRepository, repository = happyHourRepository
        let now = clock()
        try db.transaction {
            try repository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.happyHourStopped,
                payloadJson: FiscalJSON.object([("Operator", FiscalJSON.string(supervisor.name)), ("Reason", FiscalJSON.string(reason))]),
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, now: now
            )
        }
        return OperationResult(success: true, message: "Dérogation Happy Hour désactivée.")
```
3. `LocalPosAPI+Counter.swift`, `voidHeldOrder` : déclarer `let fiscal = fiscalRepository` et `let now = clock()` à côté de `let orders = orderRepository, holds = holdRepository`, et ajouter à la fin de la fermeture `db.transaction` (après `try orders.setStatus(orderId: orderId, .cancelled)`) :
```swift
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.heldOrderVoided,
                payloadJson: FiscalJSON.object([("HoldId", FiscalJSON.guid(holdId)), ("VoidedBy", FiscalJSON.string(supervisor.name)), ("Reason", FiscalJSON.string(reason))]),
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, now: now
            )
```

- [ ] **Step 4: Lancer toute la suite PosKit**

Run : `cd ios && ./scripts/test.sh unit 2>&1 | tail -5`
Expected : tout vert (367 + 3 = 370).

- [ ] **Step 5: Suivi et commit**

Ajouter la ligne 6 au suivi (PosKit 370).
```bash
git add -A ios/Packages/PosKit docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): audit JET des dérogations Happy Hour et des annulations de mise en attente"
```

---

### Task 7: Sauvegarde chiffrée : format, écriture, rotation, restauration

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupCrypto.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupService.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalBackupTests.swift`

**Interfaces:**
- Consumes: `SQLiteDatabase.backup(to:)`, `LocalFiscalRepository.verifyChains`, CryptoKit, CommonCrypto.
- Produces :
  - `enum LocalBackupFailure: Error, Equatable, LocalizedError { case notABackup, wrongPassphraseOrCorrupted, brokenChain([String]) }`
  - `enum LocalBackupCrypto` : `static let defaultIterations = 200_000`, `static func seal(_ plain: Data, passphrase: String, iterations: Int) throws -> Data`, `static func open(_ file: Data, passphrase: String) throws -> Data`.
  - `enum LocalBackupService` : `static let keepCount = 14`, `static func fileName(now: Date) -> String`, `static func write(db: SQLiteDatabase, to folder: URL, passphrase: String, now: Date, iterations: Int) throws -> URL`, `static func restore(from file: URL, passphrase: String, to destination: URL) throws`.

- [ ] **Step 1: Écrire les tests (ils échouent)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalBackupTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Sauvegarde chiffrée")
struct LocalBackupTests {
    private static let fast = 1_000 // itérations PBKDF2 réduites pour les tests
    private let at = Date(timeIntervalSince1970: 1_760_087_880)

    private func makeFolder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("backup-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try db.run("INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES ('marker', '', 42)")
        return db
    }

    @Test func sealAndOpenRoundTrip() throws {
        let plain = Data("base de données".utf8)
        let sealed = try LocalBackupCrypto.seal(plain, passphrase: "phrase secrète 1", iterations: Self.fast)
        #expect(sealed != plain && sealed.range(of: plain) == nil)
        #expect(try LocalBackupCrypto.open(sealed, passphrase: "phrase secrète 1") == plain)
    }

    @Test func aWrongPassphraseIsRejected() throws {
        let sealed = try LocalBackupCrypto.seal(Data("x".utf8), passphrase: "bonne phrase", iterations: Self.fast)
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(sealed, passphrase: "mauvaise phrase") }
    }

    @Test func aModifiedOrTruncatedFileIsRejected() throws {
        let sealed = try LocalBackupCrypto.seal(Data("contenu".utf8), passphrase: "phrase", iterations: Self.fast)
        var flipped = sealed
        flipped[flipped.count - 1] ^= 0x01
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(flipped, passphrase: "phrase") }
        var header = sealed
        header[10] ^= 0x01 // l'en-tête est authentifié
        #expect(throws: LocalBackupFailure.self) { try LocalBackupCrypto.open(header, passphrase: "phrase") }
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(sealed.prefix(sealed.count - 5), passphrase: "phrase") }
        #expect(throws: LocalBackupFailure.notABackup) { try LocalBackupCrypto.open(sealed.prefix(10), passphrase: "phrase") }
    }

    @Test func aFileThatIsNotABackupIsRejected() throws {
        #expect(throws: LocalBackupFailure.notABackup) { try LocalBackupCrypto.open(Data("SQLite format 3".utf8) + Data(count: 100), passphrase: "x") }
    }

    @Test func writeThenRestoreGivesBackTheSameDatabase() throws {
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: try makeDB(), to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        #expect(file.lastPathComponent == LocalBackupService.fileName(now: at) && file.pathExtension == "posbak")
        #expect(try Data(contentsOf: file).range(of: Data("SQLite format 3".utf8)) == nil)

        let destination = folder.appendingPathComponent("restored.sqlite")
        try LocalBackupService.restore(from: file, passphrase: "phrase de test", to: destination)
        let restored = try SQLiteDatabase(path: destination.path)
        #expect(try restored.query("SELECT LastSequence FROM LocalCounters WHERE Name = 'marker'").first?.int("LastSequence") == 42)
        #expect(try restored.userVersion() == LocalMigrator.steps.count)
    }

    @Test func restoringWithAWrongPassphraseLeavesNoFile() throws {
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: try makeDB(), to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        let destination = folder.appendingPathComponent("restored.sqlite")
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupService.restore(from: file, passphrase: "autre", to: destination) }
        #expect(!FileManager.default.fileExists(atPath: destination.path))
    }

    @Test func restoringABrokenChainIsRefusedAndLeavesNoFile() throws {
        let db = try makeDB()
        let repo = LocalFiscalRepository(db: db)
        try repo.appendJournal(eventType: "X", payloadJson: "{}", terminalId: "T01", operatorId: nil, now: at)
        try db.exec("DROP TRIGGER TR_JournalEntries_NoUpdate")
        try db.run("UPDATE JournalEntries SET PayloadJson = '{\"altéré\":1}'")
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        let destination = folder.appendingPathComponent("restored.sqlite")
        do {
            try LocalBackupService.restore(from: file, passphrase: "phrase de test", to: destination)
            Issue.record("la restauration d'une chaîne rompue aurait dû échouer")
        } catch let LocalBackupFailure.brokenChain(issues) {
            #expect(issues.contains { $0.contains("signature_mismatch") })
        }
        #expect(!FileManager.default.fileExists(atPath: destination.path))
    }

    @Test func rotationKeepsTheFourteenNewestBackups() throws {
        let folder = try makeFolder()
        let db = try makeDB()
        for day in 0..<16 {
            _ = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase", now: at.addingTimeInterval(Double(day) * 86_400), iterations: Self.fast)
        }
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasSuffix(".posbak") }.sorted()
        #expect(names.count == LocalBackupService.keepCount)
        #expect(names.last == LocalBackupService.fileName(now: at.addingTimeInterval(15 * 86_400)))
        #expect(!names.contains(LocalBackupService.fileName(now: at)))
    }

    @Test func rotationNeverTouchesOtherFiles() throws {
        let folder = try makeFolder()
        try Data("photo".utf8).write(to: folder.appendingPathComponent("vacances.jpg"))
        let db = try makeDB()
        for day in 0..<16 {
            _ = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase", now: at.addingTimeInterval(Double(day) * 86_400), iterations: Self.fast)
        }
        #expect(FileManager.default.fileExists(atPath: folder.appendingPathComponent("vacances.jpg").path))
    }

    @Test func anUnavailableFolderFailsWithoutLeavingAnyFile() throws {
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("absent-\(UUID().uuidString)", isDirectory: true)
        let db = try makeDB()
        #expect(throws: (any Error).self) { try LocalBackupService.write(db: db, to: missing, passphrase: "phrase", now: at, iterations: Self.fast) }
        #expect(!FileManager.default.fileExists(atPath: missing.path))
    }
}
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter LocalBackupTests`
Expected : erreur de compilation « cannot find 'LocalBackupCrypto' in scope ».

- [ ] **Step 3: Implémenter le chiffrement**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupCrypto.swift` :
```swift
import CommonCrypto
import CryptoKit
import Foundation

enum LocalBackupFailure: Error, Equatable, LocalizedError {
    /// Le fichier n'est pas une sauvegarde de l'application (en-tête absent ou illisible).
    case notABackup
    /// Phrase fausse ou fichier altéré : AES-GCM ne permet pas de les distinguer.
    case wrongPassphraseOrCorrupted
    /// La base restaurée a une chaîne fiscale rompue.
    case brokenChain([String])

    var errorDescription: String? {
        switch self {
        case .notABackup: "Ce fichier n'est pas une sauvegarde de l'application."
        case .wrongPassphraseOrCorrupted: "Phrase de sauvegarde incorrecte ou fichier altéré."
        case .brokenChain(let issues): "Chaîne fiscale rompue dans la sauvegarde : \(issues.prefix(3).joined(separator: " ; "))"
        }
    }
}

/// Fichier de sauvegarde : `magic(8) | itérations PBKDF2 (UInt32 BE) | sel(16) | AES-GCM combiné (nonce 12 | texte chiffré | tag 16)`.
/// L'en-tête est authentifié (AAD) ; la clé dérive de la phrase par PBKDF2-HMAC-SHA256.
enum LocalBackupCrypto {
    static let defaultIterations = 200_000
    private static let magic = Data("POSBAK1\n".utf8)
    private static let saltLength = 16
    private static let headerLength = 8 + 4 + 16
    private static let maxIterations = 10_000_000

    static func seal(_ plain: Data, passphrase: String, iterations: Int) throws -> Data {
        var salt = Data(count: saltLength)
        let status = salt.withUnsafeMutableBytes { SecRandomCopyBytes(kSecRandomDefault, saltLength, $0.baseAddress!) }
        guard status == errSecSuccess else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        var header = magic
        withUnsafeBytes(of: UInt32(iterations).bigEndian) { header.append(contentsOf: $0) }
        header.append(salt)
        let key = try deriveKey(passphrase: passphrase, salt: salt, iterations: iterations)
        let box = try AES.GCM.seal(plain, using: key, authenticating: header)
        guard let combined = box.combined else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        return header + combined
    }

    static func open(_ file: Data, passphrase: String) throws -> Data {
        let bytes = Data(file) // indices à partir de 0
        guard bytes.count >= headerLength, bytes.prefix(magic.count) == magic else { throw LocalBackupFailure.notABackup }
        let iterations = Int(bytes[8..<12].reduce(UInt32(0)) { ($0 << 8) | UInt32($1) })
        guard iterations > 0, iterations <= maxIterations else { throw LocalBackupFailure.notABackup }
        let header = Data(bytes.prefix(headerLength))
        let salt = Data(bytes[12..<headerLength])
        do {
            let key = try deriveKey(passphrase: passphrase, salt: salt, iterations: iterations)
            let box = try AES.GCM.SealedBox(combined: Data(bytes.suffix(from: headerLength)))
            return try AES.GCM.open(box, using: key, authenticating: header)
        } catch {
            throw LocalBackupFailure.wrongPassphraseOrCorrupted
        }
    }

    private static func deriveKey(passphrase: String, salt: Data, iterations: Int) throws -> SymmetricKey {
        var derived = [UInt8](repeating: 0, count: 32)
        let status = salt.withUnsafeBytes { saltBytes in
            CCKeyDerivationPBKDF(
                CCPBKDFAlgorithm(kCCPBKDF2), passphrase, passphrase.utf8.count,
                saltBytes.bindMemory(to: UInt8.self).baseAddress, salt.count,
                CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256), UInt32(iterations), &derived, derived.count
            )
        }
        guard status == kCCSuccess else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        return SymmetricKey(data: derived)
    }
}
```

- [ ] **Step 4: Implémenter le service**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupService.swift` :
```swift
import Foundation

/// Écriture, rotation et restauration des sauvegardes chiffrées de la base locale.
enum LocalBackupService {
    static let keepCount = 14
    private static let prefix = "pos-backup-"
    private static let fileExtension = "posbak"

    /// `pos-backup-20261010T091800Z.posbak` (UTC, triable par nom).
    static func fileName(now: Date) -> String {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let c = utc.dateComponents([.year, .month, .day, .hour, .minute, .second], from: now)
        return String(format: "\(prefix)%04d%02d%02dT%02d%02d%02dZ.\(fileExtension)", c.year ?? 0, c.month ?? 0, c.day ?? 0, c.hour ?? 0, c.minute ?? 0, c.second ?? 0)
    }

    /// Copie cohérente de la base, chiffrée, écrite de façon atomique dans `folder` (le dossier doit exister) ; garde les `keepCount` plus récentes.
    /// Un échec (dossier absent, disque plein) ne laisse aucun fichier ressemblant à une sauvegarde valide.
    static func write(db: SQLiteDatabase, to folder: URL, passphrase: String, now: Date, iterations: Int = LocalBackupCrypto.defaultIterations) throws -> URL {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw CocoaError(.fileNoSuchFile)
        }
        let temporary = FileManager.default.temporaryDirectory.appendingPathComponent("pos-\(UUID().uuidString).sqlite")
        defer { try? FileManager.default.removeItem(at: temporary) }
        try db.backup(to: temporary.path)
        let sealed = try LocalBackupCrypto.seal(try Data(contentsOf: temporary), passphrase: passphrase, iterations: iterations)
        let destination = folder.appendingPathComponent(fileName(now: now))
        try sealed.write(to: destination, options: writeOptions)
        try prune(folder: folder)
        return destination
    }

    /// Déchiffre vers `destination` (fichier SQLite qui ne doit pas exister) puis vérifie les chaînes fiscales. Si la phrase est fausse ou la
    /// chaîne rompue, `destination` n'existe pas à la sortie.
    static func restore(from file: URL, passphrase: String, to destination: URL) throws {
        let plain = try LocalBackupCrypto.open(try Data(contentsOf: file), passphrase: passphrase)
        do {
            try plain.write(to: destination, options: [.withoutOverwriting])
            let issues = try LocalFiscalRepository(db: SQLiteDatabase(path: destination.path)).verifyChains()
            guard issues.isEmpty else { throw LocalBackupFailure.brokenChain(issues) }
        } catch {
            for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: destination.path + suffix) }
            throw error
        }
    }

    private static func prune(folder: URL) throws {
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path)
            .filter { $0.hasPrefix(prefix) && $0.hasSuffix("." + fileExtension) }
            .sorted()
        for name in names.dropLast(keepCount) { try? FileManager.default.removeItem(at: folder.appendingPathComponent(name)) }
    }

    private static var writeOptions: Data.WritingOptions {
        #if os(iOS)
        [.atomic, .completeFileProtection]
        #else
        [.atomic]
        #endif
    }
}
```
Note : dans `restore`, la base ouverte pour la vérification est libérée à la sortie du `do`, ce qui permet de supprimer ensuite `-wal`/`-shm`.

- [ ] **Step 5: Lancer les tests**

Run : `cd ios/Packages/PosKit && swift test --filter LocalBackupTests`
Expected : 10 tests verts. Puis `cd ios && ./scripts/test.sh unit | tail -3` : 370 + 10 = 380. Si `restoringABrokenChainIsRefusedAndLeavesNoFile` échoue, vérifier que l'`UPDATE` du test a lieu après `DROP TRIGGER` et avant `write`.

- [ ] **Step 6: Suivi et commit**

Ajouter la ligne 7 au suivi (PosKit 380).
```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupCrypto.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalBackupService.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalBackupTests.swift docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): sauvegarde chiffrée AES-GCM, rotation et restauration vérifiée"
```

---

### Task 8: Sauvegarde dans `LocalPosAPI` : export, sauvegarde programmée, état, phrase dans le Keychain

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Backup.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (propriété `backupIterations`)
- Create: `ios/Packages/PosKit/Sources/PosKit/Stores/BackupPassphraseStore.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalBackupAPITests.swift`

**Interfaces:**
- Consumes: `LocalBackupService`, `LocalFiscalRepository.appendJournal/lastJournalDate`, `LocalJournalEvents`.
- Produces :
  - `public struct BackupStatus: Equatable, Sendable { public let lastSuccess: Date?; public let isOverdue: Bool }`
  - `LocalPosAPI.backupStatus() async throws -> BackupStatus` (sans authentification)
  - `LocalPosAPI.exportBackup(to folder: URL, passphrase: String) async throws -> URL` (responsable)
  - `LocalPosAPI.runScheduledBackup(to folder: URL, passphrase: String) async throws -> URL` (sans authentification : appelée par l'application au premier plan)
  - `LocalPosAPI.minimumBackupPassphraseLength` (= 8)
  - `@MainActor public protocol BackupPassphraseStore` (`load() -> String?`, `save(_:) -> Bool`, `clear()`), `InMemoryBackupPassphraseStore`, `KeychainBackupPassphraseStore`.
  - `LocalPosAPI.setBackupIterations(_:)` (interne, tests : abaisse les 200 000 itérations).

- [ ] **Step 1: Écrire les tests (ils échouent)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalBackupAPITests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : sauvegarde chiffrée, état et journal")
struct LocalBackupAPITests {
    private func makeFolder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("api-backup-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeAPI(pin: String = "1234", clock: @escaping @Sendable () -> Date = { Date() }) async throws -> (LocalPosAPI, SQLiteDatabase) {
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: clock)
        await api.setBackupIterations(1_000)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path))
    }

    @Test func aNewInstallationIsOverdue() async throws {
        let (api, _) = try await makeAPI()
        let status = try await api.backupStatus()
        #expect(status.lastSuccess == nil && status.isOverdue)
    }

    @Test func aSuccessfulBackupIsJournaledAndClearsTheOverdueFlag() async throws {
        let (api, db) = try await makeAPI()
        let folder = try makeFolder()
        let file = try await api.exportBackup(to: folder, passphrase: "phrase de test")
        #expect(FileManager.default.fileExists(atPath: file.path))
        let status = try await api.backupStatus()
        #expect(status.lastSuccess != nil && !status.isOverdue)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries WHERE EventType = 'BACKUP_COMPLETED'").first)
        #expect(entry.string("PayloadJson")?.contains(file.lastPathComponent) == true)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func aBackupBecomesOverdueAfterTwentyFourHours() async throws {
        let now = LockedDate(Date(timeIntervalSince1970: 1_760_087_880))
        let (api, _) = try await makeAPI(clock: { now.value })
        _ = try await api.exportBackup(to: try makeFolder(), passphrase: "phrase de test")
        now.value = now.value.addingTimeInterval(23 * 3600)
        #expect(try await api.backupStatus().isOverdue == false)
        now.value = now.value.addingTimeInterval(2 * 3600)
        #expect(try await api.backupStatus().isOverdue)
    }

    @Test func aFailedBackupIsJournaledAndLeavesTheStatusUntouched() async throws {
        let (api, db) = try await makeAPI()
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("absent-\(UUID().uuidString)", isDirectory: true)
        await #expect(throws: (any Error).self) { try await api.exportBackup(to: missing, passphrase: "phrase de test") }
        #expect(try await api.backupStatus().lastSuccess == nil)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries WHERE EventType = 'BACKUP_FAILED'").first)
        #expect(entry.string("PayloadJson")?.contains("phrase de test") == false)
    }

    @Test func exportRequiresAManagerButTheScheduledRunDoesNot() async throws {
        let (api, _) = try await makeAPI(pin: "2468") // serveur
        let folder = try makeFolder()
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.exportBackup(to: folder, passphrase: "phrase de test") }
        _ = try await api.runScheduledBackup(to: folder, passphrase: "phrase de test")
        #expect(try await api.backupStatus().lastSuccess != nil)
    }

    @Test func aTooShortPassphraseIsRefused() async throws {
        let (api, _) = try await makeAPI()
        let folder = try makeFolder()
        await #expect(throws: APIError.self) { try await api.exportBackup(to: folder, passphrase: "court") }
    }

    @Test func theInMemoryPassphraseStoreRoundTrips() async {
        await MainActor.run {
            let store = InMemoryBackupPassphraseStore()
            #expect(store.load() == nil)
            #expect(store.save("phrase de test") && store.load() == "phrase de test")
            store.clear()
            #expect(store.load() == nil)
        }
    }
}

/// Horloge mutable partagée entre le test et l'acteur.
final class LockedDate: @unchecked Sendable {
    private let lock = NSLock()
    private var current: Date
    init(_ date: Date) { current = date }
    var value: Date {
        get { lock.withLock { current } }
        set { lock.withLock { current = newValue } }
    }
}
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run : `cd ios/Packages/PosKit && swift test --filter LocalBackupAPITests`
Expected : erreur de compilation (`exportBackup`, `backupStatus`… absents).

- [ ] **Step 3: Implémenter**

Réécrire `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Backup.swift` :
```swift
import Foundation

/// État de la sauvegarde : dernière réussite (d'après le JET) et retard (jamais, ou plus de 24 h).
public struct BackupStatus: Equatable, Sendable {
    public let lastSuccess: Date?
    public let isOverdue: Bool

    public init(lastSuccess: Date?, isOverdue: Bool) {
        self.lastSuccess = lastSuccess
        self.isOverdue = isOverdue
    }
}

extension LocalPosAPI {
    public static let minimumBackupPassphraseLength = 8
    static let backupInterval: TimeInterval = 24 * 3600

    /// Copie **non chiffrée** de la base, réservée aux tests et au dépannage : ne jamais la proposer à l'utilisateur (elle contient les empreintes
    /// de PIN et la chaîne fiscale). L'export utilisateur passe par `exportBackup`.
    public func backup(to url: URL) async throws {
        try requireManager()
        try db.backup(to: url.path)
    }

    public func backupStatus() async throws -> BackupStatus {
        let last = try fiscalRepository.lastJournalDate(eventType: LocalJournalEvents.backupCompleted)
        let overdue = last.map { clock().timeIntervalSince($0) > Self.backupInterval } ?? true
        return BackupStatus(lastSuccess: last, isOverdue: overdue)
    }

    /// Export manuel : réservé aux responsables, phrase redemandée à chaque fois.
    @discardableResult
    public func exportBackup(to folder: URL, passphrase: String) async throws -> URL {
        let manager = try requireManager()
        return try performBackup(to: folder, passphrase: passphrase, operatorId: manager.id)
    }

    /// Sauvegarde automatique (application au premier plan, dernière sauvegarde de plus de 24 h) : pas de session requise.
    @discardableResult
    public func runScheduledBackup(to folder: URL, passphrase: String) async throws -> URL {
        try performBackup(to: folder, passphrase: passphrase, operatorId: nil)
    }

    func setBackupIterations(_ iterations: Int) { backupIterations = iterations }

    /// Écrit la sauvegarde, puis la journalise (réussite ou échec, dans sa propre transaction). La phrase n'apparaît jamais dans le journal.
    private func performBackup(to folder: URL, passphrase: String, operatorId: UUID?) throws -> URL {
        guard passphrase.count >= Self.minimumBackupPassphraseLength else {
            throw APIError.server(status: 400, message: "La phrase de sauvegarde doit comporter au moins \(Self.minimumBackupPassphraseLength) caractères.")
        }
        let now = clock()
        let fiscal = fiscalRepository
        do {
            let file = try LocalBackupService.write(db: db, to: folder, passphrase: passphrase, now: now, iterations: backupIterations)
            let size = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int) ?? 0
            try db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.backupCompleted,
                    payloadJson: FiscalJSON.object([("fileName", FiscalJSON.string(file.lastPathComponent)), ("sizeBytes", "\(size)")]),
                    terminalId: Self.standaloneTerminalId, operatorId: operatorId, now: now
                )
            }
            return file
        } catch {
            try? db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.backupFailed,
                    payloadJson: FiscalJSON.object([("reason", FiscalJSON.string((error as? LocalizedError)?.errorDescription ?? "Écriture impossible"))]),
                    terminalId: Self.standaloneTerminalId, operatorId: operatorId, now: now
                )
            }
            throw error
        }
    }
}
```
Dans `LocalPosAPI.swift`, ajouter parmi les propriétés stockées de l'acteur (après `let calendar: Calendar`) : `var backupIterations = LocalBackupCrypto.defaultIterations`.

Créer `ios/Packages/PosKit/Sources/PosKit/Stores/BackupPassphraseStore.swift` :
```swift
import Foundation
import Security

/// Phrase de sauvegarde mémorisée pour la sauvegarde automatique.
@MainActor
public protocol BackupPassphraseStore: AnyObject {
    func load() -> String?
    @discardableResult func save(_ passphrase: String) -> Bool
    func clear()
}

/// Tests unitaires, tests UI et aperçus.
@MainActor
public final class InMemoryBackupPassphraseStore: BackupPassphraseStore {
    private var value: String?
    public init(_ value: String? = nil) { self.value = value }
    public func load() -> String? { value }
    @discardableResult public func save(_ passphrase: String) -> Bool { value = passphrase; return true }
    public func clear() { value = nil }
}

/// Trousseau iOS, propre à cet appareil : la phrase ne part ni dans une sauvegarde de l'appareil ni vers iCloud.
@MainActor
public final class KeychainBackupPassphraseStore: BackupPassphraseStore {
    private let service = "com.restaurantpos.backup"
    private let account = "passphrase"

    public init() {}

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    public func load() -> String? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    @discardableResult
    public func save(_ passphrase: String) -> Bool {
        SecItemDelete(baseQuery as CFDictionary)
        var query = baseQuery
        query[kSecValueData as String] = Data(passphrase.utf8)
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    public func clear() { SecItemDelete(baseQuery as CFDictionary) }
}
```

- [ ] **Step 4: Lancer les tests**

Run : `cd ios && ./scripts/test.sh unit 2>&1 | tail -5`
Expected : tout vert (380 + 7 = 387). `LocalHardeningTests` (sauvegarde non chiffrée `backup(to:)`) reste vert.

- [ ] **Step 5: Suivi et commit**

Ajouter la ligne 8 au suivi (PosKit 387).
```bash
git add -A ios/Packages/PosKit docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios-local): export et sauvegarde programmée chiffrés, état de sauvegarde et phrase dans le Keychain"
```

---

### Task 9: Interface : retrait du bandeau « non fiscal », réglages et bandeau de sauvegarde

**Files:**
- Modify: `ios/RestaurantPOS/App/StandaloneScreens.swift` (`StandaloneBanner` devient `BackupBanner`)
- Modify: `ios/RestaurantPOS/App/MainShell.swift`
- Modify: `ios/RestaurantPOS/App/RestaurantPOSApp.swift` (`LaunchConfiguration.backupFolder`, `AppEnvironment.backup`)
- Modify: `ios/RestaurantPOS/App/RootView.swift` (déclenchement au premier plan)
- Create: `ios/RestaurantPOS/App/BackupController.swift`
- Create: `ios/RestaurantPOS/Features/Admin/BackupSettingsSection.swift`
- Modify: `ios/RestaurantPOS/Features/Admin/AdminScreen.swift` (`NetworkSettingsView`)
- Modify: `ios/RestaurantPOS/Resources/Localizable.xcstrings`
- Modify: `ios/RestaurantPOSUITests/PosUITestCase.swift` (`launchStandalone(backupFolder:)`)
- Modify: `ios/RestaurantPOSUITests/StandaloneUITests.swift`

**Interfaces:**
- Consumes: `LocalPosAPI.backupStatus/exportBackup/runScheduledBackup`, `BackupStatus`, `BackupPassphraseStore`, `InMemoryBackupPassphraseStore`, `KeychainBackupPassphraseStore`.
- Produces :
  - `@MainActor @Observable final class BackupController` : `status`, `lastError`, `isRunning`, `hasPassphrase`, `folderName`, `needsAttention`, `neverBackedUp`, `refresh()`, `setPassphrase(_:) -> Bool`, `setFolder(_:)`, `runIfDue()`, `exportNow(passphrase:) -> Bool`.
  - `AppEnvironment.backup: BackupController?` (non nil en mode autonome).
  - Argument de lancement de test `-UITestBackupFolder <chemin>` (dossier imposé, sans sélecteur de fichiers).
  - Identifiants d'accessibilité : `backup.banner`, `backup.passphrase.set`, `backup.passphrase.field`, `backup.passphrase.confirm`, `backup.passphrase.save`, `backup.folder`, `backup.folder.choose`, `backup.export`, `backup.export.passphrase`, `backup.export.confirm`, `backup.lastSuccess`.

- [ ] **Step 1: Ajouter les clés de localisation**

Run (depuis `ios/`) :
```bash
python3 - <<'EOF'
import json
path = "RestaurantPOS/Resources/Localizable.xcstrings"
catalog = json.load(open(path, encoding="utf-8"))
new = {
    "backup.section": ("Backup", "Sauvegarde", "النسخ الاحتياطي"),
    "backup.banner_never": ("No backup yet. Set one up in Management > Network.", "Aucune sauvegarde. Configurez-la dans Gestion > Réseau.", "لا توجد نسخة احتياطية. قم بإعدادها من الإدارة > الشبكة."),
    "backup.banner_overdue": ("The last backup is more than 24 hours old.", "La dernière sauvegarde date de plus de 24 h.", "آخر نسخة احتياطية مضى عليها أكثر من 24 ساعة."),
    "backup.last_success_label": ("Last backup", "Dernière sauvegarde", "آخر نسخة احتياطية"),
    "backup.never": ("Never", "Jamais", "أبدًا"),
    "backup.passphrase_label": ("Backup passphrase", "Phrase de sauvegarde", "عبارة النسخ الاحتياطي"),
    "backup.passphrase_set": ("Set", "Définie", "محددة"),
    "backup.passphrase_missing": ("Not set", "Non définie", "غير محددة"),
    "backup.passphrase_change": ("Set the passphrase", "Définir la phrase", "تحديد العبارة"),
    "backup.passphrase_title": ("Backup passphrase", "Phrase de sauvegarde", "عبارة النسخ الاحتياطي"),
    "backup.passphrase_warning": ("If you lose this passphrase, the backups cannot be read. Write it down and keep it safe.", "Si vous perdez cette phrase, les sauvegardes sont illisibles. Notez-la et conservez-la en lieu sûr.", "إذا فقدت هذه العبارة فلن يمكن قراءة النسخ الاحتياطية. دوّنها واحتفظ بها في مكان آمن."),
    "backup.passphrase_field": ("Passphrase (8 characters minimum)", "Phrase (8 caractères minimum)", "العبارة (8 أحرف على الأقل)"),
    "backup.passphrase_confirm": ("Confirm the passphrase", "Confirmez la phrase", "أكّد العبارة"),
    "backup.passphrase_mismatch": ("The two passphrases differ.", "Les deux phrases sont différentes.", "العبارتان مختلفتان."),
    "backup.passphrase_too_short": ("The passphrase must have at least 8 characters.", "La phrase doit comporter au moins 8 caractères.", "يجب أن تتكون العبارة من 8 أحرف على الأقل."),
    "backup.save": ("Save", "Enregistrer", "حفظ"),
    "backup.cancel": ("Cancel", "Annuler", "إلغاء"),
    "backup.folder_label": ("Backup folder", "Dossier de sauvegarde", "مجلد النسخ الاحتياطي"),
    "backup.folder_none": ("Not chosen", "Non choisi", "لم يتم اختياره"),
    "backup.folder_choose": ("Choose a folder", "Choisir un dossier", "اختيار مجلد"),
    "backup.export_now": ("Back up now", "Sauvegarder maintenant", "نسخ احتياطي الآن"),
    "backup.export_title": ("Back up now", "Sauvegarder maintenant", "نسخ احتياطي الآن"),
    "backup.export_passphrase": ("Enter the passphrase", "Saisissez la phrase", "أدخل العبارة"),
    "backup.export_confirm": ("Back up", "Sauvegarder", "نسخ احتياطي"),
    "backup.error_no_folder": ("Choose a backup folder first.", "Choisissez d'abord un dossier de sauvegarde.", "اختر مجلد النسخ الاحتياطي أولًا."),
}
for key, (en, fr, ar) in new.items():
    assert key not in catalog["strings"], key
    catalog["strings"][key] = {"extractionState": "manual", "localizations": {lang: {"stringUnit": {"state": "translated", "value": value}} for lang, value in (("ar", ar), ("en", en), ("fr", fr))}}
assert "mode.non_fiscal_banner" in catalog["strings"]
del catalog["strings"]["mode.non_fiscal_banner"]
open(path, "w", encoding="utf-8").write(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")
print(len(new), "clés ajoutées, 1 retirée")
EOF
git diff --stat RestaurantPOS/Resources/Localizable.xcstrings
```
Expected : `24 clés ajoutées, 1 retirée` ; le diff ne touche que les clés concernées (si tout le fichier est reformaté, la sérialisation diffère : revérifier `indent` et `ensure_ascii` comme au plan 1e).

- [ ] **Step 2: Écrire le contrôleur**

Créer `ios/RestaurantPOS/App/BackupController.swift` :
```swift
import Foundation
import Observation
import PosKit

/// Sauvegarde du mode autonome côté application : état affiché, phrase (Keychain), dossier (signet), exécution quotidienne et export manuel.
@MainActor @Observable
final class BackupController {
    private(set) var status: BackupStatus?
    private(set) var lastError: String?
    private(set) var isRunning = false
    private(set) var hasPassphrase = false
    private(set) var folderName: String?

    @ObservationIgnored private let api: LocalPosAPI
    @ObservationIgnored private let passphrases: BackupPassphraseStore
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let folderOverride: URL?
    private static let bookmarkKey = "backup.folderBookmark"

    init(api: LocalPosAPI, passphrases: BackupPassphraseStore, defaults: UserDefaults, folderOverride: URL?) {
        self.api = api
        self.passphrases = passphrases
        self.defaults = defaults
        self.folderOverride = folderOverride
        hasPassphrase = passphrases.load() != nil
        folderName = resolveFolder()?.lastPathComponent
    }

    var needsAttention: Bool { status?.isOverdue ?? false }
    var neverBackedUp: Bool { status != nil && status?.lastSuccess == nil }

    func refresh() async {
        status = try? await api.backupStatus()
        hasPassphrase = passphrases.load() != nil
        folderName = resolveFolder()?.lastPathComponent
    }

    /// Enregistre la phrase si elle a au moins 8 caractères.
    func setPassphrase(_ passphrase: String) -> Bool {
        guard passphrase.count >= LocalPosAPI.minimumBackupPassphraseLength, passphrases.save(passphrase) else { return false }
        hasPassphrase = true
        return true
    }

    func setFolder(_ url: URL) {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        if let data = try? url.bookmarkData() {
            defaults.set(data, forKey: Self.bookmarkKey)
            folderName = url.lastPathComponent
        }
    }

    /// Au premier plan : sauvegarde si la dernière date de plus de 24 h (ou n'existe pas) et que phrase et dossier sont configurés.
    func runIfDue() async {
        await refresh()
        guard status?.isOverdue == true, let phrase = passphrases.load() else { return }
        _ = await perform(passphrase: phrase, scheduled: true)
    }

    /// Export manuel : la phrase est redemandée à l'utilisateur.
    func exportNow(passphrase: String) async -> Bool { await perform(passphrase: passphrase, scheduled: false) }

    private func perform(passphrase: String, scheduled: Bool) async -> Bool {
        guard let folder = resolveFolder() else {
            lastError = String(localized: "backup.error_no_folder")
            return false
        }
        let scoped = folder.startAccessingSecurityScopedResource()
        defer { if scoped { folder.stopAccessingSecurityScopedResource() } }
        isRunning = true
        defer { isRunning = false }
        do {
            if scheduled { _ = try await api.runScheduledBackup(to: folder, passphrase: passphrase) }
            else { _ = try await api.exportBackup(to: folder, passphrase: passphrase) }
            lastError = nil
            await refresh()
            return true
        } catch {
            lastError = error.localizedDescription
            await refresh()
            return false
        }
    }

    private func resolveFolder() -> URL? {
        if let folderOverride { return folderOverride }
        guard let data = defaults.data(forKey: Self.bookmarkKey) else { return nil }
        var stale = false
        return try? URL(resolvingBookmarkData: data, bookmarkDataIsStale: &stale)
    }
}
```

- [ ] **Step 3: Brancher le contrôleur dans l'environnement et le lancement**

Dans `RestaurantPOSApp.swift` :
1. `LaunchConfiguration` : ajouter `let backupFolder: URL?` ; dans `current`, ajouter l'argument `backupFolder: isUITest ? defaults.string(forKey: "UITestBackupFolder").map { URL(fileURLWithPath: $0, isDirectory: true) } : nil` (`-UITestBackupFolder <chemin>` est lu par `UserDefaults` comme `-UITestPin`).
2. `AppEnvironment` : ajouter `private(set) var backup: BackupController?` (valeur par défaut `nil`). À la fin de `init`, une fois toutes les propriétés stockées affectées (après `model = Self.makeModel(...)`), ajouter :
```swift
        if let api, mode == .standalone {
            let store: BackupPassphraseStore = launch.isUITest ? InMemoryBackupPassphraseStore() : KeychainBackupPassphraseStore()
            backup = BackupController(api: api, passphrases: store, defaults: defaults, folderOverride: launch.backupFolder)
        }
```
3. `RootView.swift` : lire le fichier, repérer la vue racine de `body` et y attacher le déclenchement au premier plan :
```swift
    @Environment(\.scenePhase) private var scenePhase
    // ... sur la vue racine de body :
    .task(id: scenePhase) {
        guard scenePhase == .active else { return }
        await environment.backup?.runIfDue()
    }
```
   (`environment` est l'`AppEnvironment` ; si `RootView` ne le déclare pas, ajouter `@Environment(AppEnvironment.self) private var environment`.)

- [ ] **Step 4: Remplacer le bandeau « non fiscal » par le bandeau de sauvegarde**

Dans `StandaloneScreens.swift`, remplacer la vue `StandaloneBanner` (et son commentaire) par :
```swift
/// Rappel de sauvegarde en mode autonome : l'iPad est le seul dépositaire de la chaîne fiscale.
struct BackupBanner: View {
    let controller: BackupController

    var body: some View {
        HStack(spacing: Theme.Space.s) {
            Image(systemName: "exclamationmark.triangle.fill")
            Text(controller.neverBackedUp ? "backup.banner_never" : "backup.banner_overdue").font(.system(size: 13, weight: .semibold))
            Spacer()
        }
        .foregroundStyle(Theme.warning)
        .padding(.horizontal, Theme.Space.xl)
        .padding(.vertical, Theme.Space.s)
        .background(Theme.warning.opacity(0.14))
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("backup.banner")
    }
}
```
Dans `MainShell.swift`, remplacer `if environment.isStandalone { StandaloneBanner() }` par :
```swift
                if environment.isStandalone, let backup = environment.backup, backup.needsAttention { BackupBanner(controller: backup) }
```

- [ ] **Step 5: Écrire la section de réglages**

Créer `ios/RestaurantPOS/Features/Admin/BackupSettingsSection.swift` :
```swift
import SwiftUI
import PosKit

/// Réglages de sauvegarde du mode autonome : phrase, dossier, état et export manuel.
struct BackupSettingsSection: View {
    @Environment(AppEnvironment.self) private var environment
    @State private var showsPassphrase = false
    @State private var showsExport = false
    @State private var showsFolderPicker = false

    var body: some View {
        if let backup = environment.backup {
            Section("backup.section") {
                LabeledContent("backup.passphrase_label", value: backup.hasPassphrase ? String(localized: "backup.passphrase_set") : String(localized: "backup.passphrase_missing"))
                Button("backup.passphrase_change") { showsPassphrase = true }
                    .accessibilityIdentifier("backup.passphrase.set")
                LabeledContent("backup.folder_label", value: backup.folderName ?? String(localized: "backup.folder_none"))
                    .accessibilityIdentifier("backup.folder")
                Button("backup.folder_choose") { showsFolderPicker = true }
                    .accessibilityIdentifier("backup.folder.choose")
                lastSuccessRow(backup)
                if let error = backup.lastError { Text(error).font(.footnote).foregroundStyle(Theme.warning) }
                Button("backup.export_now") { showsExport = true }
                    .disabled(backup.isRunning)
                    .accessibilityIdentifier("backup.export")
            }
            .task { await backup.refresh() }
            .sheet(isPresented: $showsPassphrase) { PassphraseSheet(backup: backup) }
            .sheet(isPresented: $showsExport) { ExportSheet(backup: backup) }
            .fileImporter(isPresented: $showsFolderPicker, allowedContentTypes: [.folder]) { result in
                if case .success(let url) = result { backup.setFolder(url); Task { await backup.refresh() } }
            }
        }
    }

    @ViewBuilder
    private func lastSuccessRow(_ backup: BackupController) -> some View {
        if let last = backup.status?.lastSuccess {
            LabeledContent("backup.last_success_label", value: last.formatted(date: .abbreviated, time: .shortened))
                .accessibilityIdentifier("backup.lastSuccess")
        } else {
            LabeledContent("backup.last_success_label", value: String(localized: "backup.never"))
                .accessibilityIdentifier("backup.lastSuccess")
        }
    }
}

private struct PassphraseSheet: View {
    let backup: BackupController
    @Environment(\.dismiss) private var dismiss
    @State private var phrase = ""
    @State private var confirmation = ""
    @State private var message: LocalizedStringKey?

    var body: some View {
        NavigationStack {
            Form {
                Section { Text("backup.passphrase_warning").font(.footnote).foregroundStyle(Theme.warning) }
                Section {
                    SecureField("backup.passphrase_field", text: $phrase).accessibilityIdentifier("backup.passphrase.field")
                    SecureField("backup.passphrase_confirm", text: $confirmation).accessibilityIdentifier("backup.passphrase.confirm")
                    if let message { Text(message).font(.footnote).foregroundStyle(Theme.warning) }
                }
            }
            .navigationTitle("backup.passphrase_title")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("backup.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("backup.save") {
                        if phrase != confirmation { message = "backup.passphrase_mismatch" }
                        else if !backup.setPassphrase(phrase) { message = "backup.passphrase_too_short" }
                        else { dismiss() }
                    }
                    .accessibilityIdentifier("backup.passphrase.save")
                }
            }
        }
    }
}

private struct ExportSheet: View {
    let backup: BackupController
    @Environment(\.dismiss) private var dismiss
    @State private var phrase = ""

    var body: some View {
        NavigationStack {
            Form {
                SecureField("backup.export_passphrase", text: $phrase).accessibilityIdentifier("backup.export.passphrase")
                if let error = backup.lastError { Text(error).font(.footnote).foregroundStyle(Theme.warning) }
            }
            .navigationTitle("backup.export_title")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("backup.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("backup.export_confirm") {
                        Task { if await backup.exportNow(passphrase: phrase) { dismiss() } }
                    }
                    .disabled(backup.isRunning)
                    .accessibilityIdentifier("backup.export.confirm")
                }
            }
        }
    }
}
```
Dans `AdminScreen.swift`, `NetworkSettingsView`, branche `if environment.isStandalone { Section("mode.standalone_status") {...} }` : ajouter `BackupSettingsSection()` juste après cette `Section`, à l'intérieur de la branche `if environment.isStandalone`.

- [ ] **Step 6: Compiler**

Run : `cd ios && xcodegen generate && xcodebuild build -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -quiet 2>&1 | tail -15`
Expected : `BUILD SUCCEEDED`, aucun avertissement nouveau. Corriger les erreurs de compilation éventuelles (noms de couleurs du design system : `Theme.warning` existe ; `@Environment`).

- [ ] **Step 7: Adapter et écrire les tests UI**

`PosUITestCase.swift` : ajouter le paramètre `backupFolder: String? = nil` à `launchStandalone(...)` et, avant `app.launch()` :
```swift
        if let backupFolder { app.launchArguments += ["-UITestBackupFolder", backupFolder] }
```
`StandaloneUITests.swift` : les trois `assertExists("standalone.banner")` (lignes ~43, ~71, ~107) deviennent `assertNotExists("standalone.banner")` (le bandeau « non fiscal » n'existe plus), et ajouter :
```swift
    func testNoNonFiscalBannerAndABackupReminderOnAFreshInstallation() {
        launchStandalone()
        assertNotExists("standalone.banner")
        assertExists("backup.banner", timeout: 10)
    }

    func testBackupSettingsExportClearsTheReminder() throws {
        let folder = NSTemporaryDirectory() + "ui-backup-\(UUID().uuidString)"
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        launchStandalone(backupFolder: folder)
        assertExists("backup.banner", timeout: 10)

        tap("nav.admin")
        tap("admin.network")
        tap("backup.passphrase.set")
        fill("backup.passphrase.field", "phrase de test 1")
        fill("backup.passphrase.confirm", "phrase de test 1")
        tap("backup.passphrase.save")

        tap("backup.export")
        fill("backup.export.passphrase", "phrase de test 1")
        tap("backup.export.confirm")

        assertNotExists("backup.banner")
        XCTAssertFalse(label(of: "backup.lastSuccess").isEmpty)
    }

    func testAPassphraseMismatchIsRefused() {
        launchStandalone()
        tap("nav.admin")
        tap("admin.network")
        tap("backup.passphrase.set")
        fill("backup.passphrase.field", "phrase de test 1")
        fill("backup.passphrase.confirm", "phrase de test 2")
        tap("backup.passphrase.save")
        assertExists("backup.passphrase.field")
    }
```
`fill(_:_:)` est la fonction privée déjà définie en tête de `StandaloneUITests`. Si `backup.lastSuccess` n'expose pas son libellé via `label(of:)`, vérifier plutôt `assertNotExists("backup.banner")` seul.

- [ ] **Step 8: Lancer les tests unitaires, puis les tests UI**

Run :
```bash
cd ios && ./scripts/test.sh unit 2>&1 | tail -3
xcodebuild test -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -only-testing:RestaurantPOSUITests/StandaloneUITests 2>&1 | tail -15
./scripts/test.sh ui 2>&1 | tail -10
```
Expected : unitaires 387 verts ; `StandaloneUITests` 6 + 3 = 9 verts ; suite UI complète 50 verts (47 + 3, environ 8 minutes). Si un test UI existant dépendait de la place du bandeau, ajuster l'assertion, jamais l'identifiant.

- [ ] **Step 9: Suivi et commit**

Ajouter la ligne 9 au suivi (PosKit 387, UI 50).
```bash
git add -A ios docs/plans/ipad-standalone-2-progress.md
git commit -m "feat(ios): réglages et bandeau de sauvegarde en mode autonome, retrait du bandeau non fiscal"
```

---

### Task 10: Documentation, règle de suivi .NET/Swift et vérification finale

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/superpowers/specs/2026-10-10-ipad-standalone-2-fiscal-design.md`
- Modify: `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md`
- Modify: `docs/plans/ipad-standalone-2-progress.md`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: documentation à jour ; plus aucune mention de `NonFiscalPayments` ou de « non fiscal » dans le code ou l'UI.

- [ ] **Step 1: Corriger la spec du sous-projet 2**

Dans `docs/superpowers/specs/2026-10-10-ipad-standalone-2-fiscal-design.md` :
- Section « Données : migration V5 » : remplacer la phrase sur `TechnicalJournalEntries` par : `Création de \`FiscalReceipts\`, \`PaymentTenders\` et \`JournalEntries\` (nom du \`DbSet\` du JET) avec les mêmes colonnes que \`AppDbContext\`, y compris \`VoidedReceiptId\`. Il n'y a pas de colonne \`DuplicateNumber\` sur les reçus : comme le .NET, le numéro de duplicata se déduit du JET (nombre d'entrées \`DUPLICATE_PRINTED\` du document + 1).`
- Section « Règles métier », ligne Duplicata : remplacer `incrémente \`DuplicateNumber\`` par `numérote le duplicata d'après le JET`.
- Section « Règles métier », ajouter : `- **Parité des montants** : TVA et HT du reçu reproduisent \`CheckoutPaymentService\`, y compris ses écarts actuels (JSON de TVA sans taux à emporter ni remise globale, HT sans remise en montant fixe) ; les corriger est une décision séparée des deux côtés.`
- Section « Règles métier », ligne Solde nul et chambre : ajouter `Le .NET n'écrit pas de reçu pour une facturation chambre ; l'iPad en écrit un (règlement « chambre ») : écart assumé.`
- Remplacer `Statut : en attente de relecture` par `Statut : implémentée (plan 2026-10-10-ipad-standalone-2-fiscal-nf525)`.
- Section « Interface » : remplacer « Le bandeau « non fiscal » est retiré. » par « Le bandeau « non fiscal » est remplacé par un bandeau de rappel de sauvegarde (jamais sauvegardé, ou plus de 24 h). »

Dans `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md`, remplacer la phrase « Tant que le sous-projet 2 n'est pas terminé, le mode autonome est marqué « non fiscal » et ne doit pas servir à encaisser. » par « Le sous-projet 2 est terminé : le mode autonome émet des reçus fiscaux NF525. L'attestation NF525 doit toutefois couvrir ce mode avant tout usage réel. »

- [ ] **Step 2: Mettre à jour `CLAUDE.md`**

Dans le paragraphe « Le mode autonome de l'iPad (`LocalPosAPI`, …) », remplacer la phrase `Les tables \`NonFiscalPayments\` (provisoire, remplacée par les reçus fiscaux au sous-projet 2) et \`LocalCounters\` sont propres à l'iPad et absentes de \`AppDbContext\`, de même que \`LocalPinFailures\` et \`LocalPinLockout\` (verrouillage des PIN persistant).` par `Les tables \`LocalCounters\`, \`LocalPinFailures\` et \`LocalPinLockout\` (verrouillage des PIN persistant) sont propres à l'iPad et absentes de \`AppDbContext\`. Les reçus fiscaux (\`FiscalReceipts\`, \`PaymentTenders\`, \`JournalEntries\`) existent depuis le sous-projet 2, avec des triggers SQL d'immuabilité ; clôtures et archives viennent au sous-projet 3.`

Ajouter, juste après ce paragraphe :
```markdown
**Parité fiscale .NET/Swift.** `FiscalHashing.cs`, `CheckoutPaymentService` (montants, TVA, HT, annulation) et les charges utiles du JET sont portés dans `ios/Packages/PosKit/Sources/PosKit/Local/` (`FiscalHashing`, `FiscalReceiptMath`, `LocalFiscalRepository`). La fixture `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json` est **générée par le .NET** (`WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests`) et rejouée par les tests Swift. Toute modification fiscale côté .NET (formule, montants, charge utile du JET) impose de régénérer cette fixture, de reporter le changement dans `Local/` et d'ajouter une migration si le schéma change. Écarts .NET reproduits volontairement pour la parité : le JSON de TVA d'un reçu ignore le taux à emporter et la remise globale ; le HT ignore la remise globale en montant fixe. Les corriger se fait des deux côtés en même temps. Le JSON signé suit `System.Text.Json` (échappements `\uXXXX`) et les horodatages signés suivent `DateTimeOffset.ToString("O")` (`+00:00`, 7 décimales). La sauvegarde chiffrée (AES-GCM, clé PBKDF2 dérivée d'une phrase saisie par l'utilisateur, fichier `.posbak`) est écrite par `LocalPosAPI.exportBackup` / `runScheduledBackup` ; `backup(to:)` produit une copie non chiffrée réservée aux tests. Les événements `BACKUP_COMPLETED` / `BACKUP_FAILED` du JET n'existent que sur l'iPad.
```

- [ ] **Step 3: Vérifications finales**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
grep -rn "NonFiscalPayments\|non_fiscal\|NF-T0" ios/RestaurantPOS ios/Packages/PosKit/Sources ios/Packages/PosKit/Tests | grep -v "LocalSchemaV3.swift\|LocalSchemaV5.swift"
dotnet build RestaurantPos.slnx 2>&1 | tail -3
dotnet test RestaurantPos.slnx 2>&1 | tail -6
cd ios && ./scripts/test.sh unit 2>&1 | tail -3 && ./scripts/test.sh ui 2>&1 | tail -6
cd .. && git diff --stat main -- src/
```
Expected : le `grep` ne renvoie rien ; build .NET sans avertissement ; tests .NET tous verts ; PosKit 387 verts ; UI 50 verts ; `git diff --stat main -- src/` vide (aucun code .NET de production modifié).

- [ ] **Step 4: Suivi final et commit**

Compléter `docs/plans/ipad-standalone-2-progress.md` : ligne 10 (fait, totaux .NET / PosKit 387 / UI 50), puis :
```markdown
Sous-projet 2 « Fiscal NF525 » : terminé (condition de sortie : `FiscalHashingTests` et `FiscalReceiptMathTests` rejouent `fiscal_reference.json`, généré par le .NET).

Conditions d'entrée du sous-projet 3 :
1. Tables `DailyFiscalClosures`, `FiscalPeriodClosures`, `FiscalArchives` (migration V6) avec leurs triggers, et extension de `verifyChains` aux chaînes restantes.
2. Section Fiscal visible en mode autonome (retirer le masquage de `SidebarRail`), rapports X et Z, FEC, export via le partage.
3. Impression des duplicata : `reprintReceipt` ne fait que numéroter et journaliser (`printQueued: false`) tant que le sous-projet 4 n'existe pas.
4. Interface d'annulation de reçu : `voidReceipt` n'a pas d'écran (le client serveur n'en a pas non plus).
5. Écran de réglages de la base et restauration depuis l'interface : `LocalBackupService.restore` existe et est testée, sans écran.
6. Attestation NF525 à faire couvrir le mode autonome avant tout usage réel.
7. Décider de corriger ou non les écarts .NET reproduits pour la parité (TVA et HT du reçu), des deux côtés.
```
```bash
git add CLAUDE.md docs
git commit -m "docs: sous-projet 2 du mode autonome terminé, règle de parité fiscale .NET/Swift"
```

---

## Self-review (exécutée à l'écriture du plan)

- **Couverture de la spec** : reçus et règlements signés (tâches 1 à 5) ; annulation, duplicata, JET (4, 5) ; triggers et suppression de `NonFiscalPayments` (3, 5) ; solde nul et chambre (5) ; audit Happy Hour et mise en attente (6) ; règle des décimaux (le JSON de TVA est construit depuis les centimes et les taux `Decimal`, jamais relu en texte : 2) ; sauvegarde chiffrée, quotidienne, restauration (7, 8, 9) ; bandeau retiré, réglages, FR/EN/AR (9) ; parité .NET et règle `CLAUDE.md` (1, 2, 10) ; migration V5 d'une base V4 (3). Écarts de la spec corrigés en tâche 10 : table `JournalEntries`, pas de `DuplicateNumber`, écart chambre assumé.
- **Placeholders** : aucun `TBD`. Trois étapes demandent d'adapter un appel à une signature à vérifier par `grep` (`compItem`, `holdOrder`) ou de lire `RootView.swift` : ce sont des fichiers que le plan n'a pas pu relire intégralement, la commande de vérification est donnée.
- **Cohérence des types** : `FiscalReceiptAmounts` (tâche 2) est consommé par `appendReceipt` (4) et `settle` (5) ; `FiscalTenderInput` et `FiscalReceiptRecord` définis en 4 et utilisés en 5 ; `LocalJournalEvents` défini en 4 et utilisé en 5, 6, 8 ; `BackupStatus` et `minimumBackupPassphraseLength` définis en 8 et utilisés en 9 ; `LocalBackupFailure` défini en 7.
- **Review Focus** : 1 → tâches 1 et 6 ; 2 → tâche 2 (neuf scénarios dont partiel, pourboire, remises, taux réduit) ; 3 → tâche 5 ; 4 → tâches 4 et 5 ; 5 → tâches 7 et 8 ; 6 → tâche 4.
