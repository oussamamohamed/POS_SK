# Lot 4a — Document et rendu des tickets — Plan d'implémentation

> **Pour les agents d'exécution :** SOUS-SKILL REQUIS : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans. Les étapes utilisent des cases `- [ ]`.

**Objectif :** porter en Swift le modèle de ticket, les constructeurs de documents, le rendu texte ESC/POS (octets identiques au .NET) et un rendu raster CoreText, avec une fixture générée par le .NET.

**Architecture :** un nouveau dossier `Sources/PosKit/Printing/` (internal, `@testable` dans les tests) ne dépend ni de SQLite ni du réseau : les constructeurs prennent des valeurs d'entrée simples (`Ticket*` dans `TicketInputs.swift`) que le lot 4b remplira depuis la base. La fixture `ticket_document_reference.json` est générée par le .NET et rejouée par Swift : `TicketDocument` JSON, octets du rendu texte, commandes `GS v 0`, table PC858, textes localisés. Le raster CoreText n'est pas comparable octet pour octet (polices et lissage) : tests structurels.

**Tech Stack :** Swift 6, Swift Testing, CoreText/CoreGraphics (aucune dépendance), .NET 9 + xUnit pour le générateur de fixture.

**Spec :** `docs/superpowers/specs/2026-10-22-ipad-standalone-4-impression-design.md` (lot 4a). Référence .NET : `src/RestaurantPos.Infrastructure/Printing/` (`TicketDocument.cs`, `TicketDocumentJson.cs`, `TicketDocumentBuilder.cs`, `EscPosCommands.cs`, `EscPosTextRenderer.cs`, `EscPosRasterRenderer.cs`) et `Localization/Texts.cs` + `SharedResource*.resx`.

## Global Constraints

- Branche de travail : `feat/ipad-standalone-4a-rendu` (créée depuis `feat/ipad-standalone-4-impression`). Pousser à la fin de chaque tâche.
- `PosKit` reste sans dépendance externe. CoreText et CoreGraphics sont des frameworks système.
- Montants en centimes entiers (`Int`) ; jamais de `Double` pour un montant. `amount(cents)` = `(cents / 100m).ToString("0.00", Invariant)` du .NET (signe `-` pour un négatif).
- Les longueurs de texte du rendu texte sont des **longueurs UTF-16** (`.Length` du .NET). En Swift : `[UInt16]` (`Array(string.utf16)`), jamais `String.count`.
- Dates : jamais de `DateFormatter` ni de `Locale` utilisateur (chiffres arabes). Composants du calendrier grégorien à la main, `en_US_POSIX`, fuseau explicite.
- La fixture `ticket_document_reference.json` est **la référence** : tout écart avec ce plan se tranche en faveur de la fixture (le plan a pu mal lire le .NET) ; consigner dans `docs/plans/ipad-standalone-4a-progress.md`.
- Les tests du .NET et de PosKit tournent en entier à chaque tâche (`dotnet test RestaurantPos.slnx`, `cd ios/Packages/PosKit && swift test`). Mettre à jour `docs/plans/ipad-standalone-4a-progress.md` après chaque tâche (statut, compteurs .NET / PosKit, constats ouverts).
- Chaînes visibles de l'interface : aucune dans ce lot (les textes de ticket viennent de la table `TicketTexts`, pas de `Localizable.strings`).
- Types déjà présents à réutiliser : `Printer` (`Models/OperationsModels.swift`), `OrderDestination`, `CourseType`, `PaymentMethod` (`Models/Enums.swift`).
- Style : celui de `Local/` (commentaires français, `// MARK:`, pas de commentaire redondant). Fichiers petits et ciblés.

## Review Focus

Entrées que la spec laisse implicites et que les tests de ce lot épinglent :

1. Texte avec emoji ou caractère hors PC858 : un `?` par caractère, largeur comptée en UTF-16 (emoji = 2). Tâche 3.
2. Mot plus long que la ligne, ligne vide, texte vide : coupe nette, au moins une ligne. Tâche 3.
3. Langue inconnue (`"de"`) : repli anglais, comme le .NET. Tâche 5.
4. Rapport sans période précédente (`periodStartUtc` absent = « - ») et clôture de période à JSON de taxes ou de règlements invalide : document produit, sans plantage. Tâche 5.
5. Document arabe (RTL) : colonnes inversées, alignement miroir, image non vide. Tâche 6.

## File Structure

- Créer `ios/Packages/PosKit/Sources/PosKit/Printing/TicketDocument.swift` : enum `TicketAlign`, enum `TicketLine`, struct `TicketDocument`, `TicketDocumentJSON` (Codable à discriminant `type`, compatible `System.Text.Json` Web).
- Créer `…/Printing/TicketInputs.swift` : `TicketSettings`, `TicketReceipt`, `TicketOrder`, `TicketOrderItem`, `TicketKitchen`, `TicketKitchenItem`, `TicketReportSummary`, `TicketDailyClosure`, `TicketPeriodClosure`, `ReportPrintData`… (entrées des constructeurs).
- Créer `…/Printing/TicketFormat.swift` : `amount`, `utcDateTime`, `localDateTime`, `localTime`, `rate`.
- Créer `…/Printing/TicketTexts.swift` (généré) et `ios/scripts/gen_ticket_texts.py` : table FR/EN/AR des clés `receipt.*`, `report.*`, `kitchen_ticket.*`, `admin.payment_method_*`.
- Créer `…/Printing/TicketBuilder.swift` : port de `TicketDocumentBuilder`.
- Créer `…/Printing/Pc858.swift`, `EscPosCommands.swift`, `EscPosTextRenderer.swift`.
- Créer `…/Printing/TicketRasterRenderer.swift` + `Resources/Fonts/*.ttf` (copiées du .NET).
- Créer côté .NET : `tests/RestaurantPos.Infrastructure.Tests/TicketDocumentReferenceFixtureTests.cs`.
- Créer côté Swift tests : `Tests/PosKitTests/Printing/*Tests.swift` et `Fixtures/ticket_document_reference.json`.
- Modifier : `CLAUDE.md` (paragraphe de parité), `docs/plans/ipad-standalone-4a-progress.md` (nouveau).

---

### Tâche 1 : Fixture .NET (source de vérité)

**Files:**
- Create: `tests/RestaurantPos.Infrastructure.Tests/TicketDocumentReferenceFixtureTests.cs`
- Create (généré) : `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/ticket_document_reference.json`

**Interfaces:**
- Produces : le fichier JSON ci-dessous, consommé par les tâches 2 à 6.

Forme du fichier (camelCase, indenté, LF) :

```json
{
  "timeZone": "Europe/Paris",
  "softwareVersion": "1.0.0",
  "texts": { "en": {"receipt.ticket": "Receipt"}, "fr": {}, "ar": {} },
  "pc858": [0, 1, 2],
  "encode": [ { "text": "é€", "hex": "82d5" } ],
  "wrap": [ { "text": "…", "width": 10, "lines": ["…"] } ],
  "commands": [ { "name": "…", "drawer": true, "segments": [ { "width": 8, "height": 2, "bitsHex": "ff00" } ], "expectedHex": "1b40…" } ],
  "textRender": [ { "name": "…", "paperWidthMm": 80, "drawer": false, "document": { "language": "fr", "rightToLeft": false, "lines": [] }, "expectedHex": "…" } ],
  "documents": [ { "name": "…", "kind": "receipt", "language": "fr", "input": { }, "expected": { "language": "fr", "rightToLeft": false, "lines": [] } } ]
}
```

- `texts.<lang>` : pour **chaque clé** de `Texts.Keys(en)` commençant par `receipt.`, `report.`, `kitchen_ticket.` ou `admin.payment_method_`, la valeur de `Texts.Get(culture, clé)` (repli anglais compris).
- `pc858[i]` : le point de code Unicode que le .NET décode pour l'octet `i` (0 à 255) avec `Encoding.GetEncoding(858)`.
- `encode` : échantillons `Encoding.GetEncoding(858, new EncoderReplacementFallback("?"), …).GetBytes(text)` en hexadécimal minuscule : `"àâçéèêëîïôùû ÀÉÈ — 0123456789"`, `"€"`, `"😀"` (un seul `?`), `"مرحبا"`, `"ＡＢ"`, `""`, `"a\tb"`.
- `wrap` : `EscPosTextRenderer.Wrap` sur `("", 10)`, `("hello", 10)`, `("aaaaaaaaaaaaaaaaaaaaaaaa", 10)` (mot plus long que la ligne), `("un deux trois quatre cinq six", 10)`, `("  espaces   multiples  ", 10)`, `("😀😀😀😀😀😀", 4)`.
- `commands` : `EscPosCommands.Build` sur : une image 8×2 sans tiroir ; deux segments (16×3 puis 8×1) avec tiroir ; une image 8×300 (deux bandes : 256 puis 44 lignes) ; liste vide. `bitsHex` = `Bits` en hexadécimal.
- `textRender` : `EscPosTextRenderer.Render` sur au moins 6 documents : texte aligné centre/droite, gras, large, colonnes courtes, colonnes trop longues (étiquette et valeur coupées), séparateur simple, séparateur `Cut` au milieu et en fin de document, 58 mm et 80 mm, avec et sans tiroir, texte avec accents et `€`, texte avec emoji.
- `documents` : un élément par scénario du tableau ci-dessous. `language` = la langue demandée au constructeur (par exemple `"de"` pour le repli). `input` : objet camelCase dont les clés sont celles de `TicketScenarioInput` (voir tâche 4) — **les dates en ISO 8601 UTC `yyyy-MM-ddTHH:mm:ss.fffZ`, les montants en centimes, `null` pour une valeur absente (dont `DateTimeOffset.MinValue` → `null`)**.

| `kind` | Scénarios (noms de la fixture) |
|---|---|
| `receipt` | `receipt_fr_full` (réglages complets avec certificat, 2 lignes dont une avec modificateurs, sur place), `receipt_en_default_header` (réglages `null`, à emporter), `receipt_ar`, `receipt_duplicate_2`, `receipt_unknown_language` (`"de"`), `receipt_empty_settings_strings` (chaînes vides et espaces → valeurs par défaut) |
| `receiptFallback` | `fallback_fr`, `fallback_duplicate` |
| `fiscalReceipt` | `fiscal_receipt_buzzer`, `fiscal_receipt_no_buzzer` |
| `pickupCoupon` | `pickup_modifiers`, `pickup_no_buzzer` |
| `kitchenTicket` | `kitchen_takeaway_pickup_buzzer_comment`, `kitchen_table_courses` (service retrouvé par produit, un produit absent de la commande → Direct), `kitchen_ar` |
| `xReport` | `x_first_report` (début absent → « - »), `x_with_details` (catégories dont une sans nom, pourboires dont un serveur inconnu, remise globale), `x_summer_dst` (heures d'été), `x_winter` |
| `zClosure` | `z_fr`, `z_duplicate_certificate`, `z_rates` (taux 5.5, 10, 20, 2.1, 0.125 dans le désordre) |
| `periodClosure` | `period_monthly`, `period_annual`, `period_invalid_json`, `period_tender_keys` (noms, nombres en chaîne, clé inconnue) |
| `testPage` | `test_fr_80`, `test_ar_58` |
| `withDuplicateNotice` | `duplicate_notice_replaces` (document qui contient déjà « DUPLICATA »), `duplicate_notice_inserts` |

Les `input` des scénarios `withDuplicateNotice` contiennent un `document` (objet `TicketDocument`) et `duplicateNumber`.

- [ ] **Étape 1 : écrire le test générateur**

Reprendre le patron de `PeriodClosureReferenceFixtureTests.cs` : `FixturePath()` (racine par `RestaurantPos.slnx`), options JSON `WriteIndented` + camelCase, écriture si `WRITE_TICKET_FIXTURE=1`, sinon comparaison avec le fichier committé (même logique que ce test). Ajouter :

```csharp
[Fact]
public void Fixture_is_generated_in_paris_time()
{
    TimeZoneInfo.Local.Id.Should().BeOneOf("Europe/Paris", "Romance Standard Time");
}
```

La commande de génération est `TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests` (macOS ou Linux). `softwareVersion` s'obtient par réflexion sur la méthode privée `GetSoftwareVersion` de `TicketDocumentBuilder` (`BindingFlags.NonPublic | BindingFlags.Static`). Les documents sont sérialisés avec `TicketDocumentJson.Serialize` puis relus par `JsonDocument.Parse` pour être insérés comme objets (pas comme chaînes). Construire les entités du domaine par initialiseurs d'objet (`FiscalReceipt { TerminalId = "T01", ReceiptNumber = … }`, `Order`, `OrderItem { ProductName = … }`, `KitchenTicket`…) ; le montant TTC d'une ligne du `input` est `item.CalculateTotalTtc().AmountInCents` calculé par le .NET.

- [ ] **Étape 2 : lancer sans écrire — doit échouer (fichier absent)**

Run : `dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`
Attendu : échec « fixture absente ».

- [ ] **Étape 3 : générer, relire, relancer**

Run : `TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`, puis relancer sans la variable : PASS. Vérifier à l'œil dans le fichier : « 5.5 % », « - » pour `x_first_report`, « DUPLICATA n°2 », une ligne arabe.

- [ ] **Étape 4 : suite .NET complète, commit**

Run : `dotnet test RestaurantPos.slnx` (tout vert).

```bash
git add tests/RestaurantPos.Infrastructure.Tests/TicketDocumentReferenceFixtureTests.cs ios/Packages/PosKit/Tests/PosKitTests/Fixtures/ticket_document_reference.json docs/plans/ipad-standalone-4a-progress.md
git commit -m "test(dotnet): fixture ticket_document_reference.json générée par le .NET (4a)"
```

---

### Tâche 2 : Modèle `TicketDocument` et JSON

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Printing/TicketDocument.swift`
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Printing/TicketDocumentTests.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Printing/TicketReferenceFixture.swift` (chargeur partagé)

**Interfaces:**
- Produces : `TicketAlign` (`Int` : start 0, center 1, end 2), `TicketLine` (`.text(String, align:, bold:, large:)`, `.columns(label:value:)`, `.separator(cut:)`), `TicketDocument(language:rightToLeft:lines:)` (`Equatable`, `Codable`, `Sendable`), `TicketDocumentJSON.serialize(_:) throws -> String`, `TicketDocumentJSON.deserialize(_:) throws -> TicketDocument`.
- Produces (tests) : `TicketReferenceFixture.load()` → struct `Decodable` de la fixture (sections `texts`, `pc858`, `encode`, `wrap`, `commands`, `textRender`, `documents` ; `documents[i].inputData` et `.expectedData` sont les octets JSON bruts de `input` et `expected`, que la tâche 5 décode en `TicketScenarioInput` et `TicketDocument`).

Le JSON .NET (`JsonSerializerDefaults.Web`) : objets `{"type":"TicketText","text":"…","align":1,"bold":false,"large":false}`, `{"type":"TicketColumns","label":"…","value":"…"}`, `{"type":"TicketSeparator","cut":false}` ; énumération en nombre ; propriétés camelCase ; lecture insensible à la casse ; valeurs absentes = valeurs par défaut.

- [ ] **Étape 1 : écrire les tests**

```swift
import Foundation
import Testing
@testable import PosKit

@Suite struct TicketDocumentTests {
    @Test func roundTripKeepsEveryLineKind() throws {
        let doc = TicketDocument(language: "fr", rightToLeft: false, lines: [
            .text("Bonjour", align: .center, bold: true, large: true),
            .columns(label: "Total", value: "12.50"),
            .separator(cut: false),
            .separator(cut: true)
        ])
        let json = try TicketDocumentJSON.serialize(doc)
        #expect(try TicketDocumentJSON.deserialize(json) == doc)
    }

    @Test func readsDotNetShapeWithDefaults() throws {
        let json = #"{"language":"en","rightToLeft":true,"lines":[{"type":"TicketText","text":"A"},{"type":"TicketColumns","label":"L","value":"V"},{"type":"TicketSeparator"}]}"#
        let doc = try TicketDocumentJSON.deserialize(json)
        #expect(doc == TicketDocument(language: "en", rightToLeft: true, lines: [.text("A"), .columns(label: "L", value: "V"), .separator()]))
    }

    @Test func writesAlignAsNumberAndTypeDiscriminator() throws {
        let json = try TicketDocumentJSON.serialize(TicketDocument(language: "en", rightToLeft: false, lines: [.text("x", align: .end)]))
        #expect(json.contains(#""type":"TicketText""#))
        #expect(json.contains(#""align":2"#))
    }

    @Test func unknownTypeThrows() {
        #expect(throws: (any Error).self) {
            try TicketDocumentJSON.deserialize(#"{"language":"en","rightToLeft":false,"lines":[{"type":"Nope"}]}"#)
        }
    }

    @Test func fixtureDocumentsDecode() throws {
        let fixture = try TicketReferenceFixture.load()
        #expect(!fixture.documents.isEmpty)
        for entry in fixture.documents {
            #expect(throws: Never.self) { _ = try JSONDecoder().decode(TicketDocument.self, from: entry.expectedData) }
        }
    }
}
```

- [ ] **Étape 2 : lancer — échec de compilation attendu** (`TicketDocument` inconnu) : `cd ios/Packages/PosKit && swift test --filter TicketDocumentTests`.

- [ ] **Étape 3 : implémenter**

```swift
import Foundation

enum TicketAlign: Int, Codable, Sendable { case start = 0, center = 1, end = 2 }

enum TicketLine: Equatable, Sendable {
    case text(String, align: TicketAlign = .start, bold: Bool = false, large: Bool = false)
    case columns(label: String, value: String)
    case separator(cut: Bool = false)
}

struct TicketDocument: Equatable, Sendable {
    var language: String
    var rightToLeft: Bool
    var lines: [TicketLine]
}

extension TicketLine: Codable {
    private enum Keys: String, CodingKey { case type, text, align, bold, large, label, value, cut }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        switch try c.decode(String.self, forKey: .type) {
        case "TicketText":
            self = .text(try c.decode(String.self, forKey: .text), align: try c.decodeIfPresent(TicketAlign.self, forKey: .align) ?? .start,
                         bold: try c.decodeIfPresent(Bool.self, forKey: .bold) ?? false, large: try c.decodeIfPresent(Bool.self, forKey: .large) ?? false)
        case "TicketColumns":
            self = .columns(label: try c.decode(String.self, forKey: .label), value: try c.decode(String.self, forKey: .value))
        case "TicketSeparator":
            self = .separator(cut: try c.decodeIfPresent(Bool.self, forKey: .cut) ?? false)
        case let other:
            throw DecodingError.dataCorruptedError(forKey: .type, in: c, debugDescription: "Type de ligne inconnu : \(other)")
        }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        switch self {
        case let .text(text, align, bold, large):
            try c.encode("TicketText", forKey: .type); try c.encode(text, forKey: .text); try c.encode(align, forKey: .align)
            try c.encode(bold, forKey: .bold); try c.encode(large, forKey: .large)
        case let .columns(label, value):
            try c.encode("TicketColumns", forKey: .type); try c.encode(label, forKey: .label); try c.encode(value, forKey: .value)
        case let .separator(cut):
            try c.encode("TicketSeparator", forKey: .type); try c.encode(cut, forKey: .cut)
        }
    }
}

extension TicketDocument: Codable {}

/// Même JSON que `TicketDocumentJson` du .NET (System.Text.Json « Web »).
enum TicketDocumentJSON {
    static func serialize(_ document: TicketDocument) throws -> String {
        String(decoding: try JSONEncoder().encode(document), as: UTF8.self)
    }

    static func deserialize(_ json: String) throws -> TicketDocument {
        try JSONDecoder().decode(TicketDocument.self, from: Data(json.utf8))
    }
}
```

Le chargeur de fixture (`TicketReferenceFixture`) suit le chargeur de `LocalFecReferenceTests` (ressource `Fixtures/…` via `Bundle.module`). `documents[i].expectedData` = les octets JSON de l'objet `expected` ; `inputData` = ceux de `input` (utiliser `JSONSerialization` pour ré-encoder l'objet).

- [ ] **Étape 4 : lancer** — PASS : `swift test --filter TicketDocumentTests`.
- [ ] **Étape 5 : suite PosKit complète, commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Printing ios/Packages/PosKit/Tests/PosKitTests/Printing docs/plans/ipad-standalone-4a-progress.md
git commit -m "feat(ios): modèle TicketDocument et JSON compatible .NET (4a)"
```

---

### Tâche 3 : Rendu texte ESC/POS et commandes (octets identiques)

**Files:**
- Create: `…/Printing/Pc858.swift`, `EscPosCommands.swift`, `EscPosTextRenderer.swift`
- Test: `…/Tests/PosKitTests/Printing/EscPosTextRendererTests.swift`, `EscPosCommandsTests.swift`, `Pc858Tests.swift`

**Interfaces:**
- Consumes : `TicketDocument` (tâche 2), fixture (tâche 1).
- Produces : `struct MonoImage: Equatable, Sendable { let width: Int; let height: Int; let bits: [UInt8]; var bytesPerRow: Int { width / 8 } }` ; `EscPosCommands.bandHeight` (256) et `EscPosCommands.build(_ segments: [MonoImage], openCashDrawer: Bool) -> [UInt8]` ; `Pc858.encode(_ units: [UInt16]) -> [UInt8]` ; `EscPosTextRenderer.columns(forPaperWidthMm:) -> Int`, `.wrap(_ text: String, width: Int) -> [[UInt16]]`, `.render(_ doc: TicketDocument, paperWidthMm: Int, openCashDrawer: Bool) -> [UInt8]`.

Règles exactes (port de `EscPosTextRenderer.cs` et `EscPosCommands.cs`, lus en entier avant d'écrire) :

- `Init` = `1B 40 1B 74 13` ; `DrawerKick` = `1B 70 00 19 FA` ; `FeedAndCut` = `1D 56 42 00`.
- Colonnes : 32 (58 mm) sinon 48.
- Les `separator(cut: true)` finaux sont retirés ; un `separator(cut: true)` intermédiaire écrit `FeedAndCut` ; un séparateur simple écrit `1B 61 00` puis `cols` tirets et `0A`. Tiroir avant la coupe finale, puis `FeedAndCut`.
- `text` : `1B 61 n` (0 début, 1 centre, 2 fin) ; gras `1B 45 01` / `1B 45 00` ; large `1D 21 11` / `1D 21 00` ; coupe `wrap(text, large ? cols / 2 : cols)`.
- `columns` : `1B 61 00` ; si `label + 1 + value ≤ cols` (UTF-16) : étiquette, `cols - labelLen - valueLen` espaces, valeur ; sinon étiquette coupée, puis valeur coupée avec remplissage à gauche jusqu'à `cols`.
- `wrap` : découpe sur `" "` seulement (entrées vides ignorées) ; un mot plus long que la largeur est coupé net ; toujours au moins une ligne (vide).
- Encodage : chaque point de code Unicode → l'octet PC858 inverse de `pc858` de la fixture (premier octet gagnant), sinon `3F`. Une paire de substituts valide = un seul `?` ; un substitut isolé = `?` (les unités UTF-16 sont rassemblées par `String(decoding: units, as: UTF16.self)` puis parcourues en scalaires ; U+FFFD n'est pas dans la table donc `?`).

- [ ] **Étape 1 : écrire les tests** (tous pilotés par la fixture)

```swift
@Suite struct Pc858Tests {
    @Test func tableMatchesDotNetForEveryByte() throws {
        let f = try TicketReferenceFixture.load()
        #expect(f.pc858.count == 256)
        for byte in 0..<256 {
            let scalar = UnicodeScalar(UInt32(f.pc858[byte]))!
            let encoded = Pc858.encode(Array(String(scalar).utf16))
            // Si deux octets décodent vers le même caractère, l'inverse prend le premier.
            #expect(encoded.first == UInt8(f.pc858.firstIndex(of: f.pc858[byte])!))
        }
    }

    @Test func encodeSamplesMatchDotNet() throws {
        for sample in try TicketReferenceFixture.load().encode {
            #expect(hex(Pc858.encode(Array(sample.text.utf16))) == sample.hex, "« \(sample.text) »")
        }
    }
}

@Suite struct EscPosTextRendererTests {
    @Test func wrapMatchesDotNet() throws {
        for w in try TicketReferenceFixture.load().wrap {
            #expect(EscPosTextRenderer.wrap(w.text, width: w.width).map { String(decoding: $0, as: UTF16.self) } == w.lines, "« \(w.text) »")
        }
    }

    @Test func renderIsByteIdenticalToDotNet() throws {
        for r in try TicketReferenceFixture.load().textRender {
            let doc = try JSONDecoder().decode(TicketDocument.self, from: r.documentData)
            #expect(hex(EscPosTextRenderer.render(doc, paperWidthMm: r.paperWidthMm, openCashDrawer: r.drawer)) == r.expectedHex, r.name)
        }
    }

    @Test func emojiCountsAsTwoUnitsAndPrintsOneQuestionMark() {
        let out = EscPosTextRenderer.render(TicketDocument(language: "fr", rightToLeft: false, lines: [.columns(label: "😀", value: "1")]), paperWidthMm: 58, openCashDrawer: false)
        #expect(out.contains(0x3F))
        #expect(!out.contains(0xF0))
    }
}
```

`hex(_:)` : fonction d'aide des tests (`bytes.map { String(format: "%02x", $0) }.joined()`). `EscPosCommandsTests` : pour chaque `commands` de la fixture, construire les `MonoImage` depuis `bitsHex` et comparer `build` en hexadécimal.

- [ ] **Étape 2 : lancer** — échec de compilation attendu.
- [ ] **Étape 3 : implémenter** `Pc858.swift` (table `static let table: [UInt32]` de 256 valeurs **copiée de la fixture** `pc858`, plus un dictionnaire inverse construit au chargement), `EscPosCommands.swift` et `EscPosTextRenderer.swift` d'après les règles ci-dessus.

```swift
struct MonoImage: Equatable, Sendable {
    let width: Int
    let height: Int
    let bits: [UInt8]
    var bytesPerRow: Int { width / 8 }
}

enum EscPosCommands {
    static let bandHeight = 256
    private static let initSeq: [UInt8] = [0x1B, 0x40]
    private static let drawerKick: [UInt8] = [0x1B, 0x70, 0x00, 0x19, 0xFA]
    private static let feedAndCut: [UInt8] = [0x1D, 0x56, 0x42, 0x00]

    static func build(_ segments: [MonoImage], openCashDrawer: Bool) -> [UInt8] {
        var bytes = initSeq
        for (i, img) in segments.enumerated() {
            var top = 0
            while top < img.height {
                let rows = min(bandHeight, img.height - top)
                bytes += [0x1D, 0x76, 0x30, 0x00, UInt8(img.bytesPerRow & 0xFF), UInt8(img.bytesPerRow >> 8), UInt8(rows & 0xFF), UInt8(rows >> 8)]
                bytes += img.bits[(top * img.bytesPerRow)..<((top + rows) * img.bytesPerRow)]
                top += bandHeight
            }
            if openCashDrawer && i == segments.count - 1 { bytes += drawerKick }
            bytes += feedAndCut
        }
        return bytes
    }
}
```

Le rendu texte travaille sur `[UInt16]` de bout en bout (étiquettes, valeurs, remplissage) et n'encode qu'à l'écriture d'une ligne (`Pc858.encode(units) + [0x0A]`).

- [ ] **Étape 4 : lancer** `swift test --filter "Pc858Tests|EscPosTextRendererTests|EscPosCommandsTests"` — PASS. Si un échantillon diverge, la fixture a raison : corriger le Swift.
- [ ] **Étape 5 : suite PosKit complète, commit** (`feat(ios): rendu texte ESC/POS et commandes GS v 0 identiques au .NET (4a)`).

---

### Tâche 4 : Entrées, formats et textes localisés

**Files:**
- Create: `…/Printing/TicketInputs.swift`, `TicketFormat.swift`, `TicketTexts.swift` (généré)
- Create: `ios/scripts/gen_ticket_texts.py`
- Test: `…/Tests/PosKitTests/Printing/TicketFormatTests.swift`, `TicketTextsTests.swift`

**Interfaces:**
- Produces : `TicketFormat.amount(_ cents: Int) -> String`, `.utcDateTime(_ date: Date) -> String` (`dd/MM/yyyy HH:mm:ss`, UTC), `.localDateTime(_ date: Date, in zone: TimeZone) -> String` (`dd/MM/yyyy HH:mm`), `.localTime(_ date: Date, in zone: TimeZone) -> String` (`HH:mm`), `.rate(_ rate: Decimal) -> String` (« 0.## » invariant) ; `TicketTexts.supportedLanguages = ["en", "fr", "ar"]`, `TicketTexts.get(_ language: String, _ key: String, _ args: [String: String] = [:]) -> String` (repli : langue → anglais → clé) ; les entrées `Ticket*` (ci-dessous).

Entrées (`Codable`, `Equatable`, `Sendable`, membres camelCase identiques aux `input` de la fixture ; les `Date` décodées en ISO 8601 UTC avec millisecondes) :

```swift
struct TicketSettings: Codable, Equatable, Sendable {
    var companyName: String; var addressLines: String; var siret: String; var vatNumber: String; var certificateNumber: String?
}
struct TicketOrderItem: Codable, Equatable, Sendable {
    var quantity: Int; var productName: String; var productId: UUID?; var totalTtcCents: Int
    var selectedModifiers: [String]; var course: CourseType
}
struct TicketOrder: Codable, Equatable, Sendable {
    var destination: OrderDestination; var tableNumber: String; var pickupNumber: String?; var pickupBuzzer: String?; var items: [TicketOrderItem]
}
struct TicketReceipt: Codable, Equatable, Sendable {
    var receiptNumber: String; var createdAtUtc: Date; var terminalId: String
    var totalTtcCents: Int; var totalHtCents: Int; var taxBreakdownJson: String; var signatureHash: String
}
struct TicketKitchenItem: Codable, Equatable, Sendable {
    var quantity: Int; var productName: String; var productId: UUID?; var modifiersSummary: String?; var kitchenComment: String?
}
struct TicketKitchen: Codable, Equatable, Sendable {
    var serverName: String; var coversCount: Int; var dispatchedAtUtc: Date; var items: [TicketKitchenItem]
}
struct TicketRateAmount: Codable, Equatable, Sendable { var rate: Decimal; var cents: Int }          // décodé depuis {"rate":"5.5","cents":123}
struct TicketMethodAmount: Codable, Equatable, Sendable { var method: Int; var cents: Int }
struct TicketReportSummary: Codable, Equatable, Sendable {
    var terminalId: String; var periodStartUtc: Date?; var periodEndUtc: Date
    var totalSalesTtcCents: Int; var totalSalesHtCents: Int; var receiptCount: Int
    var vatBreakdownCents: [TicketRateAmount]; var paymentTotalsCents: [TicketMethodAmount]; var perpetualGrandTotalCents: Int
}
struct TicketDailyClosure: Codable, Equatable, Sendable {
    var terminalId: String; var closureSequence: Int; var totalSalesTtcCents: Int; var totalSalesHtCents: Int; var receiptCount: Int
    var vatBreakdownCents: [TicketRateAmount]; var paymentTotalsCents: [TicketMethodAmount]; var perpetualGrandTotalCents: Int
    var signatureHash: String; var closedAtUtc: Date; var periodStartUtc: Date?; var sealedByUserName: String
}
struct TicketPeriodClosure: Codable, Equatable, Sendable {
    var terminalId: String; var isMonthly: Bool; var periodKey: String; var closureSequence: Int
    var periodStartUtc: Date?; var periodEndUtc: Date; var totalTtcCents: Int; var totalHtCents: Int
    var taxesSummaryJson: String; var tenderTotalsJson: String; var perpetualGrandTotalCents: Int
    var dailyClosureCount: Int; var signatureHash: String; var sealedByUserName: String
}
struct ReportItemLine: Codable, Equatable, Sendable { var productName: String; var quantity: Int; var totalTtcCents: Int }
struct ReportCategoryGroup: Codable, Equatable, Sendable { var categoryName: String?; var items: [ReportItemLine]; var subtotalTtcCents: Int }
struct ReportTipLine: Codable, Equatable, Sendable { var serverName: String?; var tipCents: Int }
struct ReportPrintData: Codable, Equatable, Sendable {
    var categories: [ReportCategoryGroup]; var tips: [ReportTipLine]; var totalTipsCents: Int; var hasGlobalDiscount: Bool
    static let empty = ReportPrintData(categories: [], tips: [], totalTipsCents: 0, hasGlobalDiscount: false)
}
```

`TicketScenarioInput` (cible de décodage du `input` de la fixture, dans le **dossier de tests**) : tous les membres optionnels : `receipt`, `order`, `settings`, `duplicateNumber`, `pickupNumber`, `buzzer`, `nowUtc`, `kitchen`, `summary`, `closure`, `periodClosure`, `reportData`, `printer` (`Printer` des modèles PosKit : `name`, `ipAddress`, `port`, `paperWidthMm`), `document`. Le générateur .NET écrit exactement ces clés (tâche 1) ; si la forme réelle de la fixture diffère, adapter les structs Swift à la fixture.

Le script `gen_ticket_texts.py` lit `SharedResource.resx`, `.fr.resx`, `.ar.resx` (`<data name=… ><value>…</value>`), garde les clés des quatre préfixes, échappe pour Swift et écrit `TicketTexts.swift` (`private static let en: [String: String]`, `fr`, `ar`, en-tête « Fichier généré par ios/scripts/gen_ticket_texts.py — ne pas modifier »). `get` remplace chaque `{nom}` par la valeur, comme `Texts.Get`.

- [ ] **Étape 1 : tests**

```swift
@Suite struct TicketFormatTests {
    @Test func amountMatchesInvariantTwoDecimals() {
        #expect(TicketFormat.amount(0) == "0.00"); #expect(TicketFormat.amount(5) == "0.05")
        #expect(TicketFormat.amount(1250) == "12.50"); #expect(TicketFormat.amount(-50) == "-0.50"); #expect(TicketFormat.amount(-123456) == "-1234.56")
    }
    @Test func rateTrimsLikeZeroPoundPound() {
        #expect(TicketFormat.rate(5.5) == "5.5"); #expect(TicketFormat.rate(10) == "10"); #expect(TicketFormat.rate(2.1) == "2.1")
        #expect(TicketFormat.rate(0.125) == "0.13"); #expect(TicketFormat.rate(0) == "0")
    }
    @Test func datesUseGivenZoneAndLatinDigits() {
        let paris = TimeZone(identifier: "Europe/Paris")!
        let date = ISO8601DateFormatter().date(from: "2025-07-15T10:30:45Z")!
        #expect(TicketFormat.utcDateTime(date) == "15/07/2025 10:30:45")
        #expect(TicketFormat.localDateTime(date, in: paris) == "15/07/2025 12:30")
        #expect(TicketFormat.localTime(date, in: paris) == "12:30")
    }
}

@Suite struct TicketTextsTests {
    @Test func everyKeyMatchesDotNetInEveryLanguage() throws {
        let f = try TicketReferenceFixture.load()
        for (lang, table) in f.texts { for (key, value) in table { #expect(TicketTexts.get(lang, key) == value, "\(lang) \(key)") } }
    }
    @Test func substitutesNamedArguments() {
        #expect(TicketTexts.get("en", "report.z_title", ["sequence": "7"]) == "Z CLOSURE #7")
    }
    @Test func missingKeyReturnsTheKey() { #expect(TicketTexts.get("fr", "nope.key") == "nope.key") }
}
```

(0.125 → « 0.13 » : arrondi du milieu loin de zéro du .NET ; si la fixture contredit, la fixture gagne.)

- [ ] **Étape 2 : échec attendu ; Étape 3 : implémenter, lancer le script (`python3 -I ios/scripts/gen_ticket_texts.py`), PASS ; Étape 4 : suite complète + commit** (`feat(ios): entrées de ticket, formats et textes localisés (4a)`). Inclure le script et le fichier généré dans le commit.

---

### Tâche 5 : `TicketBuilder` (port de `TicketDocumentBuilder`)

**Files:**
- Create: `…/Printing/TicketBuilder.swift`
- Test: `…/Tests/PosKitTests/Printing/TicketBuilderReferenceTests.swift`, `TicketBuilderEdgeTests.swift`

**Interfaces:**
- Consumes : tâches 2 à 4, `Printer`.
- Produces :

```swift
struct TicketContext: Equatable, Sendable {
    var timeZone: TimeZone       // heure locale du restaurant (le .NET : ToLocalTime())
    var softwareVersion: String  // « Logiciel: RestaurantPOS v… »
}

enum TicketBuilder {
    static func pickupCoupon(order: TicketOrder, pickupNumber: String, buzzer: String?, language: String, now: Date) -> TicketDocument
    static func receipt(_ receipt: TicketReceipt, order: TicketOrder, language: String, settings: TicketSettings?, duplicateNumber: Int?, context: TicketContext) -> TicketDocument
    static func receiptFallback(_ receipt: TicketReceipt, language: String, settings: TicketSettings?, duplicateNumber: Int?, context: TicketContext) -> TicketDocument
    static func fiscalReceipt(_ receipt: TicketReceipt, order: TicketOrder, pickupNumber: String, buzzer: String?, language: String, settings: TicketSettings?, duplicateNumber: Int?, context: TicketContext) -> TicketDocument
    static func xReport(_ summary: TicketReportSummary, data: ReportPrintData, language: String, now: Date, settings: TicketSettings?, context: TicketContext) -> TicketDocument
    static func zClosure(_ closure: TicketDailyClosure, data: ReportPrintData, language: String, settings: TicketSettings?, duplicateNumber: Int?, context: TicketContext) -> TicketDocument
    static func periodClosure(_ closure: TicketPeriodClosure, data: ReportPrintData, language: String, settings: TicketSettings?, context: TicketContext) -> TicketDocument
    static func kitchenTicket(_ ticket: TicketKitchen, order: TicketOrder, language: String, context: TicketContext) -> TicketDocument
    static func testPage(printer: Printer, language: String, now: Date) -> TicketDocument
    static func withDuplicateNotice(_ doc: TicketDocument, duplicateNumber: Int) -> TicketDocument
}
```

Règles (port ligne à ligne de `TicketDocumentBuilder.cs`, à relire en entier ; ce qui suit liste les pièges) :

- `resolve(language)` : `TicketTexts.supportedLanguages` contient la langue sinon `"en"` ; `rightToLeft = (lang == "ar")`.
- En-tête (`appendHeader`) : valeurs par défaut `"RESTAURANT L'ANTIGRAVITE"`, `"12 Rue de la Gastronomie\n75001 Paris"`, `"88877766600012"`, `"FR12888777666"` quand la valeur est nulle, vide ou faite d'espaces ; l'adresse est découpée sur `\n`, chaque entrée rognée (espaces et tabulations), les entrées vides supprimées ; `SIRET: …`, `TVA: …`, `Logiciel: RestaurantPOS v<context.softwareVersion>`, `Certificat: …` seulement si non vide.
- Mention « DUPLICATA n°N » : texte exact `DUPLICATA n°\(n)` (le signe `°` est U+00B0), centre, gras, large, suivi d'un séparateur.
- Heures : `receipt.date` du ticket, du coupon et du bon de retrait = `TicketFormat.utcDateTime` (UTC) ; rapports X / Z / période et heure du bon cuisine = locales (`context.timeZone`) ; un début de période `nil` s'écrit `-`.
- Taux de TVA des rapports : tri croissant par taux, libellé `"\(TicketFormat.rate(rate)) %"`. Règlements : tri croissant par valeur d'énumération ; libellé par `PaymentMethod` (`admin.payment_method_cash`, `_credit_card`, `_meal_voucher`, `_gift_card`, `_room_charge`, sinon `_other`).
- Clôture de période : titre `CLOTURE MENSUELLE <clé> - n°<séquence>` ou `CLOTURE ANNUELLE …` ; `taxesSummaryJson` décodé comme dictionnaire `String → Int` (clés converties en `Decimal` invariant, clé non numérique ignorée) ; `tenderTotalsJson` : clé = nom d'énumération sensible à la casse (`Cash`, `CreditCard`, `MealVoucher`, `GiftCard`, `RoomCharge`) **ou** nombre entier en chaîne (`"1"`) ; autre clé ignorée ; JSON invalide ou vide = dictionnaire vide, sans plantage ; `receiptCount` du bloc de totaux = `dailyClosureCount`.
- Détails de rapport : section « articles vendus » seulement si `categories` non vide (mention « avant remises » si `hasGlobalDiscount`), nom de catégorie `nil` → `report.other_category` ; section pourboires seulement si `totalTipsCents > 0`, serveur `nil` → `report.unknown_server`.
- Bon cuisine : `OrderDestination.takeaway` → `kitchen_ticket.takeaway`, sinon `kitchen_ticket.table` avec la table ; ligne « retrait » si numéro non vide, « buzzer » si non vide ; service d'une ligne = premier article de la commande de même `productId`, sinon `.direct` ; textes de service : `course_direct`, `course_suite`, `course_dessert`, `course_on_demand`.
- Mode : `receipt.mode_takeaway` si `.takeaway`, sinon `receipt.mode_eat_in`.
- `withDuplicateNotice` : retire toute ligne `.text` dont le texte contient « DUPLICATA » (insensible à la casse), insère la mention en tête, conserve langue et sens.

- [ ] **Étape 1 : tests**

```swift
@Suite struct TicketBuilderReferenceTests {
    @Test func everyScenarioMatchesTheDotNetDocument() throws {
        let f = try TicketReferenceFixture.load()
        let context = TicketContext(timeZone: TimeZone(identifier: f.timeZone)!, softwareVersion: f.softwareVersion)
        #expect(f.documents.count >= 30)
        for entry in f.documents {
            let input = try TicketScenarioInput.decode(entry.inputData)
            let expected = try JSONDecoder().decode(TicketDocument.self, from: entry.expectedData)
            #expect(try TicketScenarioInput.build(entry.kind, entry.language, input, context) == expected, entry.name)
        }
    }
}
```

`TicketScenarioInput.build` (dans les tests) aiguille sur `kind` vers le constructeur correspondant ; le décodage des dates est ISO 8601 avec millisecondes (stratégie personnalisée), `rate` des `TicketRateAmount` est une chaîne décimale (`Decimal(string:locale:)` avec `en_US_POSIX`).

`TicketBuilderEdgeTests` : langue inconnue → `language == "en"` ; `rightToLeft` vrai seulement pour `ar` ; `withDuplicateNotice` n'ajoute qu'une mention ; JSON de taxes invalide → document produit ; deux mois d'été et d'hiver donnent +2 h et +1 h d'écart avec l'UTC dans les heures locales.

- [ ] **Étape 2 : échec attendu ; Étape 3 : implémenter ; Étape 4 : PASS ; Étape 5 : suite PosKit complète + commit** (`feat(ios): TicketBuilder, port de TicketDocumentBuilder, identique à la fixture .NET (4a)`).

Si un scénario diverge, comparer ligne par ligne (le test affiche le nom du scénario ; ajouter un message qui liste les index de lignes différents) et corriger le Swift — jamais la fixture.

---

### Tâche 6 : Rendu raster CoreText

**Files:**
- Create: `…/Printing/TicketRasterRenderer.swift`
- Create: `…/Resources/Fonts/NotoSans-Regular.ttf`, `NotoSans-Bold.ttf`, `NotoSansArabic-Regular.ttf`, `NotoSansArabic-Bold.ttf` (copiés de `src/RestaurantPos.Infrastructure/Printing/Fonts/`, plus `OFL.txt`)
- Test: `…/Tests/PosKitTests/Printing/TicketRasterRendererTests.swift`

**Interfaces:**
- Consumes : `TicketDocument`, `MonoImage`, `EscPosCommands.build`.
- Produces : `TicketRasterRenderer.dots(forPaperWidthMm:) -> Int` (384 pour 58 mm, sinon 576), `TicketRasterRenderer.render(_ doc: TicketDocument, widthDots: Int) -> [MonoImage]`, et `TicketPayload.build(printer: Printer, document: TicketDocument, openCashDrawer: Bool) -> [UInt8]` (texte si `printer.textMode` et document LTR, sinon image ; miroir de `EscPosPrinterTransport.BuildPayload`).

Mise en page (constantes du .NET) : marge 8, police normale 24, grande 40 (gras), gras 700, largeur de contenu `width - 16`. Un `separator(cut: true)` ouvre un nouveau segment (image) ; segments vides ignorés. Séparateur simple : bloc de 16 points de haut, ligne pointillée (trait 2, motif 6 pleins / 4 vides) à `y + 8`. `text` : bloc dont la hauteur est celle du texte mis en forme, aligné selon `align` (début = gauche en LTR et droite en RTL ; fin = l'inverse ; centre). `columns` : valeur alignée à droite (à gauche en RTL) sur toute la largeur, étiquette dans `max(content / 3, content - largeurValeur - 16)` à gauche (à droite en RTL, décalée de `content - largeurÉtiquette`), hauteur = max des deux. Pas de découpe de mots dans le .NET autre que le retour à la ligne de la mise en forme : utiliser le retour à la ligne de CoreText. Texte arabe : découpage en passages par écriture comme `ScriptRuns` (police `Noto Sans Arabic` pour les caractères arabes, `Noto Sans` sinon ; les espaces suivent le passage courant ; intervalles arabes 0600–06FF, 0750–077F, 08A0–08FF, FB50–FDFF, FE70–FEFF) ; direction de base de paragraphe RTL quand `doc.rightToLeft`. Seuil : luminance `< 128` → bit noir (MSB à gauche), largeur multiple de 8.

Implémentation : polices lues depuis `Bundle.module` (`url(forResource:withExtension: "ttf")`), `CTFontManagerCreateFontDescriptorFromData` + `CTFontCreateWithFontDescriptor` (aucun enregistrement global) ; contexte `CGContext` gris 8 bits, fond blanc, lissage actif, axes retournés pour que `y` croisse vers le bas ; mise en page par `CTFramesetter` (`NSAttributedString` avec `.font`, `.paragraphStyle` : alignement et `baseWritingDirection`) ; hauteur par `CTFramesetterSuggestFrameSizeWithConstraints` ; dessin par `CTFrameDraw`. Adapter les détails de l'API à la compilation ; le contrat est celui des tests.

- [ ] **Étape 1 : tests structurels**

```swift
@Suite struct TicketRasterRendererTests {
    private func doc(_ lines: [TicketLine], rtl: Bool = false) -> TicketDocument { TicketDocument(language: rtl ? "ar" : "fr", rightToLeft: rtl, lines: lines) }
    private func ink(_ image: MonoImage, columns: Range<Int>) -> Int {
        (0..<image.height).reduce(0) { total, y in
            total + columns.reduce(0) { $0 + (((image.bits[y * image.bytesPerRow + $1 / 8] >> (7 - UInt8($1 % 8))) & 1) == 1 ? 1 : 0) }
        }
    }

    @Test func widthsFollowPaper() {
        #expect(TicketRasterRenderer.dots(forPaperWidthMm: 58) == 384)
        #expect(TicketRasterRenderer.dots(forPaperWidthMm: 80) == 576)
    }

    @Test func textProducesInkAndAWholeNumberOfBytesPerRow() {
        let image = TicketRasterRenderer.render(doc([.text("Bonjour — 12.50 €", align: .start)]), widthDots: 576)[0]
        #expect(image.width == 576); #expect(image.bits.count == image.bytesPerRow * image.height)
        #expect(ink(image, columns: 0..<576) > 100)
    }

    @Test func separatorIsSixteenDotsPlusMargins() {
        let image = TicketRasterRenderer.render(doc([.separator()]), widthDots: 384)[0]
        #expect(image.height == 16 + 16)
        #expect(ink(image, columns: 0..<384) > 0)
    }

    @Test func cutSeparatorSplitsSegmentsAndDropsEmptyOnes() {
        let images = TicketRasterRenderer.render(doc([.text("A"), .separator(cut: true), .text("B"), .separator(cut: true)]), widthDots: 384)
        #expect(images.count == 2)
    }

    @Test func rightAlignedTextLeansRightAndRtlStartLeansRight() {
        let end = TicketRasterRenderer.render(doc([.text("Total", align: .end)]), widthDots: 576)[0]
        #expect(ink(end, columns: 288..<576) > ink(end, columns: 0..<288))
        let arabicStart = TicketRasterRenderer.render(doc([.text("مرحبا بكم", align: .start)], rtl: true), widthDots: 576)[0]
        #expect(ink(arabicStart, columns: 288..<576) > ink(arabicStart, columns: 0..<288))
    }

    @Test func columnsPutValueAtTheOppositeEdgeInRtl() {
        let ltr = TicketRasterRenderer.render(doc([.columns(label: "Total", value: "12.50")]), widthDots: 576)[0]
        #expect(ink(ltr, columns: 400..<576) > 0); #expect(ink(ltr, columns: 0..<100) > 0)
        let rtl = TicketRasterRenderer.render(doc([.columns(label: "المجموع", value: "12.50")], rtl: true), widthDots: 576)[0]
        #expect(ink(rtl, columns: 0..<176) > 0); #expect(ink(rtl, columns: 400..<576) > 0)
    }

    @Test func renderingIsDeterministic() {
        let d = doc([.text("Déterministe", bold: true, large: true), .columns(label: "a", value: "b")])
        #expect(TicketRasterRenderer.render(d, widthDots: 576) == TicketRasterRenderer.render(d, widthDots: 576))
    }

    @Test func payloadUsesTextOnlyForLtrTextModePrinters() {
        let ltr = doc([.text("Hello")]); let arabic = doc([.text("مرحبا")], rtl: true)
        var printer = Printer(name: "P", ipAddress: "10.0.0.1", textMode: true)
        #expect(TicketPayload.build(printer: printer, document: ltr, openCashDrawer: false).starts(with: [0x1B, 0x40, 0x1B, 0x74, 19]))
        #expect(TicketPayload.build(printer: printer, document: arabic, openCashDrawer: false).contains(0x30)) // GS v 0
        printer.textMode = false
        #expect(TicketPayload.build(printer: printer, document: ltr, openCashDrawer: false).starts(with: [0x1B, 0x40, 0x1D, 0x76, 0x30]))
    }

    @Test func bundledFontsLoad() {
        #expect(TicketRasterRenderer.fontsAvailable)
    }
}
```

`TicketRasterRenderer.fontsAvailable` : vrai si les quatre polices se chargent (utilisé aussi par l'écran de test du lot 4d).

- [ ] **Étape 2 : échec attendu ; Étape 3 : implémenter + ajouter les polices ; vérifier que `Package.swift` (`resources: [.process("Resources")]`) embarque les `.ttf` (sinon `.copy("Resources/Fonts")`) ; Étape 4 : PASS** `swift test --filter TicketRasterRendererTests`.
- [ ] **Étape 5 : contrôle visuel manuel (non automatisé)** — écrire un petit test jetable (non commité) qui sauve en PNG le rendu d'un ticket FR et d'un ticket AR du jeu de scénarios, et le regarder (aspect, arabe lié, sens). Consigner dans le fichier de suivi ce qui a été vu.
- [ ] **Étape 6 : suite PosKit complète + commit** (`feat(ios): rendu raster CoreText, polices Noto et charge utile texte ou image (4a)`).

---

### Tâche 7 : Documentation et vérification finale

**Files:**
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift » : ajouter le paragraphe « Impression 4a »)
- Modify: `docs/plans/ipad-standalone-4a-progress.md`, `docs/plans/ipad-standalone-3-progress.md` (lien vers 4)

- [ ] **Étape 1 : `CLAUDE.md`** — ajouter, dans le paragraphe de parité, une phrase par point : `ticket_document_reference.json` est **généré par le .NET** (`TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`) et rejoué par `TicketBuilderReferenceTests`, `EscPosTextRendererTests`, `EscPosCommandsTests`, `Pc858Tests`, `TicketTextsTests` ; les textes de ticket viennent de `TicketTexts` (généré par `ios/scripts/gen_ticket_texts.py` depuis les `.resx`, à relancer quand un `.resx` change) ; le raster CoreText n'est pas comparable au .NET (Skia/HarfBuzz) : tests structurels et essai papier ; les longueurs du rendu texte sont en UTF-16 ; limite connue : un mot trop long coupé au milieu d'une paire de substituts donne deux `?` comme le .NET, non couvert par la fixture ; le calcul des `ReportPrintData` (tri français des noms) arrive avec le lot 4b ; toute modification de `TicketDocumentBuilder`, `EscPosTextRenderer`, `EscPosCommands` ou des `.resx` côté .NET impose de régénérer la fixture et les textes.
- [ ] **Étape 2 : vérifications** — `dotnet test RestaurantPos.slnx` ; `cd ios/Packages/PosKit && swift test` ; `cd ios && xcodegen generate && ./scripts/test.sh unit`. Noter les compteurs dans le fichier de suivi.
- [ ] **Étape 3 : commit et push** (`docs: lot 4a fait (document et rendu des tickets)`), puis relire le diff de la branche entière pour la revue finale.
