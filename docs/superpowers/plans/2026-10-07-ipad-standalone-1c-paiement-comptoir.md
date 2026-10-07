# iPad standalone — plan 1c : paiement non fiscal, comptoir, mise en attente, chambres d'hôtel (`LocalPosAPI`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `LocalPosAPI` encaisse sur SQLite : règlement d'une table (y compris en plusieurs fois), vente au comptoir et à emporter (numéro de retrait, titres-restaurant, avoir), mise en attente et rappel d'un panier, facturation sur une chambre d'hôtel. Aucun reçu fiscal : l'encaissement est tracé dans une table provisoire, remplacée au sous-projet 2.

**Architecture:** Migration v3 (tables `HeldOrders`, `CustomerCreditVouchers`, `HotelRooms`, `RoomFolioCharges`, mêmes noms et colonnes que `AppDbContext`, plus deux tables propres à l'iPad : `NonFiscalPayments` et `LocalCounters`). Un règlement partagé (`settle`) sert `pay` et `counterCheckout`. Chaque opération multi-tables s'exécute dans une seule transaction ; toutes les validations passent **avant** la première écriture. Le PIN superviseur partage le compteur d'échecs de la connexion.

**Tech Stack:** Swift 6, `SQLite3`, Swift Testing, SwiftPM (`ios/Packages/PosKit`). Aucune dépendance nouvelle.

**Spec:** `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md` (sous-projet 1 « Socle local »). Suite du plan 1b (`docs/superpowers/plans/2026-10-07-ipad-standalone-1b-commandes-cuisine.md`, branche `feat/ipad-standalone-1b`, PR #9). **Ce plan se base sur la branche du plan 1b** (ses tests et fichiers y sont déjà présents). Sous-projet 1 :
- **1a** (fait) : SQLite, schéma v1, auth, personnel, catalogue, grille, tables, réglages.
- **1b** (fait, PR #9) : commandes de salle, cuisine, remises, transferts.
- **1c (ce plan)** : paiement non fiscal, comptoir, mise en attente, avoirs titres-restaurant, chambres d'hôtel.
- **1d** : imprimantes (configuration), Happy Hour, réseau, choix « Serveur / Autonome » au lancement, identité de terminal `T01`.

## Global Constraints

- `PosKit` reste **sans dépendance externe** : uniquement `Foundation`, `SQLite3`, `CryptoKit`.
- Swift 6 (`swift-tools-version: 6.0`), plateformes `.iOS(.v17)` et `.macOS(.v14)` ; aucun avertissement de compilation.
- Montants en **centimes entiers** (`Money`), jamais de `Double`/`Float` ; totaux via `OrderMath` ; taux en `Decimal`, stockés en texte.
- Tables et colonnes nommées comme dans `AppDbContext` (sauf les deux tables propres à l'iPad, signalées) ; Guid = `TEXT` en majuscules ; `DateTimeOffset` = `TEXT` ISO 8601 UTC ; enum = `INTEGER` ; `Money` = `INTEGER` (les soldes de chambre sont des `decimal` en euros dans le .NET, stockés en `TEXT`).
- Migrations versionnées par `PRAGMA user_version`, un script publié n'est jamais modifié (on ajoute le script v3).
- Messages d'erreur identiques au serveur (français, `SharedResource.fr.resx`).
- Une opération qui touche plusieurs tables s'exécute dans **une seule** `db.transaction` (non ré-entrante) ; les refus se décident avant la première écriture.
- Types préfixés `Local…` pour éviter les collisions avec SwiftUI/Foundation.
- Commentaires et chaînes en français ; identifiants de code en anglais ; tests Swift Testing.
- Ne pas toucher à `HTTPPosAPI`, `InMemoryPosAPI`, aux stores ni aux vues.

## Décisions de conception (écarts assumés vis-à-vis du serveur .NET)

Le mode autonome reste « non fiscal » tant que le sous-projet 2 n'est pas livré. Là où le .NET peut perdre de l'argent ou une vente, on refuse (l'iPad est seul dépositaire des données). Chaque écart est testé.

1. **Paiements non fiscaux** : table provisoire `NonFiscalPayments` (hors .NET), remplacée au sous-projet 2 par `FiscalReceipts`/`PaymentTenders` signés. Numéro de reçu `NF-{terminal}-{n°}` pour ne jamais se confondre avec un reçu fiscal ; pas de signature (`fiscalSignature` nul), `printQueued` toujours faux (impression : sous-projet 4).
2. **Paiement refusé** (`400`, `Échec de l'encaissement.`) si la liste des règlements est vide, si un montant est ≤ 0, si le total dépasse le solde dû, ou si la commande est déjà réglée ou annulée. Le .NET accepte tout cela.
3. **Pas de « vente fantôme »** : une commande introuvable est refusée (`Commande introuvable pour ce règlement.`), le .NET fabrique une vente d'un article inventé.
4. **Pourboire** : refusé hors du paiement qui solde la note ; un paiement refusé ne laisse aucun pourboire (une seule transaction).
5. **Titres-restaurant** validés avant toute écriture : un refus ne consomme pas de numéro de retrait ni d'avoir (le .NET attribue le numéro d'abord).
6. **Rappel d'une commande en attente** : remplace un panier de comptoir vide (qui est annulé), refuse (`409`) si le comptoir contient déjà une vente (le .NET écrase le panier).
7. **Mise en attente** refusée si la commande est déjà en attente ; annuler une commande en attente annule aussi la commande (le .NET la laisse ouverte pour toujours).
8. **PIN superviseur** : les échecs comptent dans le même verrouillage que la connexion (5 échecs en 1 min bloquent 30 s).
9. **Facturation chambre** : le montant doit égaler le solde de la commande ; la commande est ensuite clôturée et la table libérée (le .NET libère la table mais laisse la commande ouverte).
10. **Garde de statut** (condition d'entrée du plan 1b) : remise, retrait de remise, gratuité et changement de destination sont refusés (`409`, `Commande déjà réglée ou annulée.`) sur une commande réglée ou annulée.
11. Non portés : journal fiscal (JET) de l'annulation d'une mise en attente (sous-projet 2), impression, SignalR, normalisation `T01 ↔ T1`.

## Review Focus

Entrées ou pannes que la spec implique mais qu'aucun test nominal n'exerce ; chacune a son test dans la tâche indiquée.

1. **Règlement hors bornes** (montant nul/négatif/supérieur au solde, liste vide, commande déjà réglée) → refus sans aucune écriture → `badTendersAreRefusedWithoutWriting`, `aPaidOrderCannotBePaidAgain` (tâche 3).
2. **Pourboire sur un paiement partiel** refusé et rien d'enregistré → `tipIsOnlyAcceptedOnTheFinalPayment` (tâche 3).
3. **Titre-restaurant refusé** (plafond légal, surpaiement strict) : aucun numéro de retrait consommé, aucun avoir émis → `mealVoucherAboveTheLegalCapIsRefusedWithoutConsumingAPickupNumber` (tâche 5).
4. **PIN superviseur deviné par force brute** bloque aussi la connexion ; **rappel sur un comptoir occupé** ne perd pas la commande en attente → `wrongSupervisorPinsLockLoginToo`, `recallReplacesAnEmptyCounterButRefusesABusyOne` (tâche 4).
5. **Facturation chambre refusée** (plafond, chambre inconnue, montant différent du solde) laisse solde et commande intacts → `chargeRoomRefusalsLeaveBalanceAndOrderUntouched` (tâche 6).

## File Structure

Tout est sous `ios/Packages/PosKit/`.

| Fichier | Responsabilité |
|---|---|
| `Sources/PosKit/Local/LocalSchemaV3.swift` | Script de la migration v3. |
| `Sources/PosKit/Local/LocalMigrator.swift` | (modifié) ajoute `schemaV3` à `steps`. |
| `Sources/PosKit/Local/LocalOrderRepository+Payments.swift` | `LocalOrderStatus.isModifiable`, `APIError.localOrderClosed`, statut, pourboire, retrait, lignes éligibles titre-restaurant. |
| `Sources/PosKit/Local/LocalPaymentRepository.swift` | Règlements, compteurs (reçus, numéros de retrait), avoirs. |
| `Sources/PosKit/Local/LocalHoldRepository.swift` | `HeldOrders`. |
| `Sources/PosKit/Local/LocalHotelRepository.swift` | `HotelRooms`, `RoomFolioCharges`. |
| `Sources/PosKit/Local/LocalPosAPI.swift` | (modifié) accesseurs ; compteur d'échecs de PIN partagé. |
| `Sources/PosKit/Local/LocalPosAPI+Payments.swift` | Règlement partagé (`settle`), clôture d'une commande, `pay`. |
| `Sources/PosKit/Local/LocalPosAPI+Counter.swift` | `openCounterOrder`, `holdOrder`, `heldOrders`, `recallHeldOrder`, `voidHeldOrder`. |
| `Sources/PosKit/Local/LocalPosAPI+CounterCheckout.swift` | `LocalMealVoucher` et `counterCheckout`. |
| `Sources/PosKit/Local/LocalPosAPI+Hotel.swift` | `hotelRooms`, `chargeRoom`. |
| `Sources/PosKit/Local/LocalPosAPI+Discounts.swift`, `+Orders.swift` | (modifiés) garde de statut. |
| `Sources/PosKit/Local/LocalSeeder.swift` | (modifié) chambres de démonstration. |
| `Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` | (modifié) retire les stubs portés. |
| `Tests/PosKitTests/Local/*.swift` | Un fichier de tests par tâche. |
| `docs/plans/ipad-standalone-1c-progress.md` | Suivi d'exécution (exigé par `CLAUDE.md`). |

## Conventions pour toutes les tâches

- Les commandes `swift` se lancent depuis `ios/Packages/PosKit`. **`swift test --filter` porte sur les noms de types Swift** (ex. `--filter LocalPaymentTests`). La suite complète (`swift test`) fait foi.
- Base de référence avant ce plan (branche du plan 1b) : **244 tests PosKit passent**. Totaux attendus cumulés à la fin de chaque tâche : T1 251 · T2 252 · T3 259 · T4 266 · T5 273 · T6 277.
- Chaque tâche se termine par : suite PosKit complète verte, mise à jour de `docs/plans/ipad-standalone-1c-progress.md`, commit.
- Commits au style du dépôt : `feat(ios-local): …`, en français, avec le trailer de co-signature de la session.

---

### Task 1: Migration v3 et dépôts (règlements, attente, chambres)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV3.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalOrderRepository+Payments.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPaymentRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalHoldRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalHotelRepository.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentStoreTests.swift`
- Create: `docs/plans/ipad-standalone-1c-progress.md`

**Interfaces:**
- Consumes (plans 1a/1b) : `SQLiteDatabase`, `SQLRow`, `SQLValue`, `LocalMigrator.steps`, `LocalOrderRepository`, `LocalOrderStatus`, `LocalFloorRepository`, modèles `HeldOrder`, `HotelRoom`, `PaymentMethod`, `OrderDestination`, `Money`, `ActiveOrder`, `OrderLine`.
- Produces :
  - `extension LocalOrderStatus { var isModifiable: Bool }`, `extension APIError { static let localOrderClosed: APIError }` (`409`, `Commande déjà réglée ou annulée.`).
  - `extension LocalOrderRepository { status(of:) -> LocalOrderStatus?; tipCents(of:) -> Int; setTip(orderId:cents:); setPickup(orderId:number:buzzer:destination:); foodVoucherEligibleLineIds(orderId:) -> Set<UUID> }`.
  - `struct LocalPaymentRepository { let db; paidCents(orderId:) -> Int; insertTender(orderId:terminalId:receiptNumber:method:amount:tendered:change:); nextReceiptNumber(terminalId:) -> String; nextPickupNumber(terminalId:now:) -> String; static pickupPrefix(for:) -> String; insertCreditVoucher(code:orderId:terminalId:amount:expiresAt:) }`.
  - `struct LocalHoldRepository { let db; insert(orderId:terminalId:label:destination:itemCount:total:snapshot:staffId:) -> UUID; active(terminalId:) -> [HeldOrder]; hasActiveHold(orderId:) -> Bool; activeOrderId(holdId:) -> UUID?; markRecalled(id:) -> Bool; markVoided(id:staffId:reason:) -> Bool }`.
  - `struct LocalHotelRepository { let db; insert(_ room: HotelRoom, checkIn:checkOut:); occupiedRooms() -> [HotelRoom]; occupiedRoom(_:) -> HotelRoom?; addToBalance(roomNumber:_:); insertCharge(orderId:roomNumber:guestName:amount:tip:signature:notes:) }`.
  - `LocalPosAPI.paymentRepository`, `.holdRepository`, `.hotelRepository`.

- [ ] **Step 0: Relever la base de référence et créer le fichier de suivi**

Run (depuis la racine du dépôt, sur la branche du plan 1b) :
```bash
git branch --show-current
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run"
```
Expected: `feat/ipad-standalone-1b` ; `Test run with 244 tests … passed`. Créer ensuite la branche de travail : `git switch -c feat/ipad-standalone-1c`.

Créer `docs/plans/ipad-standalone-1c-progress.md` :
```markdown
# iPad standalone 1c — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-07-ipad-standalone-1c-paiement-comptoir.md`

Base de référence (avant tâche 1) : PosKit 244 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v3 et dépôts | à faire | | |
| 2 Garde de statut et PIN partagé | à faire | | |
| 3 Paiement des tables | à faire | | |
| 4 Comptoir et mise en attente | à faire | | |
| 5 Encaissement comptoir | à faire | | |
| 6 Chambres d'hôtel | à faire | | |
```

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentStoreTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v3 et dépôts paiement / attente / chambres")
struct LocalPaymentStoreTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    @Test func migrationV3CreatesTheTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 3)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["HeldOrders", "CustomerCreditVouchers", "HotelRooms", "RoomFolioCharges", "NonFiscalPayments", "LocalCounters"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func paymentRepositoryTracksPaidAmountAndReceiptNumbers() throws {
        let db = try makeDB()
        let orders = LocalOrderRepository(db: db), payments = LocalPaymentRepository(db: db)
        let order = ActiveOrder(tableNumber: "T1", destination: .eatIn)
        try orders.insert(order, operatorId: localNoOperator, status: .open)
        #expect(try payments.paidCents(orderId: order.orderId) == 0)
        try payments.insertTender(orderId: order.orderId, terminalId: "T01", receiptNumber: "NF-T01-000001", method: .cash, amount: 1000, tendered: 1200, change: 200)
        try payments.insertTender(orderId: order.orderId, terminalId: "T01", receiptNumber: "NF-T01-000001", method: .creditCard, amount: 500, tendered: 500, change: 0)
        #expect(try payments.paidCents(orderId: order.orderId) == 1500)

        #expect(try payments.nextReceiptNumber(terminalId: "T01") == "NF-T01-000001")
        #expect(try payments.nextReceiptNumber(terminalId: "T01") == "NF-T01-000002")
        #expect(try payments.nextReceiptNumber(terminalId: "T02") == "NF-T02-000001")

        try db.run("DELETE FROM Orders WHERE Id = ?", [.uuid(order.orderId)])
        #expect(try payments.paidCents(orderId: order.orderId) == 0)
    }

    @Test func pickupNumbersWrapAtNinetyNineAndResetEachDay() throws {
        let db = try makeDB()
        let payments = LocalPaymentRepository(db: db)
        let day = Date(timeIntervalSince1970: 1_790_000_000)
        #expect(try (1...3).map { _ in try payments.nextPickupNumber(terminalId: "T01", now: day) } == ["#A-01", "#A-02", "#A-03"])
        #expect(try payments.nextPickupNumber(terminalId: "T01", now: day.addingTimeInterval(86_400)) == "#A-01")
        // Bouclage à 99 : après #A-99 on repart à #A-01.
        let other = Date(timeIntervalSince1970: 1_800_000_000)
        var last = ""
        for _ in 1...100 { last = try payments.nextPickupNumber(terminalId: "T01", now: other) }
        #expect(last == "#A-01")
    }

    @Test func pickupPrefixFollowsTheTerminal() {
        let cases: [(String, String)] = [("", "A"), ("T01", "A"), ("MAIN", "A"), ("T02", "B"), ("T03", "C"), ("T04", "D"), ("POS-B", "B"), ("pos03", "C")]
        for (terminal, prefix) in cases { #expect(LocalPaymentRepository.pickupPrefix(for: terminal) == prefix) }
    }

    @Test func holdRepositoryRoundTripsAndFiltersByTerminal() throws {
        let db = try makeDB()
        let holds = LocalHoldRepository(db: db)
        let first = UUID(), second = UUID(), staff = UUID()
        let holdA = try holds.insert(orderId: first, terminalId: "T01", label: "Dupont", destination: .takeaway, itemCount: 3, total: Money(cents: 4500), snapshot: "{}", staffId: staff)
        let holdB = try holds.insert(orderId: second, terminalId: "T02", label: nil, destination: .eatIn, itemCount: 1, total: Money(cents: 900), snapshot: "{}", staffId: staff)
        let t01 = try holds.active(terminalId: "T01")
        #expect(t01.count == 1 && t01[0].holdId == holdA && t01[0].orderId == first && t01[0].customerLabel == "Dupont")
        #expect(t01[0].itemCount == 3 && t01[0].totalTtc.amountInCents == 4500 && t01[0].destination == .takeaway)
        #expect(try holds.active(terminalId: "").count == 2)
        #expect(try holds.hasActiveHold(orderId: first) && !(try holds.hasActiveHold(orderId: UUID())))
        #expect(try holds.activeOrderId(holdId: holdB) == second)

        #expect(try holds.markRecalled(id: holdA))
        #expect(try holds.markRecalled(id: holdA) == false)
        #expect(try holds.activeOrderId(holdId: holdA) == nil)
        #expect(try holds.markVoided(id: holdB, staffId: staff, reason: "Erreur"))
        #expect(try holds.active(terminalId: "").isEmpty)
        #expect(try holds.markVoided(id: holdB, staffId: staff, reason: "Encore") == false)
    }

    @Test func hotelRepositoryRoundTripsRoomsAndBalance() throws {
        let db = try makeDB()
        let hotel = LocalHotelRepository(db: db)
        let now = Date()
        try hotel.insert(HotelRoom(roomNumber: "204", guestName: "Alexandre", maxCreditLimit: Money(cents: 60000)), checkIn: now, checkOut: now.addingTimeInterval(86_400))
        try hotel.insert(HotelRoom(roomNumber: "101", guestName: "Jean", maxCreditLimit: Money(cents: 30000), currentBalance: Money(cents: 1250)), checkIn: now, checkOut: now.addingTimeInterval(86_400))
        try db.run("INSERT INTO HotelRooms (Id, RoomNumber, GuestName, CheckInDateUtc, CheckOutDateUtc, IsOccupied, MaxCreditLimit, CurrentBalance) VALUES (?, '999', 'Parti', ?, ?, 0, '100', '0')", [.uuid(UUID()), .date(now), .date(now)])

        #expect(try hotel.occupiedRooms().map(\.roomNumber) == ["101", "204"])
        let room = try #require(try hotel.occupiedRoom(" 101 "))
        #expect(room.guestName == "Jean" && room.maxCreditLimit == Money(cents: 30000) && room.currentBalance == Money(cents: 1250))
        #expect(try hotel.occupiedRoom("999") == nil)
        #expect(try hotel.occupiedRoom("404") == nil)

        try hotel.addToBalance(roomNumber: "101", Money(cents: 2050))
        #expect(try hotel.occupiedRoom("101")?.currentBalance == Money(cents: 3300))
        try hotel.insertCharge(orderId: UUID(), roomNumber: "101", guestName: "Jean", amount: Money(cents: 1950), tip: Money(cents: 100), signature: nil, notes: "RAS")
        #expect(try db.query("SELECT * FROM RoomFolioCharges").count == 1)
    }

    @Test func orderRepositoryTracksStatusTipAndPickup() throws {
        let db = try makeDB()
        let orders = LocalOrderRepository(db: db)
        let order = ActiveOrder(tableNumber: "Comptoir", destination: .takeaway)
        try orders.insert(order, operatorId: localNoOperator, status: .open)
        #expect(try orders.status(of: order.orderId) == .open && LocalOrderStatus.open.isModifiable)
        #expect(try orders.status(of: UUID()) == nil)
        try orders.setStatus(orderId: order.orderId, .paid)
        #expect(try orders.status(of: order.orderId) == .paid && !LocalOrderStatus.paid.isModifiable && !LocalOrderStatus.cancelled.isModifiable)

        #expect(try orders.tipCents(of: order.orderId) == 0)
        try orders.setTip(orderId: order.orderId, cents: 250)
        #expect(try orders.tipCents(of: order.orderId) == 250)

        try orders.setPickup(orderId: order.orderId, number: "#B-07", buzzer: "12", destination: .eatIn)
        let fetched = try #require(try orders.order(id: order.orderId))
        #expect(fetched.pickupNumber == "#B-07" && fetched.pickupBuzzer == "12" && fetched.destination == .eatIn)

        let eligible = OrderLine(productId: UUID(), productName: "A", quantity: 1, unitPrice: Money(cents: 100), taxRatePercent: 10)
        let other = OrderLine(productId: UUID(), productName: "B", quantity: 1, unitPrice: Money(cents: 100), taxRatePercent: 10)
        try orders.insert(eligible, orderId: order.orderId)
        try orders.insert(other, orderId: order.orderId)
        try db.run("UPDATE OrderItems SET IsFoodVoucherEligible = 0 WHERE Id = ?", [.uuid(other.lineId)])
        #expect(try orders.foodVoucherEligibleLineIds(orderId: order.orderId) == [eligible.lineId])
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPaymentStoreTests 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalPaymentRepository' in scope`.

- [ ] **Step 3: Écrire la migration v3**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV3.swift` :
```swift
import Foundation

extension LocalMigrator {
    /// Mise en attente, avoirs, chambres d'hôtel (mêmes tables et colonnes que `AppDbContext`) et deux tables propres à l'iPad :
    /// `NonFiscalPayments` (règlements provisoires, remplacés par `FiscalReceipts`/`PaymentTenders` au sous-projet 2) et
    /// `LocalCounters` (numéros de reçu et de retrait, qui sont en mémoire côté .NET).
    static let schemaV3 = """
    CREATE TABLE HeldOrders (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        CustomerLabel TEXT,
        Destination INTEGER NOT NULL,
        ItemCount INTEGER NOT NULL,
        TotalTtc INTEGER NOT NULL,
        OrderSnapshotJson TEXT NOT NULL,
        HeldAtUtc TEXT NOT NULL,
        HeldByStaffId TEXT NOT NULL,
        IsRecalled INTEGER NOT NULL,
        RecalledAtUtc TEXT,
        IsVoided INTEGER NOT NULL,
        VoidedAtUtc TEXT,
        VoidReason TEXT,
        VoidedByStaffId TEXT
    );
    CREATE INDEX IX_HeldOrders_TerminalId_IsRecalled_IsVoided ON HeldOrders (TerminalId, IsRecalled, IsVoided);

    CREATE TABLE CustomerCreditVouchers (
        Id TEXT NOT NULL PRIMARY KEY,
        VoucherCode TEXT NOT NULL,
        OriginalOrderId TEXT NOT NULL,
        TerminalId TEXT NOT NULL,
        Amount INTEGER NOT NULL,
        IssuedAtUtc TEXT NOT NULL,
        ExpiresAtUtc TEXT NOT NULL,
        IsRedeemed INTEGER NOT NULL,
        RedeemedAtUtc TEXT,
        RedeemedOrderId TEXT
    );
    CREATE UNIQUE INDEX IX_CustomerCreditVouchers_VoucherCode ON CustomerCreditVouchers (VoucherCode);

    CREATE TABLE HotelRooms (
        Id TEXT NOT NULL PRIMARY KEY,
        RoomNumber TEXT NOT NULL,
        GuestName TEXT NOT NULL,
        CheckInDateUtc TEXT NOT NULL,
        CheckOutDateUtc TEXT NOT NULL,
        IsOccupied INTEGER NOT NULL,
        MaxCreditLimit TEXT NOT NULL,
        CurrentBalance TEXT NOT NULL
    );
    CREATE INDEX IX_HotelRooms_RoomNumber ON HotelRooms (RoomNumber);

    CREATE TABLE RoomFolioCharges (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        RoomNumber TEXT NOT NULL,
        GuestName TEXT NOT NULL,
        Amount INTEGER NOT NULL,
        TipAmount INTEGER NOT NULL,
        SignatureDataUrl TEXT,
        Notes TEXT,
        ChargedAtUtc TEXT NOT NULL
    );

    CREATE TABLE NonFiscalPayments (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL REFERENCES Orders (Id) ON DELETE CASCADE,
        TerminalId TEXT NOT NULL,
        ReceiptNumber TEXT NOT NULL,
        Method INTEGER NOT NULL,
        Amount INTEGER NOT NULL,
        Tendered INTEGER NOT NULL,
        ChangeGiven INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_NonFiscalPayments_OrderId ON NonFiscalPayments (OrderId);

    CREATE TABLE LocalCounters (
        Name TEXT NOT NULL PRIMARY KEY,
        Day TEXT NOT NULL,
        LastSequence INTEGER NOT NULL
    );
    """
}
```

Dans `LocalMigrator.swift`, remplacer :
```swift
    static let steps: [String] = [schemaV1, schemaV2]
```
par :
```swift
    static let steps: [String] = [schemaV1, schemaV2, schemaV3]
```

- [ ] **Step 4: Statut, pourboire et retrait sur les commandes**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalOrderRepository+Payments.swift` :
```swift
import Foundation

extension LocalOrderStatus {
    /// Une commande ouverte, envoyée en cuisine ou en attente d'addition peut encore être modifiée ; réglée ou annulée, non.
    var isModifiable: Bool { self == .open || self == .sentToKitchen || self == .billRequested }
}

extension APIError {
    /// Opération refusée parce que la commande est déjà réglée ou annulée.
    static let localOrderClosed = APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")
}

extension LocalOrderRepository {
    func status(of id: UUID) throws -> LocalOrderStatus? {
        try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(id)]).first?.int("Status").flatMap { LocalOrderStatus(rawValue: $0) }
    }

    func tipCents(of id: UUID) throws -> Int {
        try db.query("SELECT TipAmount FROM Orders WHERE Id = ?", [.uuid(id)]).first?.int("TipAmount") ?? 0
    }

    func setTip(orderId: UUID, cents: Int) throws {
        try db.run("UPDATE Orders SET TipAmount = ? WHERE Id = ?", [.integer(cents), .uuid(orderId)])
    }

    /// Numéro de retrait, bip et destination d'une vente au comptoir.
    func setPickup(orderId: UUID, number: String, buzzer: String?, destination: OrderDestination) throws {
        try db.run(
            "UPDATE Orders SET PickupNumber = ?, PickupBuzzer = ?, Destination = ? WHERE Id = ?",
            [.text(number), .string(buzzer), .integer(destination.rawValue), .uuid(orderId)]
        )
    }

    /// Lignes payables en titres-restaurant (`OrderItems.IsFoodVoucherEligible`).
    func foodVoucherEligibleLineIds(orderId: UUID) throws -> Set<UUID> {
        Set(try db.query("SELECT Id FROM OrderItems WHERE OrderId = ? AND IsFoodVoucherEligible = 1", [.uuid(orderId)]).compactMap { $0.uuid("Id") })
    }
}
```

- [ ] **Step 5: Dépôt des règlements, compteurs et avoirs**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPaymentRepository.swift` :
```swift
import Foundation

struct LocalPaymentRepository {
    let db: SQLiteDatabase

    /// Total déjà réglé sur la commande (tous règlements confondus).
    func paidCents(orderId: UUID) throws -> Int {
        try db.query("SELECT COALESCE(SUM(Amount), 0) AS total FROM NonFiscalPayments WHERE OrderId = ?", [.uuid(orderId)]).first?.int("total") ?? 0
    }

    func insertTender(orderId: UUID, terminalId: String, receiptNumber: String, method: PaymentMethod, amount: Int, tendered: Int, change: Int) throws {
        try db.run(
            """
            INSERT INTO NonFiscalPayments (Id, OrderId, TerminalId, ReceiptNumber, Method, Amount, Tendered, ChangeGiven, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .uuid(orderId), .text(terminalId), .text(receiptNumber), .integer(method.rawValue), .integer(amount),
             .integer(tendered), .integer(change), .date(Date())]
        )
    }

    /// `NF-{terminal}-{n°}` : le préfixe `NF` distingue un reçu non fiscal d'un futur reçu fiscal.
    func nextReceiptNumber(terminalId: String) throws -> String {
        let sequence = try advanceCounter(name: "receipt:\(terminalId)", day: "", wrapAt: nil)
        return "NF-\(terminalId)-\(String(format: "%06d", sequence))"
    }

    /// `#A-01` … `#A-99` puis retour à `#A-01`, remis à zéro chaque jour UTC (comme `TakeawayCounterService`).
    func nextPickupNumber(terminalId: String, now: Date) throws -> String {
        let prefix = Self.pickupPrefix(for: terminalId)
        let day = String(SQLDate.format(now).prefix(10))
        let sequence = try advanceCounter(name: "pickup:\(prefix)", day: day, wrapAt: 99)
        return "#\(prefix)-\(String(format: "%02d", sequence))"
    }

    /// Lettre dérivée du terminal (`TakeawayCounterService.DeriveTerminalPrefix`).
    static func pickupPrefix(for terminalId: String) -> String {
        let clean = terminalId.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if clean.isEmpty { return "A" }
        if clean.hasSuffix("B") || clean.contains("POS-B") || clean.contains("POS02") || clean.contains("2") { return "B" }
        if clean.hasSuffix("C") || clean.contains("POS-C") || clean.contains("POS03") || clean.contains("3") { return "C" }
        if clean.hasSuffix("D") || clean.contains("POS-D") || clean.contains("POS04") || clean.contains("4") { return "D" }
        return "A"
    }

    func insertCreditVoucher(code: String, orderId: UUID, terminalId: String, amount: Money, expiresAt: Date) throws {
        try db.run(
            """
            INSERT INTO CustomerCreditVouchers (Id, VoucherCode, OriginalOrderId, TerminalId, Amount, IssuedAtUtc, ExpiresAtUtc, IsRedeemed, RedeemedAtUtc, RedeemedOrderId)
            VALUES (?, ?, ?, ?, ?, ?, ?, 0, NULL, NULL)
            """,
            [.uuid(UUID()), .text(code), .uuid(orderId), .text(terminalId), .integer(amount.cents), .date(Date()), .date(expiresAt)]
        )
    }

    /// Incrémente un compteur persistant. `day` change → repart de zéro ; `wrapAt` atteint → repart à 1.
    private func advanceCounter(name: String, day: String, wrapAt: Int?) throws -> Int {
        let row = try db.query("SELECT Day, LastSequence FROM LocalCounters WHERE Name = ?", [.text(name)]).first
        let current = row?.string("Day") == day ? (row?.int("LastSequence") ?? 0) : 0
        var next = current + 1
        if let wrapAt, current >= wrapAt { next = 1 }
        try db.run(
            "INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES (?, ?, ?) ON CONFLICT(Name) DO UPDATE SET Day = excluded.Day, LastSequence = excluded.LastSequence",
            [.text(name), .text(day), .integer(next)]
        )
        return next
    }
}
```

- [ ] **Step 6: Dépôts de la mise en attente et des chambres**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalHoldRepository.swift` :
```swift
import Foundation

struct LocalHoldRepository {
    let db: SQLiteDatabase

    @discardableResult
    func insert(orderId: UUID, terminalId: String, label: String?, destination: OrderDestination, itemCount: Int, total: Money, snapshot: String, staffId: UUID) throws -> UUID {
        let id = UUID()
        try db.run(
            """
            INSERT INTO HeldOrders (Id, TerminalId, OrderId, CustomerLabel, Destination, ItemCount, TotalTtc, OrderSnapshotJson, HeldAtUtc, HeldByStaffId,
                IsRecalled, RecalledAtUtc, IsVoided, VoidedAtUtc, VoidReason, VoidedByStaffId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, NULL, 0, NULL, NULL, NULL)
            """,
            [.uuid(id), .text(terminalId), .uuid(orderId), .string(label), .integer(destination.rawValue), .integer(itemCount), .integer(total.cents),
             .text(snapshot), .date(Date()), .uuid(staffId)]
        )
        return id
    }

    /// Mises en attente non rappelées ni annulées, la plus récente d'abord. `terminalId` vide = tous les terminaux.
    func active(terminalId: String) throws -> [HeldOrder] {
        let rows: [SQLRow]
        if terminalId.isEmpty {
            rows = try db.query("SELECT * FROM HeldOrders WHERE IsRecalled = 0 AND IsVoided = 0 ORDER BY HeldAtUtc DESC, rowid DESC")
        } else {
            rows = try db.query("SELECT * FROM HeldOrders WHERE IsRecalled = 0 AND IsVoided = 0 AND TerminalId = ? ORDER BY HeldAtUtc DESC, rowid DESC", [.text(terminalId)])
        }
        return rows.map {
            HeldOrder(
                holdId: $0.uuid("Id")!, terminalId: $0.string("TerminalId"), orderId: $0.uuid("OrderId")!, customerLabel: $0.string("CustomerLabel"),
                destination: OrderDestination(rawValue: $0.int("Destination") ?? 0) ?? .takeaway, itemCount: $0.int("ItemCount") ?? 0,
                totalTtc: Money(cents: $0.int("TotalTtc") ?? 0), heldAtUtc: $0.date("HeldAtUtc") ?? Date()
            )
        }
    }

    func hasActiveHold(orderId: UUID) throws -> Bool {
        try !db.query("SELECT 1 AS x FROM HeldOrders WHERE OrderId = ? AND IsRecalled = 0 AND IsVoided = 0", [.uuid(orderId)]).isEmpty
    }

    /// Commande d'une mise en attente encore active, sinon `nil`.
    func activeOrderId(holdId: UUID) throws -> UUID? {
        try db.query("SELECT OrderId FROM HeldOrders WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0", [.uuid(holdId)]).first?.uuid("OrderId")
    }

    func markRecalled(id: UUID) throws -> Bool {
        try db.run("UPDATE HeldOrders SET IsRecalled = 1, RecalledAtUtc = ? WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0", [.date(Date()), .uuid(id)]) > 0
    }

    func markVoided(id: UUID, staffId: UUID, reason: String) throws -> Bool {
        try db.run(
            "UPDATE HeldOrders SET IsVoided = 1, VoidedAtUtc = ?, VoidedByStaffId = ?, VoidReason = ? WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0",
            [.date(Date()), .uuid(staffId), .text(reason), .uuid(id)]
        ) > 0
    }
}
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalHotelRepository.swift` :
```swift
import Foundation

struct LocalHotelRepository {
    let db: SQLiteDatabase

    func insert(_ room: HotelRoom, checkIn: Date, checkOut: Date) throws {
        try db.run(
            """
            INSERT INTO HotelRooms (Id, RoomNumber, GuestName, CheckInDateUtc, CheckOutDateUtc, IsOccupied, MaxCreditLimit, CurrentBalance)
            VALUES (?, ?, ?, ?, ?, 1, ?, ?)
            """,
            [.uuid(UUID()), .text(room.roomNumber), .text(room.guestName), .date(checkIn), .date(checkOut),
             .decimal(room.maxCreditLimit.euros), .decimal(room.currentBalance.euros)]
        )
    }

    func occupiedRooms() throws -> [HotelRoom] {
        try db.query("SELECT * FROM HotelRooms WHERE IsOccupied = 1 ORDER BY RoomNumber").map(Self.decode)
    }

    func occupiedRoom(_ number: String) throws -> HotelRoom? {
        let trimmed = number.trimmingCharacters(in: .whitespacesAndNewlines)
        return try db.query("SELECT * FROM HotelRooms WHERE RoomNumber = ? AND IsOccupied = 1", [.text(trimmed)]).first.map(Self.decode)
    }

    func addToBalance(roomNumber: String, _ amount: Money) throws {
        let trimmed = roomNumber.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let row = try db.query("SELECT CurrentBalance FROM HotelRooms WHERE RoomNumber = ? AND IsOccupied = 1", [.text(trimmed)]).first else {
            throw SQLiteError(code: -1, message: "Chambre \(trimmed) introuvable")
        }
        let balance = (row.decimal("CurrentBalance") ?? 0) + amount.euros
        try db.run("UPDATE HotelRooms SET CurrentBalance = ? WHERE RoomNumber = ? AND IsOccupied = 1", [.decimal(balance), .text(trimmed)])
    }

    func insertCharge(orderId: UUID, roomNumber: String, guestName: String, amount: Money, tip: Money, signature: String?, notes: String?) throws {
        try db.run(
            """
            INSERT INTO RoomFolioCharges (Id, OrderId, RoomNumber, GuestName, Amount, TipAmount, SignatureDataUrl, Notes, ChargedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .uuid(orderId), .text(roomNumber), .text(guestName), .integer(amount.cents), .integer(tip.cents),
             .string(signature), .string(notes), .date(Date())]
        )
    }

    private static func decode(_ row: SQLRow) -> HotelRoom {
        HotelRoom(
            roomNumber: row.string("RoomNumber") ?? "", guestName: row.string("GuestName") ?? "",
            maxCreditLimit: Money(euros: row.decimal("MaxCreditLimit") ?? 0), currentBalance: Money(euros: row.decimal("CurrentBalance") ?? 0)
        )
    }
}
```

- [ ] **Step 7: Accesseurs de l'acteur et regroupement des stubs**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var kitchenRepository: LocalKitchenRepository { LocalKitchenRepository(db: db) }
```
par :
```swift
    var kitchenRepository: LocalKitchenRepository { LocalKitchenRepository(db: db) }
    var paymentRepository: LocalPaymentRepository { LocalPaymentRepository(db: db) }
    var holdRepository: LocalHoldRepository { LocalHoldRepository(db: db) }
    var hotelRepository: LocalHotelRepository { LocalHotelRepository(db: db) }
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Encaissement (plan 1c)
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult { throw unsupported() }
    public func hotelRooms() async throws -> [HotelRoom] { throw unsupported() }
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult { throw unsupported() }

    // MARK: Comptoir & vente à emporter (plan 1c)
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder { throw unsupported() }
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws { throw unsupported() }
    public func heldOrders(terminalId: String) async throws -> [HeldOrder] { throw unsupported() }
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder { throw unsupported() }
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws { throw unsupported() }
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult { throw unsupported() }
```
par :
```swift
    // MARK: Paiement des tables (retiré par la tâche 3)
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult { throw unsupported() }

    // MARK: Comptoir et mise en attente (retiré par la tâche 4)
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder { throw unsupported() }
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws { throw unsupported() }
    public func heldOrders(terminalId: String) async throws -> [HeldOrder] { throw unsupported() }
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder { throw unsupported() }
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws { throw unsupported() }

    // MARK: Encaissement comptoir (retiré par la tâche 5)
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult { throw unsupported() }

    // MARK: Chambres d'hôtel (retiré par la tâche 6)
    public func hotelRooms() async throws -> [HotelRoom] { throw unsupported() }
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult { throw unsupported() }
```

- [ ] **Step 8: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPaymentStoreTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`, aucun avertissement.

- [ ] **Step 9: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `251 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` (tâche 1 `fait`, PosKit `251`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): migration v3 et dépôts règlements, mise en attente et chambres"
```

---

### Task 2: Garde de statut et compteur d'échecs de PIN partagé

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Discounts.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Orders.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalClosedOrderTests.swift`

**Interfaces:**
- Consumes (tâche 1) : `orderRepository.status(of:)`, `LocalOrderStatus.isModifiable`, `APIError.localOrderClosed`.
- Produces : `LocalPosAPI.ensurePinAttemptsAllowed() throws`, `LocalPosAPI.recordFailedPin()`, `LocalPosAPI.resetFailedPins()` (utilisés par `login` et, en tâche 4, par `voidHeldOrder`) ; `applyDiscount`, `removeDiscount`, `compItem`, `setDestination` refusent une commande réglée ou annulée.

- [ ] **Step 1: Écrire le test qui échoue**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalClosedOrderTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : commandes réglées ou annulées")
struct LocalClosedOrderTests {
    @Test func discountsDestinationAndCompsRefuseACancelledOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", items: [localInput(burger)])
        try await seatTable(api, "T2", items: [localInput(cafe)])
        let cancelled = try #require(try await api.activeOrder(table: "T1"))
        let line = try #require(cancelled.lines.first)
        // La fusion annule la commande de T1.
        #expect(try await api.transfer(from: "T1", to: "T2", merge: true).success == true)

        let closed = APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")
        await #expect(throws: closed) { try await api.applyDiscount(orderId: cancelled.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil) }
        await #expect(throws: closed) { try await api.removeDiscount(orderId: cancelled.orderId) }
        await #expect(throws: closed) { try await api.compItem(orderId: cancelled.orderId, lineId: line.lineId, reason: "Test", operatorId: nil) }
        await #expect(throws: closed) { try await api.setDestination(orderId: cancelled.orderId, destination: .takeaway) }
        // Une commande inconnue reste une erreur « introuvable ».
        await #expect(throws: APIError.notFound("Commande introuvable.")) { try await api.setDestination(orderId: UUID(), destination: .takeaway) }
        // La commande vivante n'est pas concernée.
        let alive = try #require(try await api.activeOrder(table: "T2"))
        try await api.applyDiscount(orderId: alive.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalClosedOrderTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échec : les appels sur la commande annulée réussissent (ou lèvent une autre erreur).

- [ ] **Step 3: Partager le compteur d'échecs de PIN**

Dans `LocalPosAPI.swift`, remplacer :
```swift
        let now = Date()
        if let until = lockedUntil, until > now { throw APIError.rateLimited(nil) }
        guard let member = try staffRepository.activeMember(pin: pin) else {
            failedLogins = failedLogins.filter { now.timeIntervalSince($0) < Self.failureWindow } + [now]
            if failedLogins.count >= Self.maxFailedLogins {
                lockedUntil = now.addingTimeInterval(Self.lockoutDuration)
                failedLogins = []
            }
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        failedLogins = []
        lockedUntil = nil
```
par :
```swift
        try ensurePinAttemptsAllowed()
        guard let member = try staffRepository.activeMember(pin: pin) else {
            recordFailedPin()
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        resetFailedPins()
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    @discardableResult
    func requireAuth() throws -> StaffMember {
```
par :
```swift
    /// La connexion et le PIN superviseur partagent le même compteur d'échecs : un PIN deviné par l'une ou l'autre voie reste soumis au verrouillage.
    func ensurePinAttemptsAllowed() throws {
        if let until = lockedUntil, until > Date() { throw APIError.rateLimited(nil) }
    }

    func recordFailedPin() {
        let now = Date()
        failedLogins = failedLogins.filter { now.timeIntervalSince($0) < Self.failureWindow } + [now]
        if failedLogins.count >= Self.maxFailedLogins {
            lockedUntil = now.addingTimeInterval(Self.lockoutDuration)
            failedLogins = []
        }
    }

    func resetFailedPins() {
        failedLogins = []
        lockedUntil = nil
    }

    @discardableResult
    func requireAuth() throws -> StaffMember {
```

- [ ] **Step 4: Garde de statut**

Dans `LocalPosAPI+Discounts.swift`, remplacer :
```swift
            guard let order = try orders.order(id: orderId) else { throw Self.discountFailed }
```
par :
```swift
            guard let order = try orders.order(id: orderId) else { throw Self.discountFailed }
            guard try orders.status(of: orderId)?.isModifiable == true else { throw APIError.localOrderClosed }
```

Dans `LocalPosAPI+Discounts.swift`, remplacer :
```swift
        // Écart assumé : commande inconnue → 404 (le .NET lève une exception non gérée).
        guard try orderRepository.setDiscount(orderId: orderId, type: nil, value: 0, reason: nil) else { throw APIError.notFound("Commande introuvable.") }
```
par :
```swift
        // Écart assumé : commande inconnue → 404 (le .NET lève une exception non gérée) ; commande réglée ou annulée → 409.
        let orders = orderRepository
        try db.transaction {
            guard let status = try orders.status(of: orderId) else { throw APIError.notFound("Commande introuvable.") }
            guard status.isModifiable else { throw APIError.localOrderClosed }
            _ = try orders.setDiscount(orderId: orderId, type: nil, value: 0, reason: nil)
        }
```

Dans `LocalPosAPI+Discounts.swift`, remplacer :
```swift
            guard let order = try orders.order(id: orderId), let line = order.lines.first(where: { $0.lineId == lineId }),
                  try orders.comp(lineId: lineId, orderId: orderId, reason: trimmed)
            else { throw Self.compFailed }
```
par :
```swift
            guard let order = try orders.order(id: orderId) else { throw Self.compFailed }
            guard try orders.status(of: orderId)?.isModifiable == true else { throw APIError.localOrderClosed }
            guard let line = order.lines.first(where: { $0.lineId == lineId }) else { throw Self.compFailed }
            guard try orders.comp(lineId: lineId, orderId: orderId, reason: trimmed) else { throw Self.compFailed }
```

Dans `LocalPosAPI+Orders.swift`, remplacer :
```swift
        guard try orderRepository.setDestination(orderId: orderId, destination) else { throw APIError.notFound("Commande introuvable.") }
```
par :
```swift
        guard let status = try orderRepository.status(of: orderId) else { throw APIError.notFound("Commande introuvable.") }
        guard status.isModifiable else { throw APIError.localOrderClosed }
        _ = try orderRepository.setDestination(orderId: orderId, destination)
```

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalClosedOrderTests|LocalAuthTests|LocalDiscountTests|LocalOrderTests" 2>&1 | grep -E "error:|✘|Test run"`
Expected: tous les tests passent (la connexion garde son comportement : `fiveFailuresLockLoginEvenWithTheRightPin`, `successResetsTheFailureCounter`).

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `252 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` (tâche 2 `fait`, PosKit `252`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): garde de statut des commandes et compteur d'échecs de PIN partagé"
```

---

### Task 3: Paiement des tables (en une ou plusieurs fois)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Payments.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentTestSupport.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentTests.swift`

**Interfaces:**
- Consumes (tâches 1-2) : `paymentRepository`, `orderRepository` (`status(of:)`, `tipCents(of:)`, `setTip`, `setStatus`), `floorRepository`, `decorate(_:)`, `activeOrder(on:)`, `LocalOrderStatus.isModifiable`.
- Produces :
  - `struct LocalSettlement { receiptNumber: String; totalPaid: Money; changeGiven: Money; remaining: Money }`.
  - `LocalPosAPI.normalizedTerminal(_:) -> String`, `.balance(of:) throws -> (total: Money, tip: Money, paid: Money)`, `.closeOrder(_:as:) throws`, `.settle(order:tenders:terminalId:) throws -> LocalSettlement` (à appeler dans une transaction).
  - Méthode `PosAPI` `pay(_:)`.
  - Helpers de test : `localTender(_:_:tendered:)`, `localPayment(_:table:tenders:tip:terminal:)`.

- [ ] **Step 1: Écrire les helpers et les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentTestSupport.swift` :
```swift
import Foundation
@testable import PosKit

/// Règlement d'un moyen de paiement (`tendered` = montant remis par le client, par défaut égal au montant).
func localTender(_ method: PaymentMethod = .cash, _ amount: Int, tendered: Int? = nil) -> TenderInput {
    TenderInput(method: method, amount: Money(cents: amount), tendered: Money(cents: tendered ?? amount), changeGiven: .zero)
}

func localPayment(_ order: ActiveOrder?, table: String, tenders: [TenderInput], tip: Int = 0, terminal: String = "T01") -> PaymentRequest {
    PaymentRequest(orderId: order?.orderId, tableNumber: table, operatorId: nil, terminalId: terminal, tenders: tenders, requestReceiptPrint: false, tipAmount: Money(cents: tip))
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPaymentTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : paiement des tables")
struct LocalPaymentTests {
    private static let paymentFailed = APIError.server(status: 400, message: "Échec de l'encaissement.")

    private func seat(_ api: LocalPosAPI, _ table: String = "T1", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func payingTheFullAmountClosesTheOrderAndFreesTheTable() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950, tendered: 2000)]))
        #expect(result.receiptNumber == "NF-T01-000001" && result.fiscalSignature == nil && result.printQueued == false)
        #expect(result.totalPaid == Money(cents: 1950) && result.changeGiven == Money(cents: 50) && result.remainingBalance == .zero)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .free && table.activeOrderId == nil && table.coversCount == 0)
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func splitPaymentsAccumulateUntilTheOrderIsSettled() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let first = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        #expect(first.receiptNumber == "NF-T01-000001" && first.remainingBalance == Money(cents: 950) && first.changeGiven == .zero)
        #expect(try await api.tables().first { $0.tableNumber == "T1" }?.status == .occupied)
        let second = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950, tendered: 1000)]))
        #expect(second.receiptNumber == "NF-T01-000002" && second.remainingBalance == .zero && second.changeGiven == Money(cents: 50))
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func badTendersAreRefusedWithoutWriting() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let attempts: [[TenderInput]] = [[], [localTender(.cash, 0)], [localTender(.cash, -100)], [localTender(.cash, 2000)], [localTender(.cash, 1000), localTender(.creditCard, 1000)]]
        for tenders in attempts {
            await #expect(throws: Self.paymentFailed) { try await api.pay(localPayment(order, table: "T1", tenders: tenders)) }
        }
        // Rien n'a été écrit : le premier vrai règlement porte le premier numéro de reçu et solde la note.
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        #expect(result.receiptNumber == "NF-T01-000001" && result.remainingBalance == .zero)
    }

    @Test func aPaidOrderCannotBePaidAgain() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        await #expect(throws: Self.paymentFailed) { try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 100)])) }
        await #expect(throws: APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")) {
            try await api.pay(localPayment(nil, table: "T404", tenders: [localTender(.cash, 100)]))
        }
    }

    @Test func tipIsOnlyAcceptedOnTheFinalPayment() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        await #expect(throws: APIError.server(status: 400, message: "Le pourboire ne peut être ajouté qu'au paiement qui solde la note.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)], tip: 200))
        }
        await #expect(throws: APIError.server(status: 400, message: "Montant de pourboire invalide.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)], tip: -1))
        }
        // Aucun pourboire n'est resté enregistré : le paiement final avec pourboire solde exactement la note.
        let final = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 2150, tendered: 2200)], tip: 200))
        #expect(final.totalPaid == Money(cents: 2150) && final.changeGiven == Money(cents: 50) && final.remainingBalance == .zero)
    }

    @Test func payResolvesTheOrderFromTheTable() async throws {
        let api = try await makeLocalAPI()
        _ = try await seat(api, "T3")
        let result = try await api.pay(localPayment(nil, table: "T3", tenders: [localTender(.cash, 1950)]))
        #expect(result.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func payNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.pay(localPayment(nil, table: "T1", tenders: [localTender(.cash, 100)])) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPaymentTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter le règlement**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Payments.swift` :
```swift
import Foundation

struct LocalSettlement {
    let receiptNumber: String
    let totalPaid: Money
    let changeGiven: Money
    let remaining: Money
}

extension LocalPosAPI {
    static let paymentFailed = APIError.server(status: 400, message: "Échec de l'encaissement.")

    /// Identifiant de terminal utilisable dans un numéro de reçu (le poste autonome s'appellera `T01` au plan 1d).
    func normalizedTerminal(_ id: String) -> String {
        let trimmed = id.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "LOCAL" : trimmed
    }

    /// Total TTC de la commande, pourboire déjà enregistré et montant déjà réglé.
    func balance(of order: ActiveOrder) throws -> (total: Money, tip: Money, paid: Money) {
        let total = decorate(order).totalTtcAmount ?? .zero
        let tip = Money(cents: try orderRepository.tipCents(of: order.orderId))
        let paid = Money(cents: try paymentRepository.paidCents(orderId: order.orderId))
        return (total, tip, paid)
    }

    /// Clôture la commande (réglée ou annulée) et libère sa table si elle y était encore.
    func closeOrder(_ order: ActiveOrder, as status: LocalOrderStatus) throws {
        try orderRepository.setStatus(orderId: order.orderId, status)
        if let table = try floorRepository.table(order.tableNumber), table.activeOrderId == order.orderId {
            try floorRepository.update(table.tableNumber, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
        }
    }

    /// Enregistre des règlements sur une commande. Refuse (sans rien écrire) une liste vide, un montant ≤ 0, un total supérieur au solde
    /// dû ou une commande déjà réglée ou annulée. À appeler dans une transaction.
    func settle(order: ActiveOrder, tenders: [(method: PaymentMethod, amount: Money, tendered: Money)], terminalId: String) throws -> LocalSettlement {
        guard !tenders.isEmpty, tenders.allSatisfy({ $0.amount > .zero && $0.tendered >= .zero }),
              try orderRepository.status(of: order.orderId)?.isModifiable == true
        else { throw Self.paymentFailed }
        let (total, tip, paid) = try balance(of: order)
        let remainingBefore = (total + tip - paid).clampedAtZero()
        let totalPaid = tenders.reduce(Money.zero) { $0 + $1.amount }
        guard totalPaid <= remainingBefore else { throw Self.paymentFailed }

        let totalTendered = tenders.reduce(Money.zero) { $0 + $1.tendered }
        let change = (totalTendered - remainingBefore).clampedAtZero()
        let terminal = normalizedTerminal(terminalId)
        let receipt = try paymentRepository.nextReceiptNumber(terminalId: terminal)
        for tender in tenders {
            let tenderChange = tender.method == .cash ? (tender.tendered - tender.amount).clampedAtZero() : .zero
            try paymentRepository.insertTender(
                orderId: order.orderId, terminalId: terminal, receiptNumber: receipt, method: tender.method,
                amount: tender.amount.cents, tendered: tender.tendered.cents, change: tenderChange.cents
            )
        }
        let remaining = remainingBefore - totalPaid
        if remaining == .zero { try closeOrder(order, as: .paid) }
        return LocalSettlement(receiptNumber: receipt, totalPaid: totalPaid, changeGiven: change, remaining: remaining)
    }

    /// Règlement d'une table, en une ou plusieurs fois (note partagée). Le pourboire n'est accepté qu'au paiement qui solde la note.
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult {
        try requireAuth()
        guard request.tipAmount >= .zero else { throw APIError.server(status: 400, message: "Montant de pourboire invalide.") }
        let floor = floorRepository, orders = orderRepository
        return try db.transaction { () throws -> PaymentResult in
            let notFound = APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")
            let orderId: UUID
            if let id = request.orderId, try orders.order(id: id) != nil {
                orderId = id
            } else if let active = try floor.table(request.tableNumber)?.activeOrderId, try orders.order(id: active) != nil {
                orderId = active
            } else {
                throw notFound
            }
            guard let order = try orders.order(id: orderId) else { throw notFound }
            let tenders = request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
            if request.tipAmount > .zero {
                let (total, tip, paid) = try balance(of: order)
                let remaining = total + tip - paid
                let offered = tenders.reduce(Money.zero) { $0 + $1.amount }
                guard offered >= remaining + request.tipAmount else {
                    throw APIError.server(status: 400, message: "Le pourboire ne peut être ajouté qu'au paiement qui solde la note.")
                }
                try orders.setTip(orderId: orderId, cents: tip.cents + request.tipAmount.cents)
            }
            let settlement = try settle(order: order, tenders: tenders, terminalId: request.terminalId)
            return PaymentResult(
                receiptNumber: settlement.receiptNumber, totalPaid: settlement.totalPaid, changeGiven: settlement.changeGiven,
                remainingBalance: settlement.remaining, fiscalSignature: nil, printQueued: false
            )
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Paiement des tables (retiré par la tâche 3)` : ce commentaire, la ligne `pay`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPaymentTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `259 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` (tâche 3 `fait`, PosKit `259`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): paiement des tables non fiscal, en plusieurs fois, avec pourboire"
```

---

### Task 4: Comptoir et mise en attente

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Counter.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterTests.swift`

**Interfaces:**
- Consumes (tâches 1-3) : `holdRepository`, `orderRepository`, `floorRepository`, `staffRepository.activeMember(pin:)`, `decorate`, `activeOrder(on:)`, `ensurePinAttemptsAllowed()`, `recordFailedPin()`, `resetFailedPins()`, `LocalOrderStatus.isModifiable`, `requireAuth()` (renvoie le `StaffMember`).
- Produces : méthodes `PosAPI` `openCounterOrder`, `holdOrder`, `heldOrders`, `recallHeldOrder`, `voidHeldOrder`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : comptoir et mise en attente")
struct LocalCounterTests {
    private static let forbidden = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")

    /// Panier du comptoir contenant `quantity` burgers.
    private func counterCart(_ api: LocalPosAPI, quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        return try await api.addItems(table: "Comptoir", items: [localInput(burger, quantity: quantity)])
    }

    @Test func openCounterOrderCreatesAndReusesTheCounterOrder() async throws {
        let api = try await makeLocalAPI()
        let first = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        #expect(first.tableNumber == "Comptoir" && first.destination == .takeaway && first.lines.isEmpty)
        let again = try await api.openCounterOrder(terminalId: "T01", destination: .eatIn)
        #expect(again.orderId == first.orderId && again.destination == .takeaway)
        let table = try #require(try await api.tables().first { $0.isCounter })
        #expect(table.capacity == 1 && table.status == .occupied && table.activeOrderId == first.orderId)
    }

    @Test func holdDetachesTheOrderAndListsIt() async throws {
        let api = try await makeLocalAPI()
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        await #expect(throws: APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.")) {
            try await api.holdOrder(orderId: empty.orderId, terminalId: "T01", label: "Vide")
        }
        let cart = try await counterCart(api, quantity: 2)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")
        let held = try #require(try await api.heldOrders(terminalId: "T01").first)
        #expect(held.orderId == cart.orderId && held.customerLabel == "Dupont" && held.itemCount == 2)
        #expect(held.totalTtc.amountInCents == 3900 && held.destination == .takeaway && held.terminalId == "T01")
        #expect(try await api.activeOrder(table: "Comptoir") == nil)
        #expect(try await api.tables().first { $0.isCounter }?.status == .free)
        await #expect(throws: APIError.server(status: 409, message: "Cette commande est déjà en attente.")) {
            try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Encore")
        }
    }

    @Test func recallRestoresTheHeldOrderOnTheCounter() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api, quantity: 2)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        let recalled = try await api.recallHeldOrder(holdId: hold.holdId)
        #expect(recalled.orderId == cart.orderId && recalled.lines.first?.quantity == 2 && recalled.tableNumber == "Comptoir")
        #expect(try await api.heldOrders(terminalId: "T01").isEmpty)
        #expect(try await api.tables().first { $0.isCounter }?.activeOrderId == cart.orderId)
        await #expect(throws: APIError.notFound("Commande en attente introuvable ou déjà rappelée.")) { try await api.recallHeldOrder(holdId: hold.holdId) }
    }

    @Test func recallReplacesAnEmptyCounterButRefusesABusyOne() async throws {
        let api = try await makeLocalAPI()
        let held = try await counterCart(api)
        try await api.holdOrder(orderId: held.orderId, terminalId: "T01", label: "A")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)

        // Un panier de comptoir vide est remplacé…
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        #expect(empty.lines.isEmpty && empty.orderId != held.orderId)
        let recalled = try await api.recallHeldOrder(holdId: hold.holdId)
        #expect(recalled.orderId == held.orderId)

        // …mais une vente en cours n'est jamais écrasée : la commande en attente reste disponible.
        try await api.holdOrder(orderId: held.orderId, terminalId: "T01", label: "B")
        let busy = try await counterCart(api)
        let secondHold = try #require(try await api.heldOrders(terminalId: "T01").first)
        await #expect(throws: APIError.server(status: 409, message: "Le comptoir a déjà une vente en cours.")) { try await api.recallHeldOrder(holdId: secondHold.holdId) }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)
        #expect(try await api.activeOrder(table: "Comptoir")?.orderId == busy.orderId)
    }

    @Test func voidHeldOrderNeedsASupervisorPin() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "À annuler")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)

        await #expect(throws: APIError.server(status: 400, message: "Code PIN superviseur requis.")) {
            try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "  ", reason: "Test", terminalId: "T01")
        }
        await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "2468", reason: "Test", terminalId: "T01") }
        await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "0000", reason: "Test", terminalId: "T01") }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)

        try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Client parti", terminalId: "T01")
        #expect(try await api.heldOrders(terminalId: "T01").isEmpty)
        await #expect(throws: APIError.notFound("Commande en attente introuvable.")) {
            try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Encore", terminalId: "T01")
        }
        // La commande annulée n'est plus modifiable.
        await #expect(throws: APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")) {
            try await api.applyDiscount(orderId: cart.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
        }
    }

    @Test func wrongSupervisorPinsLockLoginToo() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "X")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        for _ in 0..<5 {
            await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "0000", reason: "Test", terminalId: "T01") }
        }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Test", terminalId: "T01") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)
    }

    @Test func heldOrdersAreFilteredByTerminal() async throws {
        let api = try await makeLocalAPI()
        let first = try await counterCart(api)
        try await api.holdOrder(orderId: first.orderId, terminalId: "T01", label: "A")
        let second = try await counterCart(api)
        try await api.holdOrder(orderId: second.orderId, terminalId: "T02", label: "B")
        #expect(try await api.heldOrders(terminalId: "T01").map(\.customerLabel) == ["A"])
        #expect(try await api.heldOrders(terminalId: "T02").map(\.customerLabel) == ["B"])
        #expect(try await api.heldOrders(terminalId: "").count == 2)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalCounterTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter le comptoir et la mise en attente**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Counter.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Table « Comptoir » (ventes directes), créée au besoin.
    private func counterTable() throws -> DiningTable {
        let counter = DiningTable.counterNumber
        if try !floorRepository.tableExists(counter) { try floorRepository.insert(DiningTable(tableNumber: counter, capacity: 1)) }
        guard let table = try floorRepository.table(counter) else { throw SQLiteError(code: -1, message: "Table Comptoir introuvable après création") }
        return table
    }

    /// Panier courant du comptoir : le crée s'il n'y en a pas (la destination demandée ne s'applique qu'à un nouveau panier).
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            let table = try counterTable()
            if let id = table.activeOrderId, try orders.order(id: id) != nil { return }
            let created = ActiveOrder(tableNumber: DiningTable.counterNumber, destination: destination)
            try orders.insert(created, operatorId: localNoOperator, status: .open)
            try floor.update(DiningTable.counterNumber, status: .occupied, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: created.orderId, openedAt: Date())
        }
        guard let order = try activeOrder(on: try counterTable()) else { throw SQLiteError(code: -1, message: "Panier du comptoir introuvable après création") }
        return order
    }

    /// Met un panier en attente et libère le comptoir. Refusé si le panier est vide ou déjà en attente.
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws {
        let member = try requireAuth()
        let floor = floorRepository, orders = orderRepository, holds = holdRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId), !order.lines.isEmpty, try orders.status(of: orderId)?.isModifiable == true else {
                throw APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.")
            }
            guard try !holds.hasActiveHold(orderId: orderId) else { throw APIError.server(status: 409, message: "Cette commande est déjà en attente.") }
            let decorated = decorate(order)
            let snapshot = String(decoding: try JSONEncoder().encode(decorated), as: UTF8.self)
            try holds.insert(
                orderId: orderId, terminalId: terminalId.isEmpty ? "POS_MAIN_TERM" : terminalId, label: label.isEmpty ? nil : label,
                destination: order.destination, itemCount: order.lines.reduce(0) { $0 + $1.quantity }, total: decorated.totalTtcAmount ?? .zero,
                snapshot: snapshot, staffId: member.id
            )
            if let table = try floor.table(order.tableNumber), table.activeOrderId == orderId {
                try floor.update(table.tableNumber, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
            }
        }
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        try requireAuth()
        return try holdRepository.active(terminalId: terminalId)
    }

    /// Remet un panier en attente sur le comptoir. Un panier de comptoir vide est annulé et remplacé ; une vente en cours n'est jamais
    /// écrasée (`409`) : la commande en attente reste alors disponible.
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, holds = holdRepository
        try db.transaction {
            let notFound = APIError.notFound("Commande en attente introuvable ou déjà rappelée.")
            guard let orderId = try holds.activeOrderId(holdId: holdId), try orders.order(id: orderId) != nil,
                  try orders.status(of: orderId)?.isModifiable == true
            else { throw notFound }
            let table = try counterTable()
            if let current = table.activeOrderId, let existing = try orders.order(id: current) {
                guard existing.lines.isEmpty else { throw APIError.server(status: 409, message: "Le comptoir a déjà une vente en cours.") }
                try orders.setStatus(orderId: current, .cancelled)
            }
            _ = try holds.markRecalled(id: holdId)
            try floor.update(DiningTable.counterNumber, status: .occupied, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: orderId, openedAt: table.openedAtUtc ?? Date())
        }
        guard let order = try activeOrder(on: try counterTable()) else { throw SQLiteError(code: -1, message: "Panier rappelé introuvable après écriture") }
        return order
    }

    /// Annule une commande en attente, sur PIN d'un responsable. Les échecs de PIN comptent dans le même verrouillage que la connexion.
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        try requireAuth()
        guard !supervisorPin.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw APIError.server(status: 400, message: "Code PIN superviseur requis.")
        }
        try ensurePinAttemptsAllowed()
        let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        guard let supervisor = try staffRepository.activeMember(pin: supervisorPin) else {
            recordFailedPin()
            throw insufficient
        }
        guard supervisor.role.isManager else { throw insufficient }
        resetFailedPins()
        let orders = orderRepository, holds = holdRepository
        try db.transaction {
            guard let orderId = try holds.activeOrderId(holdId: holdId) else { throw APIError.notFound("Commande en attente introuvable.") }
            _ = try holds.markVoided(id: holdId, staffId: supervisor.id, reason: reason)
            try orders.setStatus(orderId: orderId, .cancelled)
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Comptoir et mise en attente (retiré par la tâche 4)` : ce commentaire, les 5 lignes `openCounterOrder`, `holdOrder`, `heldOrders`, `recallHeldOrder`, `voidHeldOrder`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalCounterTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `266 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` (tâche 4 `fait`, PosKit `266`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): comptoir, mise en attente, rappel et annulation sur PIN superviseur"
```

---

### Task 5: Encaissement au comptoir (retrait, titres-restaurant, avoir)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+CounterCheckout.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterCheckoutTests.swift`

**Interfaces:**
- Consumes (tâches 1-4) : `settle(order:tenders:terminalId:)`, `normalizedTerminal`, `paymentRepository` (`nextPickupNumber`, `insertCreditVoucher`), `orderRepository` (`order`, `status(of:)`, `setPickup`, `setTip`, `foodVoucherEligibleLineIds`), `decorate`, `closeOrder`, modèles `CounterCheckoutRequest`, `CounterTender`, `CounterCheckoutResult`, `CreditVoucher`, `MealVoucherPolicy`.
- Produces : `enum LocalMealVoucher { static let dailyCap: Money; struct Outcome { error: String?; surplus: Money }; validate(eligibleSubtotal:orderTotal:amount:facialValue:policy:) -> Outcome; format(_:) -> String }` ; méthode `PosAPI` `counterCheckout(_:)`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterCheckoutTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : encaissement au comptoir")
struct LocalCounterCheckoutTests {
    private func cart(_ api: LocalPosAPI, quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        return try await api.addItems(table: "Comptoir", items: [localInput(burger, quantity: quantity)])
    }

    private func checkout(_ order: ActiveOrder, _ tenders: [CounterTender], terminal: String = "T01", tip: Int = 0, policy: MealVoucherPolicy = .capAtBalance) -> CounterCheckoutRequest {
        CounterCheckoutRequest(
            orderId: order.orderId, terminalId: terminal, destination: .takeaway, pickupBuzzer: nil, tipAmount: Money(cents: tip),
            requestFiscalReceiptPrint: false, mealVoucherPolicy: policy, tenders: tenders
        )
    }

    private func cash(_ amount: Int, tendered: Int? = nil) -> CounterTender {
        CounterTender(method: .cash, amount: Money(cents: amount), tendered: Money(cents: tendered ?? amount))
    }

    private func voucher(_ amount: Int, facial: Int? = nil) -> CounterTender {
        CounterTender(method: .mealVoucher, amount: Money(cents: amount), tendered: Money(cents: amount), facialValue: facial.map { Money(cents: $0) })
    }

    @Test func counterCheckoutPaysAssignsPickupAndClosesTheOrder() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)
        var request = checkout(order, [cash(1950, tendered: 2000)])
        request.pickupBuzzer = "12"
        let result = try await api.counterCheckout(request)
        #expect(result.orderId == order.orderId && result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001")
        #expect(result.totalPaid == Money(cents: 1950) && result.changeGiven == Money(cents: 50) && result.remainingBalance == .zero)
        #expect(result.openCashDrawer == true && result.issuedCreditVoucher == nil && result.fiscalSignature == nil && result.printQueued == false)
        #expect(try await api.tables().first { $0.isCounter }?.status == .free)
        #expect(try await api.activeOrder(table: "Comptoir") == nil)

        let second = try await api.counterCheckout(checkout(try await cart(api), [cash(1950)]))
        #expect(second.pickupNumber == "#A-02" && second.receiptNumber == "NF-T01-000002" && second.openCashDrawer == true)
    }

    @Test func pickupNumbersFollowTheTerminalPrefix() async throws {
        let api = try await makeLocalAPI()
        let result = try await api.counterCheckout(checkout(try await cart(api), [cash(1950)], terminal: "T02"))
        #expect(result.pickupNumber == "#B-01" && result.receiptNumber == "NF-T02-000001")
    }

    @Test func mealVoucherAboveTheLegalCapIsRefusedWithoutConsumingAPickupNumber() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api, quantity: 2)  // 39,00 € dont tout est éligible
        await #expect(throws: APIError.server(status: 400, message: "Le montant par Titre-Restaurant (30.00 €) dépasse le plafond légal éligible (25.00 €).")) {
            try await api.counterCheckout(checkout(order, [voucher(3000)]))
        }
        // Rien n'a été écrit : la vente suivante reçoit le premier numéro de retrait et de reçu.
        let result = try await api.counterCheckout(checkout(order, [cash(3900)]))
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001" && result.issuedCreditVoucher == nil)
    }

    @Test func mealVoucherOverpaymentFollowsThePolicy() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)  // 19,50 €
        await #expect(throws: APIError.server(status: 400, message: "Surpaiement par Titre-Restaurant refusé : la valeur faciale (25.00 €) dépasse le solde dû (19.50 €).")) {
            try await api.counterCheckout(checkout(order, [voucher(1950, facial: 2500)], policy: .strictRejection))
        }
        // Plafonné au solde : le surplus est absorbé, aucun avoir.
        let capped = try await api.counterCheckout(checkout(order, [voucher(1950, facial: 2500)], policy: .capAtBalance))
        #expect(capped.totalPaid == Money(cents: 1950) && capped.remainingBalance == .zero && capped.issuedCreditVoucher == nil && capped.openCashDrawer == false)

        let credited = try await api.counterCheckout(checkout(try await cart(api), [voucher(1950, facial: 2500)], policy: .customerCreditVoucher))
        let credit = try #require(credited.issuedCreditVoucher)
        #expect(credit.amount == Money(cents: 550) && credit.voucherCode.hasPrefix("CR-") && credit.voucherCode.count == 11)
        let expiry = try #require(credit.expiresAtUtc)
        #expect(abs(expiry.timeIntervalSinceNow - 90 * 86_400) < 60)
    }

    @Test func counterCheckoutRefusesEmptyOrUnknownOrders() async throws {
        let api = try await makeLocalAPI()
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        let refused = APIError.server(status: 400, message: "Commande introuvable ou panier vide.")
        await #expect(throws: refused) { try await api.counterCheckout(checkout(empty, [cash(100)])) }
        var unknown = checkout(empty, [cash(100)])
        unknown.orderId = UUID()
        await #expect(throws: refused) { try await api.counterCheckout(unknown) }
    }

    @Test func counterTipIsRecordedAndAPartialPaymentLeavesTheBalance() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)
        let result = try await api.counterCheckout(checkout(order, [cash(1950)], tip: 100))
        #expect(result.totalPaid == Money(cents: 1950) && result.remainingBalance == Money(cents: 100))
        #expect(try await api.activeOrder(table: "Comptoir")?.orderId == order.orderId)
        let rest = try await api.pay(localPayment(order, table: "Comptoir", tenders: [localTender(.cash, 100)]))
        #expect(rest.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "Comptoir") == nil)
    }

    @Test func counterCheckoutNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let order = ActiveOrder(tableNumber: "Comptoir")
        await #expect(throws: APIError.unauthorized) { try await api.counterCheckout(checkout(order, [cash(100)])) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalCounterCheckoutTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter l'encaissement au comptoir**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+CounterCheckout.swift` :
```swift
import Foundation

/// Règle des titres-restaurant (`MealVoucherPolicyService`) : plafond légal journalier et surpaiement.
enum LocalMealVoucher {
    static let dailyCap = Money(cents: 2500)

    struct Outcome {
        let error: String?
        let surplus: Money
    }

    /// `eligibleSubtotal` : total TTC des lignes payables en titres-restaurant ; `orderTotal` : total TTC de la commande.
    static func validate(eligibleSubtotal: Money, orderTotal: Money, amount: Money, facialValue: Money?, policy: MealVoucherPolicy) -> Outcome {
        let legalMax = min(eligibleSubtotal, dailyCap)
        if amount > legalMax {
            return Outcome(error: "Le montant par Titre-Restaurant (\(format(amount)) €) dépasse le plafond légal éligible (\(format(legalMax)) €).", surplus: .zero)
        }
        if let face = facialValue, face > orderTotal {
            let surplus = face - orderTotal
            switch policy {
            case .strictRejection:
                return Outcome(error: "Surpaiement par Titre-Restaurant refusé : la valeur faciale (\(format(face)) €) dépasse le solde dû (\(format(orderTotal)) €).", surplus: surplus)
            case .customerCreditVoucher:
                return Outcome(error: nil, surplus: surplus)
            case .capAtBalance:
                return Outcome(error: nil, surplus: .zero)
            }
        }
        return Outcome(error: nil, surplus: .zero)
    }

    /// `19.50` : deux décimales, point décimal (comme le serveur).
    static func format(_ amount: Money) -> String {
        String(format: "%d.%02d", amount.cents / 100, amount.cents % 100)
    }
}

extension LocalPosAPI {
    /// Encaisse une vente au comptoir ou à emporter. Tous les refus (panier vide, titre-restaurant hors règle) précèdent la première
    /// écriture : un refus ne consomme ni numéro de retrait ni avoir (le .NET attribue d'abord le numéro).
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        try requireAuth()
        guard request.tipAmount >= .zero else { throw APIError.server(status: 400, message: "Montant de pourboire invalide.") }
        let orders = orderRepository, payments = paymentRepository
        return try db.transaction { () throws -> CounterCheckoutResult in
            guard let order = try orders.order(id: request.orderId), !order.lines.isEmpty,
                  try orders.status(of: request.orderId)?.isModifiable == true
            else { throw APIError.server(status: 400, message: "Commande introuvable ou panier vide.") }
            let terminal = normalizedTerminal(request.terminalId)

            var surplus = Money.zero
            if let voucher = request.tenders.first(where: { $0.method == .mealVoucher }) {
                let eligibleIds = try orders.foodVoucherEligibleLineIds(orderId: order.orderId)
                let eligible = order.lines.filter { eligibleIds.contains($0.lineId) }
                    .reduce(Money.zero) { $0 + OrderMath.lineTotal(CartLine(serverLine: $1)) }
                let outcome = LocalMealVoucher.validate(
                    eligibleSubtotal: eligible, orderTotal: decorate(order).totalTtcAmount ?? .zero, amount: voucher.amount,
                    facialValue: voucher.facialValue, policy: request.mealVoucherPolicy
                )
                if let message = outcome.error { throw APIError.server(status: 400, message: message) }
                if request.mealVoucherPolicy == .customerCreditVoucher { surplus = outcome.surplus }
            }

            let pickup = try payments.nextPickupNumber(terminalId: terminal, now: Date())
            try orders.setPickup(orderId: order.orderId, number: pickup, buzzer: request.pickupBuzzer, destination: request.destination)
            if request.tipAmount > .zero { try orders.setTip(orderId: order.orderId, cents: request.tipAmount.cents) }

            var issued: CreditVoucher?
            if surplus > .zero {
                let code = "CR-" + String(UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(8)).uppercased()
                let expiry = Date().addingTimeInterval(90 * 86_400)
                try payments.insertCreditVoucher(code: code, orderId: order.orderId, terminalId: terminal, amount: surplus, expiresAt: expiry)
                issued = CreditVoucher(voucherCode: code, amount: surplus, expiresAtUtc: expiry)
            }

            guard let current = try orders.order(id: order.orderId) else { throw SQLiteError(code: -1, message: "Commande introuvable après mise à jour") }
            let tenders = request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
            let settlement = try settle(order: current, tenders: tenders, terminalId: terminal)
            return CounterCheckoutResult(
                orderId: order.orderId, pickupNumber: pickup, totalPaid: settlement.totalPaid, changeGiven: settlement.changeGiven,
                remainingBalance: settlement.remaining, receiptNumber: settlement.receiptNumber, fiscalSignature: nil, issuedCreditVoucher: issued,
                openCashDrawer: request.tenders.contains { $0.method == .cash }, printQueued: false
            )
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Encaissement comptoir (retiré par la tâche 5)` : ce commentaire, la ligne `counterCheckout`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalCounterCheckoutTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `273 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` (tâche 5 `fait`, PosKit `273`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): encaissement au comptoir (retrait, titres-restaurant, avoir) sans écriture avant validation"
```

---

### Task 6: Chambres d'hôtel et documentation

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Hotel.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHotelTests.swift`
- Modify: `ios/README.md`

**Interfaces:**
- Consumes : `hotelRepository`, `orderRepository`, `paymentRepository.paidCents`, `balance(of:)`, `closeOrder(_:as:)`, `floorRepository`, `Seed.make().rooms`, `LocalMealVoucher.format(_:)`.
- Produces : méthodes `PosAPI` `hotelRooms()`, `chargeRoom(_:)` ; chambres de démonstration dans `LocalSeeder`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHotelTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : chambres d'hôtel")
struct LocalHotelTests {
    private func seat(_ api: LocalPosAPI, _ table: String = "T3", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    private func charge(_ order: ActiveOrder, table: String = "T3", room: String = "101", amount: Int, tip: Int = 0) -> RoomChargeRequest {
        RoomChargeRequest(
            orderId: order.orderId, tableNumber: table, roomNumber: room, guestName: "Jean Dujardin", amount: Money(cents: amount),
            tipAmount: Money(cents: tip), signatureDataUrl: nil, notes: nil
        )
    }

    @Test func hotelRoomsListsTheOccupiedRooms() async throws {
        let api = try await makeLocalAPI()
        let rooms = try await api.hotelRooms()
        #expect(rooms.map(\.roomNumber) == ["101", "204", "305"])
        #expect(rooms[0].maxCreditLimit == Money(cents: 30000) && rooms[0].currentBalance == .zero && rooms[0].availableCredit == Money(cents: 30000))
    }

    @Test func chargeRoomClosesTheOrderAndRaisesTheBalance() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let result = try await api.chargeRoom(charge(order, amount: 1950, tip: 100))
        #expect(result.success == true && result.message == "Facturation de 20.50 € enregistrée sur la chambre 101 (Jean Dujardin)")
        #expect(try await api.hotelRooms().first { $0.roomNumber == "101" }?.currentBalance == Money(cents: 2050))
        #expect(try await api.activeOrder(table: "T3") == nil)
        #expect(try await api.tables().first { $0.tableNumber == "T3" }?.status == .free)
        // La commande est clôturée : on ne peut plus la remiser.
        await #expect(throws: APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")) {
            try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
        }
    }

    @Test func chargeRoomRefusalsLeaveBalanceAndOrderUntouched() async throws {
        let api = try await makeLocalAPI()
        let big = try await seat(api, "T3", quantity: 16)  // 312,00 € > plafond de la chambre 101 (300,00 €)
        await #expect(throws: APIError.server(status: 400, message: "Plafond de crédit chambre dépassé.")) { try await api.chargeRoom(charge(big, amount: 31200)) }
        await #expect(throws: APIError.server(status: 400, message: "Chambre 999 introuvable ou non occupée.")) { try await api.chargeRoom(charge(big, room: "999", amount: 31200)) }
        await #expect(throws: APIError.server(status: 400, message: "Facturation chambre échouée.")) { try await api.chargeRoom(charge(big, room: "204", amount: 1000)) }
        await #expect(throws: APIError.server(status: 400, message: "Facturation chambre échouée.")) { try await api.chargeRoom(charge(big, room: "204", amount: 0)) }
        #expect(try await api.hotelRooms().allSatisfy { $0.currentBalance == .zero })
        #expect(try await api.activeOrder(table: "T3")?.orderId == big.orderId)
        // La même commande se facture normalement sur une chambre au plafond suffisant.
        let result = try await api.chargeRoom(charge(big, room: "204", amount: 31200))
        #expect(result.success == true)
        #expect(try await api.hotelRooms().first { $0.roomNumber == "204" }?.currentBalance == Money(cents: 31200))
    }

    @Test func roomCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.hotelRooms() }
        await #expect(throws: APIError.unauthorized) { try await api.chargeRoom(RoomChargeRequest(orderId: nil, tableNumber: "T1", roomNumber: "101", guestName: "X", amount: Money(cents: 100), tipAmount: .zero, signatureDataUrl: nil, notes: nil)) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHotelTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter les chambres**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Hotel.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Chambres occupées, par numéro (`GET /api/hotel/rooms`).
    public func hotelRooms() async throws -> [HotelRoom] {
        try requireAuth()
        return try hotelRepository.occupiedRooms()
    }

    /// Facture une commande sur une chambre. Refusée si la chambre est inconnue ou libre, si le plafond de crédit serait dépassé, ou si le
    /// montant n'est pas exactement le solde de la commande (écart assumé : le .NET ne contrôle pas le montant). La commande est clôturée
    /// et sa table libérée (le .NET laisse la commande ouverte).
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult {
        try requireAuth()
        let failed = APIError.server(status: 400, message: "Facturation chambre échouée.")
        guard request.amount > .zero, request.tipAmount >= .zero else { throw failed }
        let floor = floorRepository, orders = orderRepository, hotel = hotelRepository
        return try db.transaction { () throws -> OperationResult in
            let orderId: UUID
            if let id = request.orderId, try orders.order(id: id) != nil {
                orderId = id
            } else if let active = try floor.table(request.tableNumber)?.activeOrderId, try orders.order(id: active) != nil {
                orderId = active
            } else {
                throw failed
            }
            guard let order = try orders.order(id: orderId), try orders.status(of: orderId)?.isModifiable == true else { throw failed }
            let (total, _, paid) = try balance(of: order)
            guard request.amount == total - paid else { throw failed }

            guard let room = try hotel.occupiedRoom(request.roomNumber) else {
                throw APIError.server(status: 400, message: "Chambre \(request.roomNumber) introuvable ou non occupée.")
            }
            let charge = request.amount + request.tipAmount
            guard room.currentBalance + charge <= room.maxCreditLimit else {
                throw APIError.server(status: 400, message: "Plafond de crédit chambre dépassé.")
            }
            let guest = request.guestName.trimmingCharacters(in: .whitespacesAndNewlines)
            try hotel.addToBalance(roomNumber: request.roomNumber, charge)
            try hotel.insertCharge(
                orderId: orderId, roomNumber: room.roomNumber, guestName: guest, amount: request.amount, tip: request.tipAmount,
                signature: request.signatureDataUrl, notes: request.notes
            )
            try closeOrder(order, as: .paid)
            return OperationResult(success: true, message: "Facturation de \(LocalMealVoucher.format(charge)) € enregistrée sur la chambre \(room.roomNumber) (\(guest))")
        }
    }
}
```

Dans `LocalSeeder.swift`, remplacer :
```swift
            try floor.insertDefaultSettings()
```
par :
```swift
            try floor.insertDefaultSettings()
            // Chambres de démonstration (comme `Program.SeedDatabase`) : à remplacer par l'accueil de l'établissement avant toute mise en service.
            let hotel = LocalHotelRepository(db: db)
            let now = Date()
            for room in seed.rooms { try hotel.insert(room, checkIn: now.addingTimeInterval(-86_400), checkOut: now.addingTimeInterval(3 * 86_400)) }
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Chambres d'hôtel (retiré par la tâche 6)` : ce commentaire, les 2 lignes `hotelRooms` et `chargeRoom`, et la ligne vide qui suit.

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHotelTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 4 tests … passed`.

- [ ] **Step 5: Mettre à jour `ios/README.md`**

Dans `ios/README.md`, section « Mode autonome (en cours) », remplacer le paragraphe qui décrit les plans livrés et à venir par :
```markdown
`LocalPosAPI` (`Packages/PosKit/Sources/PosKit/Local/`) implémente `PosAPI` sur une base SQLite locale, sans serveur .NET. Livré : authentification par PIN (avec verrouillage), personnel, catalogue, grille tactile, tables et réglages (plan 1a) ; commandes de salle, envoi cuisine et bons, remises et gratuités, transfert et fusion de tables (plan 1b) ; paiement non fiscal des tables et du comptoir (reçus `NF-…`, numéros de retrait, titres-restaurant et avoirs), mise en attente, facturation sur chambre (plan 1c). Les autres méthodes répondent `501` (« Non disponible en mode autonome ») jusqu'au plan 1d (imprimantes, Happy Hour, réseau, choix Serveur/Autonome au lancement) et aux sous-projets fiscaux.
```
Conserver la ligne suivante (commande de test) inchangée.

- [ ] **Step 6: Vérification finale, suivi, commit**

Run:
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"
cd ../../.. && dotnet build RestaurantPos.slnx 2>&1 | tail -3
```
Expected: `Test run with 277 tests … passed`, aucun avertissement ; `dotnet build` réussi (aucun fichier .NET modifié).

Mettre à jour `docs/plans/ipad-standalone-1c-progress.md` : tâche 6 `fait`, PosKit `277`, puis ajouter la section suivante :
```markdown
Conditions d'entrée du plan 1d (le mode devient sélectionnable : ces points ne sont plus « sans visiteur ») :
1. Verrouillage des PIN persistant (aujourd'hui en mémoire : relancer l'app remet le compteur à zéro) — reprendre `docs/plans/ipad-standalone-1a-progress.md`.
2. Pas de comptes ni d'identité de démonstration sur une installation réelle (PIN 1234 et 9999, SIRET de démo, chambres de démo) : création d'un responsable et de l'identité de l'établissement au premier lancement.
3. Une seule instance SQLite par fichier, `sqlite3_busy_timeout`, protection de fichier iOS, sauvegarde sûre avec le mode WAL.
4. Le terminal autonome s'appelle `T01` : les numéros de reçu `NF-T01-…` et de retrait `#A-…` en dépendent.
5. Les règlements `NonFiscalPayments` sont provisoires : le sous-projet 2 les remplace par `FiscalReceipts`/`PaymentTenders` signés (migration dédiée), et l'interface doit marquer le mode « non fiscal » tant que ce n'est pas fait.
```

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local ios/README.md docs/plans/ipad-standalone-1c-progress.md
git commit -m "feat(ios-local): facturation sur chambre d'hôtel et documentation du plan 1c"
```

---

## Self-Review

**1. Couverture de la spec (sous-projet 1)**
- Parcours complet d'un service au mode autonome : encaissement des tables (tâche 3), vente au comptoir et à emporter (tâches 4-5), mise en attente (tâche 4), chambres (tâche 6). La saisie, la cuisine, les remises et les transferts viennent du plan 1b.
- « Mêmes tables et colonnes que `AppDbContext` » : `HeldOrders`, `CustomerCreditVouchers`, `HotelRooms`, `RoomFolioCharges` reprennent les colonnes du .NET ; les deux tables propres à l'iPad (`NonFiscalPayments`, `LocalCounters`) sont signalées et justifiées (le fiscal est le sous-projet 2).
- Montants en centimes, jamais de `Double` : `Money` partout ; le seul `String(format:)` formate des entiers.
- Parité des messages avec le serveur : textes de `SharedResource.fr.resx` repris tels quels dans les tests.
- Mode « non fiscal » tant que le sous-projet 2 n'est pas terminé : reçus `NF-…`, pas de signature, condition d'entrée du plan 1d.

**2. Placeholders** : aucun « TBD »/« TODO » ; chaque étape de code contient le code complet ; les remplacements donnent le texte avant/après exact.

**3. Cohérence des types** : `paymentRepository`, `holdRepository`, `hotelRepository` (tâche 1) ; `settle(order:tenders:terminalId:)` et `closeOrder(_:as:)` (tâche 3) réutilisés par la tâche 5 et la tâche 6 ; `LocalMealVoucher.format(_:)` (tâche 5) réutilisé par la tâche 6 ; `ensurePinAttemptsAllowed()`/`recordFailedPin()`/`resetFailedPins()` (tâche 2) utilisés par la tâche 4 ; `APIError.localOrderClosed` (tâche 1) utilisé par les tâches 2 et ses tests des tâches 4 et 6.

**4. Review Focus** : les cinq points ont chacun un test nommé dans la tâche propriétaire (tâches 3, 4, 5, 6).

**5. Lacunes connues, hors test** : atomicité sous panne disque réelle (transactions, non simulée) ; expiration du verrouillage de 30 s (pas d'horloge injectable) ; journal fiscal (JET) de l'annulation d'une mise en attente (sous-projet 2) ; impression (`printQueued` toujours faux).

## Execution Handoff

Plan à relire avant toute exécution. Vérification prévue à la rédaction : les blocs « Créer / remplacer / supprimer » de ce document sont appliqués mécaniquement, tâche par tâche, sur une copie propre de PosKit à l'état de la branche du plan 1b, et `swift test` doit donner exactement 251, 252, 259, 266, 273 puis 277 tests verts, sans avertissement.
