# iPad standalone — plan 1b : commandes de salle, cuisine, remises, transferts (`LocalPosAPI`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `LocalPosAPI` prend en charge, sur SQLite, tout le cycle d'une commande de salle : ouverture de table, saisie des lignes, envoi en cuisine (bons par poste, réclame suite, avancement des bons), remises et gratuités avec traçabilité, transfert et fusion de tables.

**Architecture:** On ajoute la migration v2 (tables `Orders`, `OrderItems`, `KitchenTickets`, `KitchenTicketItems`, `TableTransferLogs`, `OrderDiscountAudits`, mêmes noms et colonnes que `AppDbContext`) et trois dépôts (`LocalOrderRepository`, `LocalKitchenRepository`, extension de `LocalFloorRepository`). Les méthodes de `PosAPI` sont ajoutées par extensions de l'acteur existant, chacune dans une transaction. Les totaux réutilisent `OrderMath` (déjà porté depuis le .NET), pas de nouveau calcul monétaire.

**Tech Stack:** Swift 6, `SQLite3`, Swift Testing, SwiftPM (`ios/Packages/PosKit`). Aucune dépendance nouvelle.

**Spec:** `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md` (sous-projet 1 « Socle local »). Suite du plan 1a (`docs/superpowers/plans/2026-10-06-ipad-standalone-1a-socle-sqlite.md`, fusionné). Le sous-projet 1 est désormais découpé en quatre plans :
- **1a** (fait) : SQLite, schéma v1, auth, personnel, catalogue, grille, tables, réglages.
- **1b (ce plan)** : commandes de salle, cuisine, remises et gratuités, transfert/fusion.
- **1c** : paiement non fiscal, comptoir et vente à emporter, mise en attente, avoirs titres-restaurant, chambres d'hôtel.
- **1d** : imprimantes (configuration), Happy Hour, réseau, choix « Serveur / Autonome » au lancement, identité de terminal `T01`.

## Global Constraints

- `PosKit` reste **sans dépendance externe** : uniquement `Foundation`, `SQLite3`, `CryptoKit`.
- Swift 6 (`swift-tools-version: 6.0`), plateformes `.iOS(.v17)` et `.macOS(.v14)` ; aucun avertissement de compilation.
- Montants en **centimes entiers** (`Money`), jamais de `Double`/`Float` ; taux et pourcentages en `Decimal`, stockés en texte ; les calculs de totaux passent par `OrderMath` (même arrondi « away from zero » que le .NET).
- Tables et colonnes nommées comme dans `AppDbContext` ; Guid = `TEXT` en majuscules ; `DateTimeOffset` = `TEXT` ISO 8601 UTC ; enum = `INTEGER` ; `Money` = `INTEGER`.
- Migrations versionnées par `PRAGMA user_version`, **échec explicite**, un script publié n'est jamais modifié (on ajoute le script v2).
- Messages d'erreur et textes identiques au serveur (français, `SharedResource.fr.resx`) et à `InMemoryPosAPI`.
- Une opération qui touche plusieurs tables s'exécute dans **une seule** `db.transaction` (non ré-entrante : aucune transaction imbriquée).
- Éviter les noms de types qui entrent en collision avec SwiftUI/Foundation ; types du plan préfixés `Local…`.
- Commentaires et chaînes en français ; identifiants de code en anglais ; tests Swift Testing.
- Ne pas toucher à `HTTPPosAPI`, `InMemoryPosAPI`, aux stores ni aux vues.

## Décisions de conception (écarts assumés vis-à-vis du serveur .NET)

La spec demande la parité avec le .NET ; sur ces points précis, le .NET peut détruire une commande et l'iPad est seul dépositaire des données (pas de serveur de secours). Chaque écart est testé.

1. **Rouvrir une table occupée** garde sa commande (met à jour couverts et serveur). Le .NET crée une nouvelle commande et orpheline l'ancienne.
2. **Transfert** vers une table occupée, ou vers la même table, est refusé (`Échec du transfert de table.`). Le .NET écrase `ActiveOrderId` et perd la commande. La **fusion** (`merge: true`) reste le moyen de combiner deux commandes.
3. **Quantité ≤ 0** refusée (`400`, `La quantité doit être positive.`), sans équivalent serveur : une ligne négative minorerait le total.
4. **Réclame suite** sans ligne « suite » ne crée pas de bon vide (le .NET en crée un sans article).
5. `removeDiscount` sur une commande inconnue répond `404` (le .NET répond `500` non géré).
6. Non portés : la normalisation `T01 ↔ T1` des numéros de table (héritage web) et le routage SignalR/impression (plans 1d et sous-projet 4).
7. `kitchenTickets()` suit le serveur : les 50 derniers bons, **tous statuts**, du plus récent au plus ancien (`InMemoryPosAPI` masque les bons servis).

## Review Focus

Entrées ou pannes que la spec implique mais qu'aucun test nominal n'exerce ; chacune a son test dans la tâche indiquée.

1. **Transfert vers une table occupée ou vers soi-même** ne doit détruire aucune commande → `transferOntoOccupiedTableIsRefusedAndKeepsBothOrders`, `transferToSameTableIsRefused` (tâche 4).
2. **Quantité nulle ou négative** → `nonPositiveQuantityIsRejected` (tâche 2).
3. **Ajout après envoi en cuisine** : la nouvelle ligne ne fusionne pas avec une ligne déjà envoyée, et un second envoi n'émet que les nouveautés (pas de bons en double) → `dispatchAfterNewLineOnlySendsNewLines` (tâche 3).
4. **Remise hors bornes** (> 100 %, négative, motif vide, commande inconnue) ne modifie rien ; une remise fixe supérieure au total plafonne à 0 → `discountValidationLeavesTheOrderUntouched`, `fixedDiscountClampsAtZero` (tâche 5).
5. **Options contenant des virgules** (`Sans oignon, sans sel`) doivent revenir identiques (pas de découpage sur la virgule) → `modifiersWithCommasRoundTrip` (tâche 2).

## File Structure

Tout est sous `ios/Packages/PosKit/`.

| Fichier | Responsabilité |
|---|---|
| `Sources/PosKit/Local/LocalSchemaV2.swift` | Script de la migration v2 (tables commandes, cuisine, journaux). |
| `Sources/PosKit/Local/LocalMigrator.swift` | (modifié) ajoute `schemaV2` à `steps`. |
| `Sources/PosKit/Local/LocalOrderRepository.swift` | `Orders`/`OrderItems` ↔ `ActiveOrder`/`OrderLine`, journaux de transfert et d'audit de remise. |
| `Sources/PosKit/Local/LocalKitchenRepository.swift` | `LocalStations` (résolution du poste), `KitchenTickets`/`KitchenTicketItems` ↔ `KitchenTicket`. |
| `Sources/PosKit/Local/LocalFloorRepository+Orders.swift` | Lecture et mise à jour de l'état d'une table (statut, couverts, serveur, commande). |
| `Sources/PosKit/Local/LocalCatalogRepository.swift` | (modifié) `stationFallbacks()` pour le routage cuisine. |
| `Sources/PosKit/Local/LocalPosAPI.swift` | (modifié) accesseurs `orderRepository`, `kitchenRepository`. |
| `Sources/PosKit/Local/LocalPosAPI+Orders.swift` | `openTable`, `activeOrder`, `addItems`, `setDestination` + aide de décoration. |
| `Sources/PosKit/Local/LocalPosAPI+Floor.swift` | (modifié) `tables()` renseigne le total de chaque commande active. |
| `Sources/PosKit/Local/LocalPosAPI+Kitchen.swift` | `dispatch`, `fireSuite`, `kitchenTickets`, `bumpTicket`. |
| `Sources/PosKit/Local/LocalPosAPI+Transfer.swift` | `transfer` (transfert et fusion). |
| `Sources/PosKit/Local/LocalPosAPI+Discounts.swift` | `applyDiscount`, `removeDiscount`, `compItem`. |
| `Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` | (modifié) retire les stubs portés ; renumérote les plans suivants. |
| `Tests/PosKitTests/Local/*.swift` | Un fichier de tests par tâche. |
| `docs/plans/ipad-standalone-1b-progress.md` | Suivi d'exécution (exigé par `CLAUDE.md`). |

## Conventions pour toutes les tâches

- Les commandes `swift` se lancent depuis `ios/Packages/PosKit`. **Le filtre `swift test --filter` porte sur les noms de types Swift, pas sur les noms d'affichage des suites** : utiliser par exemple `--filter LocalOrderTests`. La suite complète (`swift test`) fait foi.
- Base de référence avant ce plan : **203 tests PosKit passent**. Totaux attendus cumulés à la fin de chaque tâche : T1 208 · T2 219 · T3 227 · T4 233 · T5 240.
- Chaque tâche se termine par : suite PosKit complète verte, mise à jour de `docs/plans/ipad-standalone-1b-progress.md`, commit. Le fichier de suivi est commité avec la tâche.
- Commits au style du dépôt : `feat(ios-local): …`, en français, avec le trailer de co-signature de la session.
- `swift test` refuse tout avertissement de compilation dans le code ajouté.

---

### Task 1: Migration v2 et dépôts (commandes, cuisine, état des tables)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV2.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalOrderRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalKitchenRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFloorRepository+Orders.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalCatalogRepository.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderStoreTests.swift`
- Create: `docs/plans/ipad-standalone-1b-progress.md`

**Interfaces:**
- Consumes (plan 1a) : `SQLiteDatabase` (`exec`, `run`, `query`, `transaction`, `userVersion`), `SQLRow`, `SQLValue`, `LocalMigrator.steps`, `LocalFloorRepository` (`tables()`, `insert(_:)`), `LocalCatalogRepository`, `LocalPosAPI` (`db`, accesseurs de dépôts) ; modèles PosKit `ActiveOrder`, `OrderLine`, `KitchenTicket`, `KitchenTicketItem`, `DiningTable`, `TableStatus`, `TicketStatus`, `DiscountType`, `OrderDestination`, `CourseType`, `Money`.
- Produces :
  - `let localNoOperator: UUID` (identifiant « opérateur inconnu », `Guid.Empty` du .NET).
  - `enum LocalOrderStatus: Int { open = 0, sentToKitchen = 1, billRequested = 2, paid = 3, cancelled = 4 }`.
  - `struct LocalOrderRepository { let db; order(id:) -> ActiveOrder?; lines(orderId:) -> [OrderLine]; insert(_ o: ActiveOrder, operatorId: UUID, status: LocalOrderStatus); insert(_ l: OrderLine, orderId: UUID); setQuantity(lineId: UUID, quantity: Int); markDispatched(orderId: UUID); setStatus(orderId: UUID, _ status: LocalOrderStatus); setDestination(orderId: UUID, _ d: OrderDestination) -> Bool; setTableNumber(orderId: UUID, _ number: String); setDiscount(orderId: UUID, type: DiscountType?, value: Decimal, reason: String?) -> Bool; comp(lineId: UUID, orderId: UUID, reason: String) -> Bool; moveLines(from: UUID, to: UUID); insertTransferLog(source: String, target: String, orderId: UUID, operatorName: String, isMerge: Bool); insertDiscountAudit(orderId: UUID, itemId: UUID?, type: DiscountType, value: Decimal, saved: Money, reason: String, operatorId: UUID) }`.
  - `enum LocalStations { static let hotKitchen; normalize(_:) -> String?; resolve(item:product:category:) -> String }`.
  - `struct LocalKitchenRepository { let db; insert(_ t: KitchenTicket, productIds: [UUID]); recentTickets(limit: Int) -> [KitchenTicket]; bump(id: UUID) -> Bool }`.
  - `extension LocalFloorRepository { table(_:) -> DiningTable?; waiterId(of:) -> UUID?; update(_:status:covers:waiterName:waiterId:activeOrderId:openedAt:) }`.
  - `LocalCatalogRepository.stationFallbacks() -> [UUID: (product: String?, category: String?)]`.
  - `LocalPosAPI.orderRepository`, `LocalPosAPI.kitchenRepository`.

- [ ] **Step 0: Relever la base de référence et créer le fichier de suivi**

Run (depuis la racine du dépôt) :
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run"
```
Expected: `Test run with 203 tests … passed`.

Créer `docs/plans/ipad-standalone-1b-progress.md` :
```markdown
# iPad standalone 1b — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1b-commandes-cuisine.md`

Base de référence (avant tâche 1) : PosKit 203 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v2 et dépôts | à faire | | |
| 2 Commandes de salle | à faire | | |
| 3 Envoi cuisine et bons | à faire | | |
| 4 Transfert et fusion | à faire | | |
| 5 Remises et gratuités | à faire | | |
```

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderStoreTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v2 et dépôts commandes / cuisine")
struct LocalOrderStoreTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    @Test func migrationV2CreatesOrderTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 2)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["Orders", "OrderItems", "KitchenTickets", "KitchenTicketItems", "TableTransferLogs", "OrderDiscountAudits"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func deletingAnOrderCascadesToItsLines() throws {
        let db = try makeDB()
        let repo = LocalOrderRepository(db: db)
        let order = ActiveOrder(tableNumber: "T1", destination: .eatIn)
        try repo.insert(order, operatorId: localNoOperator, status: .open)
        try repo.insert(OrderLine(productId: UUID(), productName: "Café", quantity: 1, unitPrice: Money(cents: 300), taxRatePercent: 10), orderId: order.orderId)
        #expect(try repo.lines(orderId: order.orderId).count == 1)
        try db.run("DELETE FROM Orders WHERE Id = ?", [.uuid(order.orderId)])
        #expect(try repo.lines(orderId: order.orderId).isEmpty)
    }

    @Test func orderRepositoryRoundTripsAnOrderAndItsLines() throws {
        let db = try makeDB()
        let repo = LocalOrderRepository(db: db)
        let scheduleId = UUID()
        let line = OrderLine(
            productId: UUID(), productName: "Café ☕", quantity: 3, unitPrice: Money(cents: 450), taxRatePercent: 5.5,
            preparationStationId: "BAR", modifiersSummary: ["Sans sucre, svp", "Lait d'avoine"], course: .dessert,
            discountPercent: 12.5, modifiersPriceExtra: Money(cents: 30), taxRateTakeawayPercent: 5.5,
            isHappyHourApplied: true, originalUnitPrice: Money(cents: 500), appliedHappyHourScheduleId: scheduleId
        )
        let order = ActiveOrder(tableNumber: "T9", globalDiscountType: .fixedAmount, globalDiscountValue: 5, globalDiscountReason: "Geste", destination: .takeaway, pickupNumber: "#A-01", pickupBuzzer: "12")
        try repo.insert(order, operatorId: localNoOperator, status: .open)
        try repo.insert(line, orderId: order.orderId)

        let fetched = try #require(try repo.order(id: order.orderId))
        #expect(fetched.tableNumber == "T9" && fetched.destination == .takeaway)
        #expect(fetched.globalDiscountType == .fixedAmount && fetched.globalDiscountValue == 5 && fetched.globalDiscountReason == "Geste")
        #expect(fetched.pickupNumber == "#A-01" && fetched.pickupBuzzer == "12")
        #expect(fetched.lines == [line])

        try repo.setQuantity(lineId: line.lineId, quantity: 7)
        try repo.markDispatched(orderId: order.orderId)
        let updated = try #require(try repo.lines(orderId: order.orderId).first)
        #expect(updated.quantity == 7 && updated.isDispatched)
        #expect(try repo.order(id: UUID()) == nil)
    }

    @Test func kitchenRepositoryRoundTripsATicketAndBumpsIt() throws {
        let db = try makeDB()
        let repo = LocalKitchenRepository(db: db)
        let orderId = UUID()
        let productIds = [UUID(), UUID()]
        let ticket = KitchenTicket(
            orderId: orderId, tableNumber: "T3", serverName: "Sophie", coversCount: 4, stationId: "BAR",
            items: [KitchenTicketItem(productName: "Café", quantity: 2, modifiersSummary: "Sans sucre"), KitchenTicketItem(productName: "Thé", quantity: 1)]
        )
        try repo.insert(ticket, productIds: productIds)
        let fetched = try #require(try repo.recentTickets(limit: 50).first)
        #expect(fetched.id == ticket.id && fetched.orderId == orderId && fetched.tableNumber == "T3")
        #expect(fetched.serverName == "Sophie" && fetched.coversCount == 4 && fetched.stationId == "BAR" && fetched.status == .pending)
        #expect(fetched.items.map(\.productName) == ["Café", "Thé"] && fetched.items.map(\.quantity) == [2, 1])
        #expect(fetched.items[0].modifiersSummary == "Sans sucre" && fetched.items[1].modifiersSummary == nil)

        #expect(try repo.bump(id: ticket.id))
        #expect(try repo.recentTickets(limit: 50).first?.status == .inPreparation)
        #expect(try repo.bump(id: UUID()) == false)
        #expect(throws: SQLiteError.self) { try repo.insert(ticket, productIds: [UUID()]) }
    }

    @Test func floorRepositoryUpdatesTableState() throws {
        let db = try makeDB()
        let floor = LocalFloorRepository(db: db)
        try floor.insert(DiningTable(tableNumber: "T1", capacity: 4))
        let waiter = UUID(), order = UUID(), opened = Date()
        try floor.update("T1", status: .occupied, covers: 3, waiterName: "Sophie", waiterId: waiter, activeOrderId: order, openedAt: opened)
        let table = try #require(try floor.table("T1"))
        #expect(table.status == .occupied && table.coversCount == 3 && table.assignedWaiterName == "Sophie")
        #expect(table.activeOrderId == order && table.capacity == 4)
        #expect(table.openedAtUtc.map { abs($0.timeIntervalSince(opened)) < 0.002 } == true)
        #expect(try floor.waiterId(of: "T1") == waiter)
        try floor.update("T1", status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
        #expect(try floor.table("T1")?.activeOrderId == nil)
        #expect(try floor.waiterId(of: "T1") == nil)
        #expect(try floor.table("T404") == nil)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalOrderStoreTests 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalOrderRepository' in scope`.

- [ ] **Step 3: Écrire la migration v2**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV2.swift` :
```swift
import Foundation

extension LocalMigrator {
    /// Commandes de salle, cuisine, journaux de transfert et d'audit des remises. Mêmes tables et colonnes que `AppDbContext`.
    /// Les paiements, mises en attente, chambres et avoirs arrivent avec le plan 1c.
    static let schemaV2 = """
    CREATE TABLE Orders (
        Id TEXT NOT NULL PRIMARY KEY,
        TableNumber TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        Status INTEGER NOT NULL,
        Destination INTEGER NOT NULL,
        PickupNumber TEXT,
        PickupBuzzer TEXT,
        PickupScheduledAtUtc TEXT,
        CreatedAtUtc TEXT NOT NULL,
        GlobalDiscountType INTEGER,
        GlobalDiscountValue TEXT NOT NULL,
        GlobalDiscountReason TEXT,
        TipAmount INTEGER NOT NULL
    );

    CREATE TABLE OrderItems (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL REFERENCES Orders (Id) ON DELETE CASCADE,
        ProductId TEXT NOT NULL,
        ProductName TEXT NOT NULL,
        Quantity INTEGER NOT NULL,
        UnitPrice INTEGER NOT NULL,
        TaxRatePercent TEXT NOT NULL,
        TaxRateTakeawayPercent TEXT,
        IsFoodVoucherEligible INTEGER NOT NULL,
        PreparationStationId TEXT,
        IsDispatched INTEGER NOT NULL,
        SelectedModifiers TEXT NOT NULL,
        ModifiersPriceExtra INTEGER NOT NULL,
        KitchenComment TEXT,
        Course INTEGER NOT NULL,
        DiscountPercent TEXT NOT NULL,
        IsComp INTEGER NOT NULL,
        CompReason TEXT,
        IsHappyHourApplied INTEGER NOT NULL,
        OriginalUnitPrice INTEGER,
        AppliedHappyHourScheduleId TEXT,
        OrderedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_OrderItems_OrderId ON OrderItems (OrderId);

    CREATE TABLE KitchenTickets (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        TableNumber TEXT NOT NULL,
        ServerName TEXT NOT NULL,
        CoversCount INTEGER NOT NULL,
        StationId TEXT NOT NULL,
        Status INTEGER NOT NULL,
        DispatchedAtUtc TEXT NOT NULL,
        PreparedAtUtc TEXT,
        CompletedAtUtc TEXT
    );

    CREATE TABLE KitchenTicketItems (
        Id TEXT NOT NULL PRIMARY KEY,
        TicketId TEXT NOT NULL REFERENCES KitchenTickets (Id) ON DELETE CASCADE,
        ProductId TEXT NOT NULL,
        ProductName TEXT NOT NULL,
        Quantity INTEGER NOT NULL,
        ModifiersSummary TEXT,
        KitchenComment TEXT,
        Status INTEGER NOT NULL
    );

    CREATE TABLE TableTransferLogs (
        Id TEXT NOT NULL PRIMARY KEY,
        SourceTableNumber TEXT NOT NULL,
        TargetTableNumber TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        OperatorName TEXT NOT NULL,
        IsMerge INTEGER NOT NULL,
        TimestampUtc TEXT NOT NULL
    );

    CREATE TABLE OrderDiscountAudits (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        OrderItemId TEXT,
        DiscountType INTEGER NOT NULL,
        Value TEXT NOT NULL,
        AmountSaved INTEGER NOT NULL,
        Reason TEXT NOT NULL,
        AuthorizedByOperatorId TEXT NOT NULL,
        AppliedAtUtc TEXT NOT NULL
    );
    """
}
```

Dans `LocalMigrator.swift`, remplacer :
```swift
    static let steps: [String] = [schemaV1]
```
par :
```swift
    static let steps: [String] = [schemaV1, schemaV2]
```

- [ ] **Step 4: Dépôt des commandes**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalOrderRepository.swift` :
```swift
import Foundation

/// Valeurs de l'enum `OrderStatus` du .NET (`Orders.Status`).
enum LocalOrderStatus: Int {
    case open = 0, sentToKitchen = 1, billRequested = 2, paid = 3, cancelled = 4
}

/// Identifiant d'opérateur « inconnu » : le .NET stocke `Guid.Empty` quand la requête n'en porte pas.
let localNoOperator = UUID(uuidString: "00000000-0000-0000-0000-000000000000")!

struct LocalOrderRepository {
    let db: SQLiteDatabase

    /// L'en-tête et les lignes de la commande, sans les infos de table (serveur, couverts) ni les totaux.
    func order(id: UUID) throws -> ActiveOrder? {
        guard let row = try db.query("SELECT * FROM Orders WHERE Id = ?", [.uuid(id)]).first else { return nil }
        let orderLines = try lines(orderId: id)
        return ActiveOrder(
            orderId: id, tableNumber: row.string("TableNumber") ?? "", openedAtUtc: row.date("CreatedAtUtc"), lines: orderLines,
            globalDiscountType: row.int("GlobalDiscountType").flatMap { DiscountType(rawValue: $0) },
            globalDiscountValue: row.decimal("GlobalDiscountValue") ?? 0, globalDiscountReason: row.string("GlobalDiscountReason"),
            destination: OrderDestination(rawValue: row.int("Destination") ?? 0) ?? .takeaway,
            pickupNumber: row.string("PickupNumber"), pickupBuzzer: row.string("PickupBuzzer")
        )
    }

    func lines(orderId: UUID) throws -> [OrderLine] {
        try db.query("SELECT * FROM OrderItems WHERE OrderId = ? ORDER BY OrderedAtUtc, rowid", [.uuid(orderId)]).map(Self.decodeLine)
    }

    func insert(_ o: ActiveOrder, operatorId: UUID, status: LocalOrderStatus) throws {
        try db.run(
            """
            INSERT INTO Orders (Id, TableNumber, OperatorId, Status, Destination, PickupNumber, PickupBuzzer, PickupScheduledAtUtc, CreatedAtUtc,
                GlobalDiscountType, GlobalDiscountValue, GlobalDiscountReason, TipAmount)
            VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?, ?, 0)
            """,
            [.uuid(o.orderId), .text(o.tableNumber), .uuid(operatorId), .integer(status.rawValue), .integer(o.destination.rawValue),
             .string(o.pickupNumber), .string(o.pickupBuzzer), .date(o.openedAtUtc ?? Date()), .integer(o.globalDiscountType?.rawValue),
             .decimal(o.globalDiscountValue), .string(o.globalDiscountReason)]
        )
    }

    func insert(_ l: OrderLine, orderId: UUID) throws {
        let modifiers = String(decoding: try JSONEncoder().encode(l.modifiersSummary), as: UTF8.self)
        try db.run(
            """
            INSERT INTO OrderItems (Id, OrderId, ProductId, ProductName, Quantity, UnitPrice, TaxRatePercent, TaxRateTakeawayPercent, IsFoodVoucherEligible,
                PreparationStationId, IsDispatched, SelectedModifiers, ModifiersPriceExtra, KitchenComment, Course, DiscountPercent, IsComp, CompReason,
                IsHappyHourApplied, OriginalUnitPrice, AppliedHappyHourScheduleId, OrderedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?, NULL, ?, ?, ?, NULL, ?, ?, ?, ?)
            """,
            [.uuid(l.lineId), .uuid(orderId), .uuid(l.productId), .text(l.productName), .integer(l.quantity), .integer(l.unitPrice.cents),
             .decimal(l.taxRatePercent), .decimal(l.taxRateTakeawayPercent), .string(l.preparationStationId), .bool(l.isDispatched), .text(modifiers),
             .integer(l.modifiersPriceExtra.cents), .integer(l.course.rawValue), .decimal(l.discountPercent), .bool(l.isComp),
             .bool(l.isHappyHourApplied), .integer(l.originalUnitPrice?.cents), .uuid(l.appliedHappyHourScheduleId), .date(Date())]
        )
    }

    func setQuantity(lineId: UUID, quantity: Int) throws {
        try db.run("UPDATE OrderItems SET Quantity = ? WHERE Id = ?", [.integer(quantity), .uuid(lineId)])
    }

    /// Marque comme envoyées en cuisine toutes les lignes de la commande.
    func markDispatched(orderId: UUID) throws {
        try db.run("UPDATE OrderItems SET IsDispatched = 1 WHERE OrderId = ? AND IsDispatched = 0", [.uuid(orderId)])
    }

    func setStatus(orderId: UUID, _ status: LocalOrderStatus) throws {
        try db.run("UPDATE Orders SET Status = ? WHERE Id = ?", [.integer(status.rawValue), .uuid(orderId)])
    }

    /// `false` si la commande est inconnue.
    func setDestination(orderId: UUID, _ destination: OrderDestination) throws -> Bool {
        try db.run("UPDATE Orders SET Destination = ? WHERE Id = ?", [.integer(destination.rawValue), .uuid(orderId)]) > 0
    }

    func setTableNumber(orderId: UUID, _ number: String) throws {
        try db.run("UPDATE Orders SET TableNumber = ? WHERE Id = ?", [.text(number), .uuid(orderId)])
    }

    /// `type == nil` retire la remise. `false` si la commande est inconnue.
    func setDiscount(orderId: UUID, type: DiscountType?, value: Decimal, reason: String?) throws -> Bool {
        try db.run(
            "UPDATE Orders SET GlobalDiscountType = ?, GlobalDiscountValue = ?, GlobalDiscountReason = ? WHERE Id = ?",
            [.integer(type?.rawValue), .decimal(value), .string(reason), .uuid(orderId)]
        ) > 0
    }

    /// Passe la ligne en gratuité. `false` si la ligne n'appartient pas à la commande.
    func comp(lineId: UUID, orderId: UUID, reason: String) throws -> Bool {
        try db.run("UPDATE OrderItems SET IsComp = 1, CompReason = ? WHERE Id = ? AND OrderId = ?", [.text(reason), .uuid(lineId), .uuid(orderId)]) > 0
    }

    func moveLines(from source: UUID, to target: UUID) throws {
        try db.run("UPDATE OrderItems SET OrderId = ? WHERE OrderId = ?", [.uuid(target), .uuid(source)])
    }

    func insertTransferLog(source: String, target: String, orderId: UUID, operatorName: String, isMerge: Bool) throws {
        try db.run(
            """
            INSERT INTO TableTransferLogs (Id, SourceTableNumber, TargetTableNumber, OrderId, OperatorId, OperatorName, IsMerge, TimestampUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .text(source), .text(target), .uuid(orderId), .uuid(localNoOperator), .text(operatorName), .bool(isMerge), .date(Date())]
        )
    }

    func insertDiscountAudit(orderId: UUID, itemId: UUID?, type: DiscountType, value: Decimal, saved: Money, reason: String, operatorId: UUID) throws {
        try db.run(
            """
            INSERT INTO OrderDiscountAudits (Id, OrderId, OrderItemId, DiscountType, Value, AmountSaved, Reason, AuthorizedByOperatorId, AppliedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .uuid(orderId), .uuid(itemId), .integer(type.rawValue), .decimal(value), .integer(saved.cents), .text(reason),
             .uuid(operatorId), .date(Date())]
        )
    }

    private static func decodeLine(_ r: SQLRow) throws -> OrderLine {
        let modifiers = try JSONDecoder().decode([String].self, from: Data((r.string("SelectedModifiers") ?? "[]").utf8))
        return OrderLine(
            lineId: r.uuid("Id")!, productId: r.uuid("ProductId")!, productName: r.string("ProductName") ?? "", quantity: r.int("Quantity") ?? 1,
            unitPrice: Money(cents: r.int("UnitPrice") ?? 0), taxRatePercent: r.decimal("TaxRatePercent") ?? 10,
            preparationStationId: r.string("PreparationStationId"), isDispatched: r.bool("IsDispatched"), modifiersSummary: modifiers,
            course: CourseType(rawValue: r.int("Course") ?? 0) ?? .direct, isComp: r.bool("IsComp"),
            discountPercent: r.decimal("DiscountPercent") ?? 0, modifiersPriceExtra: Money(cents: r.int("ModifiersPriceExtra") ?? 0),
            taxRateTakeawayPercent: r.decimal("TaxRateTakeawayPercent"), isHappyHourApplied: r.bool("IsHappyHourApplied"),
            originalUnitPrice: r.int("OriginalUnitPrice").map { Money(cents: $0) }, appliedHappyHourScheduleId: r.uuid("AppliedHappyHourScheduleId")
        )
    }
}
```

- [ ] **Step 5: Dépôt de la cuisine**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalKitchenRepository.swift` :
```swift
import Foundation

/// Résolution du poste de préparation : ligne → article → famille → cuisine chaude (`PreparationStations.Resolve` du .NET).
enum LocalStations {
    static let hotKitchen = "HOT_KITCHEN"

    static func normalize(_ id: String?) -> String? {
        guard let trimmed = id?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else { return nil }
        return trimmed
    }

    static func resolve(item: String?, product: String?, category: String?) -> String {
        normalize(item) ?? normalize(product) ?? normalize(category) ?? hotKitchen
    }
}

struct LocalKitchenRepository {
    let db: SQLiteDatabase

    /// `productIds` : un identifiant d'article par élément de `ticket.items`, dans le même ordre.
    func insert(_ ticket: KitchenTicket, productIds: [UUID]) throws {
        guard ticket.items.count == productIds.count else {
            throw SQLiteError(code: -1, message: "Bon cuisine : \(ticket.items.count) articles pour \(productIds.count) identifiants")
        }
        try db.run(
            """
            INSERT INTO KitchenTickets (Id, OrderId, TableNumber, ServerName, CoversCount, StationId, Status, DispatchedAtUtc, PreparedAtUtc, CompletedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL, NULL)
            """,
            [.uuid(ticket.id), .uuid(ticket.orderId), .text(ticket.tableNumber), .text(ticket.serverName ?? ""), .integer(ticket.coversCount ?? 1),
             .text(ticket.stationId), .integer(ticket.status.rawValue), .date(ticket.dispatchedAtUtc ?? Date())]
        )
        for (item, productId) in zip(ticket.items, productIds) {
            try db.run(
                """
                INSERT INTO KitchenTicketItems (Id, TicketId, ProductId, ProductName, Quantity, ModifiersSummary, KitchenComment, Status)
                VALUES (?, ?, ?, ?, ?, ?, ?, 0)
                """,
                [.uuid(item.id), .uuid(ticket.id), .uuid(productId), .text(item.productName), .integer(item.quantity),
                 .string(item.modifiersSummary), .string(item.kitchenComment)]
            )
        }
    }

    /// Les `limit` derniers bons, tous statuts, du plus récent au plus ancien (comme `GET /api/kds/tickets`).
    func recentTickets(limit: Int) throws -> [KitchenTicket] {
        try db.query("SELECT * FROM KitchenTickets ORDER BY DispatchedAtUtc DESC, rowid DESC LIMIT ?", [.integer(limit)]).map { row in
            let id = row.uuid("Id")!
            let items = try db.query("SELECT * FROM KitchenTicketItems WHERE TicketId = ? ORDER BY rowid", [.uuid(id)]).map {
                KitchenTicketItem(
                    id: $0.uuid("Id")!, productName: $0.string("ProductName") ?? "", quantity: $0.int("Quantity") ?? 1,
                    modifiersSummary: $0.string("ModifiersSummary"), kitchenComment: $0.string("KitchenComment")
                )
            }
            return KitchenTicket(
                id: id, orderId: row.uuid("OrderId"), tableNumber: row.string("TableNumber") ?? "?", serverName: row.string("ServerName"),
                coversCount: row.int("CoversCount"), stationId: row.string("StationId") ?? LocalStations.hotKitchen,
                status: TicketStatus(rawValue: row.int("Status") ?? 0) ?? .pending, dispatchedAtUtc: row.date("DispatchedAtUtc"), items: items
            )
        }
    }

    /// Fait avancer le bon d'un cran (en attente → en préparation → prêt → servi ; servi reste servi). `false` si le bon est inconnu.
    func bump(id: UUID) throws -> Bool {
        guard let row = try db.query("SELECT Status, PreparedAtUtc FROM KitchenTickets WHERE Id = ?", [.uuid(id)]).first else { return false }
        let current = TicketStatus(rawValue: row.int("Status") ?? 0) ?? .pending
        let next = TicketStatus(rawValue: min(current.rawValue + 1, TicketStatus.served.rawValue)) ?? .served
        let now = Date()
        let prepared = (next == .inPreparation && row.date("PreparedAtUtc") == nil) ? now : row.date("PreparedAtUtc")
        try db.run(
            "UPDATE KitchenTickets SET Status = ?, PreparedAtUtc = ?, CompletedAtUtc = COALESCE(?, CompletedAtUtc) WHERE Id = ?",
            [.integer(next.rawValue), .date(prepared), .date(next == .served ? now : nil), .uuid(id)]
        )
        return true
    }
}
```

- [ ] **Step 6: État des tables et postes de repli**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalFloorRepository+Orders.swift` :
```swift
import Foundation

extension LocalFloorRepository {
    func table(_ number: String) throws -> DiningTable? {
        try tables().first { $0.tableNumber == number }
    }

    func waiterId(of number: String) throws -> UUID? {
        try db.query("SELECT AssignedWaiterId FROM DiningTables WHERE TableNumber = ?", [.text(number)]).first?.uuid("AssignedWaiterId")
    }

    /// Met à jour l'état d'occupation d'une table. `orderId == nil` et `status == .free` libèrent la table.
    func update(_ number: String, status: TableStatus, covers: Int, waiterName: String?, waiterId: UUID?, activeOrderId: UUID?, openedAt: Date?) throws {
        try db.run(
            """
            UPDATE DiningTables SET Status = ?, CoversCount = ?, AssignedWaiterName = ?, AssignedWaiterId = ?, ActiveOrderId = ?, OpenedAtUtc = ?, UpdatedAtUtc = ?
            WHERE TableNumber = ?
            """,
            [.integer(status.rawValue), .integer(covers), .string(waiterName), .uuid(waiterId), .uuid(activeOrderId), .date(openedAt), .date(Date()), .text(number)]
        )
    }
}
```

Dans `LocalCatalogRepository.swift`, remplacer :
```swift
    private func modifierGroupsByProduct() throws -> [UUID: [ModifierGroup]] {
```
par :
```swift
    /// Poste propre de l'article et poste de sa famille (repli du routage cuisine). Inclut les articles archivés.
    func stationFallbacks() throws -> [UUID: (product: String?, category: String?)] {
        let rows = try db.query(
            """
            SELECT p.Id AS Id, p.PreparationStationId AS PStation, c.PreparationStationId AS CStation
            FROM Products p LEFT JOIN Categories c ON c.Id = p.CategoryId
            """
        )
        var result: [UUID: (product: String?, category: String?)] = [:]
        for row in rows {
            if let id = row.uuid("Id") { result[id] = (product: row.string("PStation"), category: row.string("CStation")) }
        }
        return result
    }

    private func modifierGroupsByProduct() throws -> [UUID: [ModifierGroup]] {
```

- [ ] **Step 7: Accesseurs de l'acteur et regroupement des stubs**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var gridRepository: LocalGridRepository { LocalGridRepository(db: db) }
```
par :
```swift
    var gridRepository: LocalGridRepository { LocalGridRepository(db: db) }
    var orderRepository: LocalOrderRepository { LocalOrderRepository(db: db) }
    var kitchenRepository: LocalKitchenRepository { LocalKitchenRepository(db: db) }
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Salle & commandes (plan 1b)
    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws { throw unsupported() }
    public func activeOrder(table: String) async throws -> ActiveOrder? { throw unsupported() }
    public func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder { throw unsupported() }
    public func dispatch(table: String) async throws { throw unsupported() }
    public func fireSuite(table: String) async throws { throw unsupported() }
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult { throw unsupported() }
    public func setDestination(orderId: UUID, destination: OrderDestination) async throws { throw unsupported() }
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws { throw unsupported() }
    public func removeDiscount(orderId: UUID) async throws { throw unsupported() }
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws { throw unsupported() }
```
par :
```swift
    // MARK: Commandes de salle (retiré par la tâche 2)
    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws { throw unsupported() }
    public func activeOrder(table: String) async throws -> ActiveOrder? { throw unsupported() }
    public func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder { throw unsupported() }
    public func setDestination(orderId: UUID, destination: OrderDestination) async throws { throw unsupported() }

    // MARK: Envoi cuisine (retiré par la tâche 3)
    public func dispatch(table: String) async throws { throw unsupported() }
    public func fireSuite(table: String) async throws { throw unsupported() }

    // MARK: Transfert et fusion (retiré par la tâche 4)
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult { throw unsupported() }

    // MARK: Remises et gratuités (retiré par la tâche 5)
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws { throw unsupported() }
    public func removeDiscount(orderId: UUID) async throws { throw unsupported() }
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws { throw unsupported() }
```

- [ ] **Step 8: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalOrderStoreTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 5 tests … passed`, aucun avertissement.

- [ ] **Step 9: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `208 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1b-progress.md` (tâche 1 `fait`, PosKit `208`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1b-progress.md
git commit -m "feat(ios-local): migration v2 et dépôts commandes, cuisine et état des tables"
```

---

### Task 2: Commandes de salle (ouverture, lecture, ajout de lignes, destination)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Orders.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Floor.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderTestSupport.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderTests.swift`

**Interfaces:**
- Consumes (tâche 1) : `orderRepository`, `LocalOrderRepository` (`order`, `lines`, `insert`, `setQuantity`, `setDestination`), `LocalFloorRepository` (`table`, `waiterId`, `update`, `tableExists`, `insert`), `localNoOperator`, `LocalOrderStatus` ; (plan 1a) `requireAuth()`, `db`, `floorRepository`, `makeLocalAPI`.
- Produces :
  - `LocalPosAPI.defaultDestination(forTable:) -> OrderDestination`, `LocalPosAPI.decorate(_ order: ActiveOrder) -> ActiveOrder` (renseigne `totalPrice` des lignes et `totalTtcAmount`), `LocalPosAPI.activeOrder(on table: DiningTable) throws -> ActiveOrder?` (ajoute serveur, couverts, ouverture).
  - Méthodes `PosAPI` : `openTable`, `activeOrder(table:)`, `addItems(table:items:)`, `setDestination`.
  - Helpers de test : `localProduct(_:_:)`, `localInput(_:quantity:course:modifiers:extra:)`, `seatTable(_:_:covers:waiter:items:)`.

- [ ] **Step 1: Écrire les helpers et les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderTestSupport.swift` :
```swift
import Foundation
@testable import PosKit

func localProduct(_ api: LocalPosAPI, _ name: String) async throws -> Product {
    guard let product = try await api.products().first(where: { $0.name == name }) else { throw APIError.notFound(name) }
    return product
}

/// Ligne de commande envoyée telle que le ferait le client (tarif et taxes de l'article).
func localInput(_ p: Product, quantity: Int = 1, course: CourseType = .direct, modifiers: [String] = [], extra: Money = .zero) -> OrderItemInput {
    OrderItemInput(
        productId: p.id, productName: p.name, quantity: quantity, unitPrice: p.price, taxRatePercent: p.taxRatePercent,
        preparationStationId: p.preparationStationId, modifiers: modifiers, course: course, modifiersPriceExtra: extra,
        taxRateTakeawayPercent: p.taxRateTakeawayPercent, isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil
    )
}

/// Ouvre une table puis y ajoute les lignes données.
func seatTable(_ api: LocalPosAPI, _ number: String, covers: Int = 2, waiter: String = "Sophie", items: [OrderItemInput] = []) async throws {
    try await api.openTable(number: number, covers: covers, operatorId: nil, waiterName: waiter)
    if !items.isEmpty { _ = try await api.addItems(table: number, items: items) }
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalOrderTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : commandes de salle")
struct LocalOrderTests {
    @Test func openTableCreatesEatInOrderAndOccupiesTable() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T1", covers: 3, operatorId: UUID(), waiterName: "Sophie")
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .occupied && table.coversCount == 3 && table.assignedWaiterName == "Sophie" && table.openedAtUtc != nil)
        let order = try #require(try await api.activeOrder(table: "T1"))
        #expect(order.lines.isEmpty && order.destination == .eatIn && order.waiterName == "Sophie" && order.coversCount == 3)
        #expect(table.activeOrderId == order.orderId)
    }

    @Test func openTableDefaultsCoversAndWaiter() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T2", covers: 0, operatorId: nil, waiterName: nil)
        let order = try #require(try await api.activeOrder(table: "T2"))
        #expect(order.coversCount == 2 && order.waiterName == "Serveur")
    }

    @Test func reopeningKeepsTheExistingOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 2, items: [localInput(burger)])
        let before = try #require(try await api.activeOrder(table: "T1"))
        try await api.openTable(number: "T1", covers: 5, operatorId: nil, waiterName: "Léa")
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.orderId == before.orderId && after.lines.count == 1)
        #expect(after.coversCount == 5 && after.waiterName == "Léa")
    }

    @Test func addItemsMergesIdenticalUndispatchedLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T2")
        _ = try await api.addItems(table: "T2", items: [localInput(burger), localInput(burger, quantity: 2)])
        let order = try await api.addItems(table: "T2", items: [localInput(burger, modifiers: ["Bleu"]), localInput(cafe)])
        #expect(order.lines.count == 3)
        #expect(order.lines[0].quantity == 3 && order.lines[0].totalPrice == Money(cents: 3 * 1950))
        #expect(order.lines[1].modifiersSummary == ["Bleu"])
        #expect(order.totalTtcAmount == Money(cents: 3 * 1950 + 1950 + 850))
    }

    @Test func nonPositiveQuantityIsRejected() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        for bad in [0, -1] {
            await #expect(throws: APIError.server(status: 400, message: "La quantité doit être positive.")) {
                try await api.addItems(table: "T3", items: [localInput(burger, quantity: bad)])
            }
        }
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func addItemsCreatesAnUnknownTable() async throws {
        let api = try await makeLocalAPI()
        let cafe = try await localProduct(api, "Café Gourmand")
        let order = try await api.addItems(table: "T99", items: [localInput(cafe)])
        #expect(order.tableNumber == "T99" && order.lines.count == 1)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T99" })
        #expect(table.capacity == 2 && table.status == .occupied && table.activeOrderId == order.orderId)
    }

    @Test func counterTableDefaultsToTakeaway() async throws {
        let api = try await makeLocalAPI()
        let cafe = try await localProduct(api, "Café Gourmand")
        #expect(try await api.addItems(table: "Comptoir", items: [localInput(cafe)]).destination == .takeaway)
        #expect(try await api.addItems(table: "T3", items: [localInput(cafe)]).destination == .eatIn)
    }

    @Test func modifiersWithCommasRoundTrip() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let modifiers = ["Sans oignon, sans sel", "Bleu"]
        _ = try await api.addItems(table: "T4", items: [localInput(burger, modifiers: modifiers, extra: Money(cents: 250))])
        let line = try #require(try await api.activeOrder(table: "T4")?.lines.first)
        #expect(line.modifiersSummary == modifiers && line.modifiersPriceExtra == Money(cents: 250))
        #expect(line.totalPrice == Money(cents: 1950 + 250))
    }

    @Test func setDestinationUpdatesTheOrderAndReportsUnknownOnes() async throws {
        let api = try await makeLocalAPI()
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        try await seatTable(api, "T5", items: [localInput(pizza)])
        let order = try #require(try await api.activeOrder(table: "T5"))
        #expect(order.destination == .eatIn)
        try await api.setDestination(orderId: order.orderId, destination: .takeaway)
        #expect(try await api.activeOrder(table: "T5")?.destination == .takeaway)
        await #expect(throws: APIError.notFound("Commande introuvable.")) {
            try await api.setDestination(orderId: UUID(), destination: .eatIn)
        }
    }

    @Test func orderCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.openTable(number: "T1", covers: 2, operatorId: nil, waiterName: nil) }
        await #expect(throws: APIError.unauthorized) { try await api.activeOrder(table: "T1") }
        await #expect(throws: APIError.unauthorized) { try await api.addItems(table: "T1", items: []) }
        await #expect(throws: APIError.unauthorized) { try await api.setDestination(orderId: UUID(), destination: .eatIn) }
    }

    @Test func tablesReportTheTotalOfTheirActiveOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T5", items: [localInput(burger), localInput(cafe)])
        let tables = try await api.tables()
        #expect(tables.first { $0.tableNumber == "T5" }?.activeOrderTotalTtc == Money(cents: 2800))
        #expect(tables.filter { $0.tableNumber != "T5" }.allSatisfy { $0.activeOrderTotalTtc == .zero })
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalOrderTests 2>&1 | grep -E "✘|Test run" | head -5`
Expected: échecs `APIError.server(status: 501 …)` (les méthodes répondent encore « non disponible »).

- [ ] **Step 3: Implémenter les commandes de salle**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Orders.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Une commande prise sur une table de salle est consommée sur place ; seul « Comptoir » (ventes directes) reste à emporter
    /// (`TableManagementService.DestinationForTable`).
    func defaultDestination(forTable number: String) -> OrderDestination {
        number.caseInsensitiveCompare(DiningTable.counterNumber) == .orderedSame ? .takeaway : .eatIn
    }

    /// Renseigne le total de chaque ligne et le total TTC de la commande (même calcul que le serveur : `OrderMath`).
    func decorate(_ order: ActiveOrder) -> ActiveOrder {
        var decorated = order
        let cart = decorated.lines.map(CartLine.init(serverLine:))
        for index in decorated.lines.indices { decorated.lines[index].totalPrice = OrderMath.lineTotal(cart[index]) }
        decorated.totalTtcAmount = OrderMath.totals(lines: cart, destination: decorated.destination, discount: decorated.globalDiscount).totalTtc
        return decorated
    }

    /// Commande active d'une table, avec serveur, couverts et heure d'ouverture de la table.
    func activeOrder(on table: DiningTable) throws -> ActiveOrder? {
        guard let id = table.activeOrderId, var order = try orderRepository.order(id: id) else { return nil }
        order.waiterName = table.assignedWaiterName
        order.coversCount = table.coversCount
        order.openedAtUtc = table.openedAtUtc ?? order.openedAtUtc
        return decorate(order)
    }

    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws {
        try requireAuth()
        let covers = covers <= 0 ? 2 : covers
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            if try !floor.tableExists(number) { try floor.insert(DiningTable(tableNumber: number, capacity: covers)) }
            let existing = try floor.table(number)
            // Écart assumé : une table déjà occupée garde sa commande (le .NET en crée une nouvelle et orpheline l'ancienne).
            let orderId: UUID
            if let id = existing?.activeOrderId, try orders.order(id: id) != nil {
                orderId = id
            } else {
                let created = ActiveOrder(tableNumber: number, destination: defaultDestination(forTable: number))
                try orders.insert(created, operatorId: operatorId ?? localNoOperator, status: .open)
                orderId = created.orderId
            }
            let openedAt = existing?.activeOrderId == nil ? Date() : (existing?.openedAtUtc ?? Date())
            try floor.update(number, status: .occupied, covers: covers, waiterName: waiterName ?? "Serveur", waiterId: operatorId, activeOrderId: orderId, openedAt: openedAt)
        }
    }

    public func activeOrder(table: String) async throws -> ActiveOrder? {
        try requireAuth()
        guard let found = try floorRepository.table(table) else { return nil }
        return try activeOrder(on: found)
    }

    public func addItems(table number: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        try requireAuth()
        guard items.allSatisfy({ $0.quantity > 0 }) else { throw APIError.server(status: 400, message: "La quantité doit être positive.") }
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            if try !floor.tableExists(number) { try floor.insert(DiningTable(tableNumber: number, capacity: 2)) }
            guard let table = try floor.table(number) else { throw SQLiteError(code: -1, message: "Table \(number) introuvable après création") }
            let orderId: UUID
            if let id = table.activeOrderId, try orders.order(id: id) != nil {
                orderId = id
            } else {
                let created = ActiveOrder(tableNumber: number, destination: defaultDestination(forTable: number))
                try orders.insert(created, operatorId: localNoOperator, status: .open)
                orderId = created.orderId
            }
            var lines = try orders.lines(orderId: orderId)
            for input in items {
                let sameLine = lines.firstIndex {
                    !$0.isDispatched && $0.productId == input.productId && $0.course == input.course && $0.unitPrice == input.unitPrice
                        && $0.isHappyHourApplied == input.isHappyHourApplied && $0.modifiersPriceExtra == input.modifiersPriceExtra
                        && $0.modifiersSummary == input.modifiers
                }
                if let index = sameLine {
                    lines[index].quantity += input.quantity
                    try orders.setQuantity(lineId: lines[index].lineId, quantity: lines[index].quantity)
                } else {
                    let line = OrderLine(
                        productId: input.productId, productName: input.productName, quantity: input.quantity, unitPrice: input.unitPrice,
                        taxRatePercent: input.taxRatePercent, preparationStationId: input.preparationStationId, modifiersSummary: input.modifiers,
                        course: input.course, modifiersPriceExtra: input.modifiersPriceExtra, taxRateTakeawayPercent: input.taxRateTakeawayPercent,
                        isHappyHourApplied: input.isHappyHourApplied, originalUnitPrice: input.originalUnitPrice,
                        appliedHappyHourScheduleId: input.appliedHappyHourScheduleId
                    )
                    try orders.insert(line, orderId: orderId)
                    lines.append(line)
                }
            }
            try floor.update(
                number, status: table.activeOrderId == orderId ? table.status : .occupied, covers: table.coversCount,
                waiterName: table.assignedWaiterName, waiterId: try floor.waiterId(of: number), activeOrderId: orderId,
                openedAt: table.openedAtUtc ?? Date()
            )
        }
        guard let table = try floorRepository.table(number), let order = try activeOrder(on: table) else {
            throw SQLiteError(code: -1, message: "Commande de la table \(number) introuvable après écriture")
        }
        return order
    }

    public func setDestination(orderId: UUID, destination: OrderDestination) async throws {
        try requireAuth()
        guard try orderRepository.setDestination(orderId: orderId, destination) else { throw APIError.notFound("Commande introuvable.") }
    }
}
```

Dans `LocalPosAPI+Floor.swift`, remplacer :
```swift
    public func tables() async throws -> [DiningTable] { try floorRepository.tables() }
```
par :
```swift
    public func tables() async throws -> [DiningTable] {
        try floorRepository.tables().map { table in
            var withTotal = table
            if let order = try activeOrder(on: table) { withTotal.activeOrderTotalTtc = order.totalTtcAmount ?? .zero }
            return withTotal
        }
    }
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Commandes de salle (retiré par la tâche 2)` : ce commentaire, les 4 lignes `openTable`, `activeOrder`, `addItems`, `setDestination`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalOrderTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 11 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `219 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1b-progress.md` (tâche 2 `fait`, PosKit `219`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1b-progress.md
git commit -m "feat(ios-local): commandes de salle (ouverture de table, lignes, destination) sur SQLite"
```

---

### Task 3: Envoi cuisine, réclame suite et bons cuisine

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Kitchen.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalAuthTests.swift` (le test du plan 1a `unsupportedMethodsAnswer501` citait `kitchenTickets`, désormais porté)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalKitchenTests.swift`

**Interfaces:**
- Consumes : `orderRepository` (`order`, `markDispatched`, `setStatus`), `kitchenRepository` (`insert`, `recentTickets`, `bump`), `catalogRepository.stationFallbacks()`, `floorRepository.table(_:)`, `LocalStations.resolve`, `requireAuth()` ; helpers de test (tâche 2).
- Produces : méthodes `PosAPI` `dispatch(table:)`, `fireSuite(table:)`, `kitchenTickets()`, `bumpTicket(id:)`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalKitchenTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : cuisine")
struct LocalKitchenTests {
    @Test func dispatchSplitsTheOrderByStationAndMarksLinesSent() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        let tartare = try await localProduct(api, "Tartare de Saumon")
        try await seatTable(api, "T2", covers: 2, waiter: "Sophie", items: [localInput(burger), localInput(cafe), localInput(tartare)])
        try await api.dispatch(table: "T2")
        let tickets = try await api.kitchenTickets()
        #expect(Set(tickets.map(\.stationId)) == ["HOT_KITCHEN", "BAR", "COLD"])
        #expect(tickets.allSatisfy { $0.tableNumber == "T2" && $0.serverName == "Sophie" && $0.coversCount == 2 && $0.status == .pending && $0.items.count == 1 })
        #expect(try await api.activeOrder(table: "T2")?.lines.allSatisfy(\.isDispatched) == true)
        // Second envoi sans nouveauté : aucun bon supplémentaire.
        try await api.dispatch(table: "T2")
        #expect(try await api.kitchenTickets().count == 3)
    }

    @Test func dispatchAfterNewLineOnlySendsNewLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T3", items: [localInput(burger)])
        try await api.dispatch(table: "T3")
        let order = try await api.addItems(table: "T3", items: [localInput(burger)])
        #expect(order.lines.count == 2)
        #expect(order.lines[0].isDispatched && !order.lines[1].isDispatched)
        try await api.dispatch(table: "T3")
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 2)
        #expect(tickets.allSatisfy { $0.items.count == 1 && $0.items[0].quantity == 1 })
    }

    @Test func stationFallsBackToTheCategoryThenToTheHotKitchen() async throws {
        let api = try await makeLocalAPI()
        try await api.createProduct(ProductDraft(name: "Spritz", categoryId: "CAT_DRINKS", price: Money(cents: 900), stationId: nil))
        try await api.createProduct(ProductDraft(name: "Soupe du jour", categoryId: "CAT_STARTERS", price: Money(cents: 600), stationId: nil))
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons & Vins", colorHex: "#3498DB", displayOrder: 5, preparationStationId: "BAR")
        let spritz = try await localProduct(api, "Spritz")
        let soupe = try await localProduct(api, "Soupe du jour")
        try await seatTable(api, "T4", items: [localInput(spritz), localInput(soupe)])
        try await api.dispatch(table: "T4")
        let stations = Dictionary(uniqueKeysWithValues: try await api.kitchenTickets().map { ($0.items[0].productName, $0.stationId) })
        #expect(stations == ["Spritz": "BAR", "Soupe du jour": "HOT_KITCHEN"])
    }

    @Test func takeawayTicketShowsThePrefix() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.addItems(table: "Comptoir", items: [localInput(burger)])
        try await api.dispatch(table: "Comptoir")
        #expect(try await api.kitchenTickets().first?.tableNumber == "[À EMPORTER] Comptoir")
    }

    @Test func dispatchWithoutAnActiveOrderIsNotFound() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T7", covers: 2, operatorId: nil, waiterName: nil)
        try await api.dispatch(table: "T7")  // commande ouverte mais vide : rien à envoyer, pas d'erreur
        #expect(try await api.kitchenTickets().isEmpty)
        await #expect(throws: APIError.notFound(nil)) { try await api.dispatch(table: "T1") }
        await #expect(throws: APIError.notFound(nil)) { try await api.dispatch(table: "T404") }
    }

    @Test func fireSuiteCreatesATicketOnlyForSuiteLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let tiramisu = try await localProduct(api, "Tiramisu Maison")
        try await seatTable(api, "T4", items: [localInput(burger), localInput(tiramisu, quantity: 2, course: .suite)])
        try await api.fireSuite(table: "T4")
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 1)
        let ticket = try #require(tickets.first)
        #expect(ticket.stationId == "HOT_KITCHEN" && ticket.tableNumber == "T4" && ticket.items.count == 1)
        #expect(ticket.items[0].productName == "[RÉCLAME SUITE] Tiramisu Maison" && ticket.items[0].quantity == 2)
        // Sans ligne « suite » (ou table inconnue), aucun bon vide.
        try await seatTable(api, "T5", items: [localInput(burger)])
        try await api.fireSuite(table: "T5")
        try await api.fireSuite(table: "T404")
        #expect(try await api.kitchenTickets().count == 1)
    }

    @Test func bumpAdvancesTheStatusAndChecksTheRole() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T6", items: [localInput(burger)])
        try await api.dispatch(table: "T6")
        let id = try #require(try await api.kitchenTickets().first?.id)

        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.forbidden("Réservé à la cuisine et aux responsables.")) { try await api.bumpTicket(id: id) }

        _ = try await api.login(pin: "5678")
        var statuses: [TicketStatus] = []
        for _ in 0..<4 {
            try await api.bumpTicket(id: id)
            statuses.append(try #require(try await api.kitchenTickets().first { $0.id == id }).status)
        }
        #expect(statuses == [.inPreparation, .ready, .served, .served])
        await #expect(throws: APIError.notFound(nil)) { try await api.bumpTicket(id: UUID()) }
    }

    @Test func kitchenTicketsAreNewestFirstAndCappedAtFifty() async throws {
        let api = try await makeLocalAPI()
        let tiramisu = try await localProduct(api, "Tiramisu Maison")
        try await seatTable(api, "T1", items: [localInput(tiramisu, course: .suite)])
        for _ in 0..<51 { try await api.fireSuite(table: "T1") }
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 50)
        #expect(zip(tickets, tickets.dropFirst()).allSatisfy { ($0.dispatchedAtUtc ?? .distantPast) >= ($1.dispatchedAtUtc ?? .distantPast) })
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalKitchenTests 2>&1 | grep -E "✘|Test run" | head -5`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter la cuisine**

Le test du plan 1a `unsupportedMethodsAnswer501` doit citer une méthode qui reste non portée : le rapport fiscal l'est jusqu'au sous-projet 2.

Dans `LocalAuthTests.swift`, remplacer :
```swift
        await #expect(throws: APIError.server(status: 501, message: "Non disponible en mode autonome : kitchenTickets()")) {
            try await api.kitchenTickets()
        }
```
par :
```swift
        await #expect(throws: APIError.server(status: 501, message: "Non disponible en mode autonome : xReport(terminalId:)")) {
            try await api.xReport(terminalId: "T01")
        }
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Kitchen.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Envoie en cuisine les lignes pas encore envoyées : un bon par poste de préparation (ligne → article → famille → cuisine chaude),
    /// puis marque les lignes comme envoyées. Rien à envoyer : succès sans bon. Table ou commande absente : `404`.
    public func dispatch(table number: String) async throws {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, kitchen = kitchenRepository, catalog = catalogRepository
        try db.transaction {
            guard let table = try floor.table(number), let orderId = table.activeOrderId, let order = try orders.order(id: orderId) else {
                throw APIError.notFound(nil)
            }
            let pending = order.lines.filter { !$0.isDispatched }
            guard !pending.isEmpty else { return }
            let fallbacks = try catalog.stationFallbacks()
            // Un groupe par poste, dans l'ordre d'apparition des lignes (bons déterministes).
            var groups: [(station: String, lines: [OrderLine])] = []
            for line in pending {
                let fallback = fallbacks[line.productId]
                let station = LocalStations.resolve(item: line.preparationStationId, product: fallback?.product, category: fallback?.category)
                if let index = groups.firstIndex(where: { $0.station == station }) {
                    groups[index].lines.append(line)
                } else {
                    groups.append((station: station, lines: [line]))
                }
            }
            let displayTable: String
            if order.destination == .takeaway {
                let reference = order.pickupNumber.flatMap { $0.isEmpty ? nil : $0 } ?? order.tableNumber
                let buzzer = order.pickupBuzzer.flatMap { $0.isEmpty ? nil : " (Bip: \($0))" } ?? ""
                displayTable = "[À EMPORTER] \(reference)\(buzzer)"
            } else {
                displayTable = order.tableNumber
            }
            for group in groups {
                let ticket = KitchenTicket(
                    orderId: orderId, tableNumber: displayTable, serverName: table.assignedWaiterName ?? "Serveur", coversCount: table.coversCount,
                    stationId: group.station,
                    items: group.lines.map {
                        KitchenTicketItem(productName: $0.productName, quantity: $0.quantity,
                                          modifiersSummary: $0.modifiersSummary.isEmpty ? nil : $0.modifiersSummary.joined(separator: ", "))
                    }
                )
                try kitchen.insert(ticket, productIds: group.lines.map(\.productId))
            }
            try orders.markDispatched(orderId: orderId)
            try orders.setStatus(orderId: orderId, .sentToKitchen)
        }
    }

    /// Réclame la suite : bon unique (cuisine chaude) avec les lignes « suite » de la commande. Sans ligne « suite », aucun bon
    /// (le .NET en créerait un sans article). Table ou commande absente : sans effet, comme le serveur.
    public func fireSuite(table number: String) async throws {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, kitchen = kitchenRepository
        try db.transaction {
            guard let table = try floor.table(number), let orderId = table.activeOrderId, let order = try orders.order(id: orderId) else { return }
            let suite = order.lines.filter { $0.course == .suite }
            guard !suite.isEmpty else { return }
            let ticket = KitchenTicket(
                orderId: orderId, tableNumber: number, serverName: "", coversCount: 1, stationId: LocalStations.hotKitchen,
                items: suite.map {
                    KitchenTicketItem(productName: "[RÉCLAME SUITE] \($0.productName)", quantity: $0.quantity,
                                      modifiersSummary: $0.modifiersSummary.isEmpty ? nil : $0.modifiersSummary.joined(separator: ", "))
                }
            )
            try kitchen.insert(ticket, productIds: suite.map(\.productId))
        }
    }

    /// Les 50 derniers bons, tous statuts (comme `GET /api/kds/tickets`, accessible sans connexion).
    public func kitchenTickets() async throws -> [KitchenTicket] {
        try kitchenRepository.recentTickets(limit: 50)
    }

    public func bumpTicket(id: UUID) async throws {
        let member = try requireAuth()
        guard member.role.canBumpKitchen else { throw APIError.forbidden("Réservé à la cuisine et aux responsables.") }
        guard try kitchenRepository.bump(id: id) else { throw APIError.notFound(nil) }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Envoi cuisine (retiré par la tâche 3)` : ce commentaire, les 2 lignes `dispatch` et `fireSuite`, et la ligne vide qui suit.

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Cuisine (plan 1b)` : ce commentaire, les 2 lignes `kitchenTickets` et `bumpTicket`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalKitchenTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 8 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `227 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1b-progress.md` (tâche 3 `fait`, PosKit `227`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1b-progress.md
git commit -m "feat(ios-local): envoi cuisine par poste, réclame suite et bons cuisine sur SQLite"
```

---

### Task 4: Transfert et fusion de tables

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Transfer.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTransferTests.swift`

**Interfaces:**
- Consumes : `floorRepository` (`table`, `waiterId`, `update`), `orderRepository` (`order`, `moveLines`, `setStatus`, `setTableNumber`, `insertTransferLog`), `LocalOrderStatus.cancelled`, `requireAuth()`, `OperationResult` ; helpers de test.
- Produces : méthode `PosAPI` `transfer(from:to:merge:) -> OperationResult`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTransferTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : transfert et fusion")
struct LocalTransferTests {
    private func names(_ order: ActiveOrder?) -> [String] { order?.lines.map(\.productName) ?? [] }

    @Test func transferMovesTheOrderToAFreeTable() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 4, waiter: "Sophie", items: [localInput(burger)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: false)
        #expect(result.success == true && result.message == "Commande transférée de T1 vers T2")
        let tables = try await api.tables()
        let source = try #require(tables.first { $0.tableNumber == "T1" })
        let target = try #require(tables.first { $0.tableNumber == "T2" })
        #expect(source.status == .free && source.activeOrderId == nil && source.coversCount == 0 && source.assignedWaiterName == nil && source.openedAtUtc == nil)
        #expect(target.status == .occupied && target.coversCount == 4 && target.assignedWaiterName == "Sophie" && target.activeOrderId != nil)
        let order = try #require(try await api.activeOrder(table: "T2"))
        #expect(order.tableNumber == "T2" && names(order) == ["Burger Gourmet Rossini"])
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func transferOntoOccupiedTableIsRefusedAndKeepsBothOrders() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", items: [localInput(burger)])
        try await seatTable(api, "T2", items: [localInput(cafe)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: false)
        #expect(result.success == false && result.message == "Échec du transfert de table.")
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
        #expect(names(try await api.activeOrder(table: "T2")) == ["Café Gourmand"])
    }

    @Test func transferToSameTableIsRefused() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        #expect(try await api.transfer(from: "T1", to: "T1", merge: false).success == false)
        #expect(try await api.transfer(from: "T1", to: "T1", merge: true).success == false)
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
    }

    @Test func transferFromAnEmptyOrUnknownTableFails() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        #expect(try await api.transfer(from: "T3", to: "T4", merge: false).success == false)
        #expect(try await api.transfer(from: "T404", to: "T4", merge: false).success == false)
        // Cible inconnue : la commande source reste intacte.
        #expect(try await api.transfer(from: "T1", to: "T404", merge: false).success == false)
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
    }

    @Test func mergeCombinesLinesAndCovers() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", covers: 2, waiter: "Sophie", items: [localInput(burger)])
        try await seatTable(api, "T2", covers: 4, waiter: "Léa", items: [localInput(cafe)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: true)
        #expect(result.success == true && result.message == "Tables T1 et T2 fusionnées")
        let merged = try #require(try await api.activeOrder(table: "T2"))
        #expect(Set(names(merged)) == ["Burger Gourmet Rossini", "Café Gourmand"] && merged.coversCount == 6 && merged.waiterName == "Léa")
        #expect(merged.totalTtcAmount == Money(cents: 1950 + 850))
        let source = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(source.status == .free && source.activeOrderId == nil && source.coversCount == 0)
    }

    @Test func mergeIntoAFreeTableBehavesLikeATransfer() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 3, items: [localInput(burger)])
        let result = try await api.transfer(from: "T1", to: "T3", merge: true)
        #expect(result.success == true && result.message == "Tables T1 et T3 fusionnées")
        let order = try #require(try await api.activeOrder(table: "T3"))
        #expect(order.coversCount == 3 && names(order) == ["Burger Gourmet Rossini"])
        #expect(try await api.activeOrder(table: "T1") == nil)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalTransferTests 2>&1 | grep -E "✘|Test run" | head -5`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter le transfert et la fusion**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Transfer.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Transfert (`merge == false`) ou fusion (`merge == true`) de la commande d'une table vers une autre.
    /// Échec (`success: false`) si la source n'a pas de commande, si une table est inconnue, si source et cible sont la même table,
    /// ou si un transfert vise une table occupée (le .NET écraserait alors la commande : écart assumé, utiliser la fusion).
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult {
        try requireAuth()
        let failure = OperationResult(success: false, message: merge ? "Échec de la fusion de tables." : "Échec du transfert de table.")
        let floor = floorRepository, orders = orderRepository
        return try db.transaction { () throws -> OperationResult in
            guard from != to, let source = try floor.table(from), let target = try floor.table(to),
                  let sourceOrderId = source.activeOrderId, try orders.order(id: sourceOrderId) != nil
            else { return failure }

            if let targetOrderId = target.activeOrderId, try orders.order(id: targetOrderId) != nil {
                guard merge else { return failure }
                try orders.moveLines(from: sourceOrderId, to: targetOrderId)
                try orders.setStatus(orderId: sourceOrderId, .cancelled)
                try floor.update(
                    to, status: target.status, covers: target.coversCount + source.coversCount, waiterName: target.assignedWaiterName,
                    waiterId: try floor.waiterId(of: to), activeOrderId: targetOrderId, openedAt: target.openedAtUtc
                )
                try orders.insertTransferLog(
                    source: from, target: to, orderId: targetOrderId,
                    operatorName: target.assignedWaiterName ?? source.assignedWaiterName ?? "Serveur", isMerge: true
                )
            } else {
                try orders.setTableNumber(orderId: sourceOrderId, to)
                try floor.update(
                    to, status: .occupied, covers: source.coversCount, waiterName: source.assignedWaiterName,
                    waiterId: try floor.waiterId(of: from), activeOrderId: sourceOrderId, openedAt: source.openedAtUtc
                )
                try orders.insertTransferLog(source: from, target: to, orderId: sourceOrderId, operatorName: source.assignedWaiterName ?? "Serveur", isMerge: false)
            }
            try floor.update(from, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
            return OperationResult(success: true, message: merge ? "Tables \(from) et \(to) fusionnées" : "Commande transférée de \(from) vers \(to)")
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Transfert et fusion (retiré par la tâche 4)` : ce commentaire, la ligne `transfer`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalTransferTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 6 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `233 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1b-progress.md` (tâche 4 `fait`, PosKit `233`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1b-progress.md
git commit -m "feat(ios-local): transfert et fusion de tables sans perte de commande"
```

---

### Task 5: Remises, gratuités et mise à jour de la documentation

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Discounts.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalDiscountTests.swift`
- Modify: `ios/README.md`

**Interfaces:**
- Consumes : `orderRepository` (`order`, `setDiscount`, `comp`, `insertDiscountAudit`), `OrderMath.totals`, `GlobalDiscount`, `CartLine`, `Money`, `requireAuth()`, `localNoOperator` ; helpers de test ; `temporaryDatabasePath()`.
- Produces : méthodes `PosAPI` `applyDiscount`, `removeDiscount`, `compItem`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalDiscountTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : remises et gratuités")
struct LocalDiscountTests {
    private static let discountFailed = APIError.server(status: 400, message: "Opération de remise échouée.")
    private static let compFailed = APIError.server(status: 400, message: "Opération de gratuité échouée.")

    private func seat(_ api: LocalPosAPI, _ table: String, _ items: [OrderItemInput]) async throws -> ActiveOrder {
        try await seatTable(api, table, items: items)
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func percentageDiscountReducesTheTotal() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "  Anniversaire  ", operatorId: nil)
        let discounted = try #require(try await api.activeOrder(table: "T1"))
        #expect(discounted.totalTtcAmount == Money(cents: 1755))
        #expect(discounted.globalDiscountType == .percentage && discounted.globalDiscountValue == 10 && discounted.globalDiscountReason == "Anniversaire")
    }

    @Test func fixedDiscountClampsAtZero() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .fixedAmount, value: 5, reason: "Geste", operatorId: nil)
        #expect(try await api.activeOrder(table: "T1")?.totalTtcAmount == Money(cents: 1450))
        try await api.applyDiscount(orderId: order.orderId, type: .fixedAmount, value: 100, reason: "Geste", operatorId: nil)
        #expect(try await api.activeOrder(table: "T1")?.totalTtcAmount == .zero)
    }

    @Test func discountValidationLeavesTheOrderUntouched() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        let invalid: [(DiscountType, Decimal, String)] = [(.percentage, 101, "Trop"), (.percentage, -5, "Négatif"), (.fixedAmount, -1, "Négatif"), (.percentage, 10, "   ")]
        for (type, value, reason) in invalid {
            await #expect(throws: Self.discountFailed) { try await api.applyDiscount(orderId: order.orderId, type: type, value: value, reason: reason, operatorId: nil) }
        }
        await #expect(throws: Self.discountFailed) { try await api.applyDiscount(orderId: UUID(), type: .percentage, value: 10, reason: "Inconnue", operatorId: nil) }
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.totalTtcAmount == Money(cents: 1950) && after.globalDiscountType == nil && after.globalDiscountValue == 0)
    }

    @Test func removeDiscountRestoresTheTotal() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 50, reason: "Test", operatorId: nil)
        try await api.removeDiscount(orderId: order.orderId)
        let restored = try #require(try await api.activeOrder(table: "T1"))
        #expect(restored.totalTtcAmount == Money(cents: 1950) && restored.globalDiscountType == nil && restored.globalDiscountReason == nil)
        await #expect(throws: APIError.notFound("Commande introuvable.")) { try await api.removeDiscount(orderId: UUID()) }
    }

    @Test func compItemZeroesTheLine() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        let order = try await seat(api, "T1", [localInput(burger), localInput(cafe)])
        let burgerLine = try #require(order.lines.first { $0.productName == burger.name })
        try await api.compItem(orderId: order.orderId, lineId: burgerLine.lineId, reason: "Erreur de cuisine", operatorId: nil)
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.totalTtcAmount == Money(cents: 850))
        #expect(after.lines.first { $0.lineId == burgerLine.lineId }?.isComp == true)
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: order.orderId, lineId: burgerLine.lineId, reason: "  ", operatorId: nil) }
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: order.orderId, lineId: UUID(), reason: "Inconnue", operatorId: nil) }
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: UUID(), lineId: burgerLine.lineId, reason: "Inconnue", operatorId: nil) }
    }

    @Test func discountsAreAudited() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger, quantity: 2)])
        let operatorId = UUID()
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "  Geste commercial  ", operatorId: operatorId)
        let lineId = try #require(order.lines.first?.lineId)
        try await api.compItem(orderId: order.orderId, lineId: lineId, reason: "Erreur de cuisine", operatorId: operatorId)

        let reader = try SQLiteDatabase(path: path)
        let audits = try reader.query("SELECT * FROM OrderDiscountAudits ORDER BY rowid")
        #expect(audits.count == 2)
        #expect(audits[0].int("DiscountType") == 0 && audits[0].decimal("Value") == 10 && audits[0].int("AmountSaved") == 390)
        #expect(audits[0].string("Reason") == "Geste commercial" && audits[0].uuid("AuthorizedByOperatorId") == operatorId && audits[0].uuid("OrderItemId") == nil)
        #expect(audits[1].int("DiscountType") == 2 && audits[1].int("AmountSaved") == 3900 && audits[1].uuid("OrderItemId") == lineId)
        #expect(audits[1].string("Reason") == "Erreur de cuisine")
    }

    @Test func discountCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.applyDiscount(orderId: UUID(), type: .percentage, value: 10, reason: "x", operatorId: nil) }
        await #expect(throws: APIError.unauthorized) { try await api.removeDiscount(orderId: UUID()) }
        await #expect(throws: APIError.unauthorized) { try await api.compItem(orderId: UUID(), lineId: UUID(), reason: "x", operatorId: nil) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalDiscountTests 2>&1 | grep -E "✘|Test run" | head -5`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter les remises et gratuités**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Discounts.swift` :
```swift
import Foundation

extension LocalPosAPI {
    private static let discountFailed = APIError.server(status: 400, message: "Opération de remise échouée.")
    private static let compFailed = APIError.server(status: 400, message: "Opération de gratuité échouée.")

    /// Remise globale sur la commande. Refusée (même message générique que le serveur) si la valeur est négative, si un pourcentage dépasse
    /// 100, si le motif est vide ou si la commande est inconnue. Chaque remise est journalisée dans `OrderDiscountAudits`.
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws {
        try requireAuth()
        let trimmed = reason.trimmingCharacters(in: .whitespacesAndNewlines)
        guard value >= 0, !trimmed.isEmpty, !(type == .percentage && value > 100) else { throw Self.discountFailed }
        let orders = orderRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId) else { throw Self.discountFailed }
            let totals = OrderMath.totals(
                lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination,
                discount: GlobalDiscount(type: type, value: value, reason: trimmed)
            )
            _ = try orders.setDiscount(orderId: orderId, type: type, value: value, reason: trimmed)
            try orders.insertDiscountAudit(
                orderId: orderId, itemId: nil, type: type, value: value, saved: (totals.subtotalTtc - totals.totalTtc).clampedAtZero(),
                reason: trimmed, operatorId: operatorId ?? localNoOperator
            )
        }
    }

    public func removeDiscount(orderId: UUID) async throws {
        try requireAuth()
        // Écart assumé : commande inconnue → 404 (le .NET lève une exception non gérée).
        guard try orderRepository.setDiscount(orderId: orderId, type: nil, value: 0, reason: nil) else { throw APIError.notFound("Commande introuvable.") }
    }

    /// Offre une ligne (gratuité). L'économie journalisée est `prix unitaire × quantité`, hors options (comme `CompOrderItemAsync`).
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws {
        try requireAuth()
        let trimmed = reason.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw Self.compFailed }
        let orders = orderRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId), let line = order.lines.first(where: { $0.lineId == lineId }),
                  try orders.comp(lineId: lineId, orderId: orderId, reason: trimmed)
            else { throw Self.compFailed }
            try orders.insertDiscountAudit(
                orderId: orderId, itemId: lineId, type: .comp, value: 100, saved: line.unitPrice * line.quantity,
                reason: trimmed, operatorId: operatorId ?? localNoOperator
            )
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Remises et gratuités (retiré par la tâche 5)` : ce commentaire, les 3 lignes `applyDiscount`, `removeDiscount`, `compItem`, et la ligne vide qui suit.

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Imprimantes (plan 1c)
```
par :
```swift
    // MARK: Imprimantes (plan 1d)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Happy Hour (plan 1c)
```
par :
```swift
    // MARK: Happy Hour (plan 1d)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Réseau (plan 1c)
```
par :
```swift
    // MARK: Réseau (plan 1d)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Encaissement (plan 1b)
```
par :
```swift
    // MARK: Encaissement (plan 1c)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Comptoir & vente à emporter (plan 1b)
```
par :
```swift
    // MARK: Comptoir & vente à emporter (plan 1c)
```

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalDiscountTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 5: Mettre à jour `ios/README.md`**

Dans `ios/README.md`, section « Mode autonome (en cours) », remplacer le paragraphe qui décrit le plan livré et les plans suivants par :
```markdown
`LocalPosAPI` (`Packages/PosKit/Sources/PosKit/Local/`) implémente `PosAPI` sur une base SQLite locale, sans serveur .NET. Livré : authentification par PIN (avec verrouillage), personnel, catalogue, grille tactile, tables et réglages (plan 1a) ; commandes de salle, envoi cuisine et bons, remises et gratuités, transfert et fusion de tables (plan 1b). Les autres méthodes répondent `501` (« Non disponible en mode autonome ») jusqu'aux plans 1c (paiement non fiscal, comptoir, mise en attente, chambres d'hôtel) et 1d (imprimantes, Happy Hour, réseau, choix Serveur/Autonome au lancement).
```
Conserver la ligne suivante (commande de test) inchangée.

- [ ] **Step 6: Vérification finale, suivi, commit**

Run:
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"
cd ../../.. && dotnet build RestaurantPos.slnx 2>&1 | tail -3
```
Expected: `Test run with 240 tests … passed`, aucun avertissement ; `dotnet build` réussi (aucun fichier .NET modifié).

Mettre à jour `docs/plans/ipad-standalone-1b-progress.md` : tâche 5 `fait`, PosKit `240`, et lister les findings de revue différés connus.

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local ios/README.md docs/plans/ipad-standalone-1b-progress.md
git commit -m "feat(ios-local): remises et gratuités tracées ; plans suivants renumérotés (1c, 1d)"
```

---

## Self-Review

**1. Couverture de la spec (sous-projet 1)**
- « Mêmes tables et noms de colonnes que `Domain/Entities` » → migration v2 (tâche 1), 6 tables.
- Parcours de table complet du sous-projet 1 (ouvrir, saisir, envoyer en cuisine, remiser, transférer) → tâches 2 à 5 ; le paiement et le comptoir sont dans le plan 1c (annoncé).
- Montants en centimes, arrondi du .NET → `OrderMath` réutilisé, jamais de `Double` (tâches 2, 5).
- Migrations versionnées, échec explicite → `steps = [schemaV1, schemaV2]`, `userVersion` testé.
- Parité de messages avec le serveur → textes de `SharedResource.fr.resx` repris dans les tests.
- Écarts avec le .NET → listés dans « Décisions de conception » et couverts par un test chacun.

**2. Placeholders** : aucun « TBD »/« TODO » ; toutes les étapes de code contiennent le code complet ; les remplacements donnent le texte avant/après exact.

**3. Cohérence des types** : `orderRepository`/`kitchenRepository` (tâche 1) utilisés tels quels aux tâches 2 à 5 ; `LocalOrderRepository.setDiscount(orderId:type:value:reason:)` et `comp(lineId:orderId:reason:)` appelés avec les mêmes étiquettes ; `LocalFloorRepository.update(_:status:covers:waiterName:waiterId:activeOrderId:openedAt:)` appelé de façon identique aux tâches 2 et 4 ; helpers de test `localProduct`, `localInput`, `seatTable` définis en tâche 2 et réutilisés en 3 à 5 ; `temporaryDatabasePath()` vient du plan 1a.

**4. Review Focus** : les cinq points ont chacun un test nommé dans la tâche propriétaire (tâches 2, 3, 4, 5).

**5. Lacunes connues, hors test** : expiration du verrouillage PIN (plan 1a) ; atomicité sous panne disque réelle (couverte par les transactions, non simulée) ; normalisation `T01 ↔ T1` non portée.

## Execution Handoff

Plan à relire avant toute exécution. Vérification prévue à la rédaction : les blocs « Créer / remplacer / supprimer » de ce document sont appliqués mécaniquement, tâche par tâche, sur une copie propre de PosKit, et `swift test` doit donner exactement 208, 219, 227, 233 puis 240 tests verts, sans avertissement.
