# Annulation d'une ligne envoyée et correction d'une ligne non envoyée : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un manager ou admin annule une ligne déjà envoyée en cuisine (motif, bon imprimé, JET) ; n'importe quel opérateur corrige une ligne non envoyée ; annuler un reçu partiel rend la commande de nouveau encaissable.

**Architecture:** Une route `POST /api/orders/{orderId}/items/{itemId}/void` décide entre annulation et correction selon `IsDispatched`. La ligne est retirée de la commande (totaux et fixtures fiscales intacts) ; une copie va dans `OrderLineVoids` et dans le JET. Un lien `OrderItemId` relie l'article de bon cuisine à la ligne. Le mode autonome de l'iPad réimplémente la même logique dans `Local/` (migration V17).

**Tech Stack:** .NET 10 / EF Core / SQLite, JS vanilla, Swift 6 / SwiftUI / PosKit, Playwright, XCUITest.

**Spec:** `docs/superpowers/specs/2026-11-13-annulation-ligne-envoyee-design.md`

**Branche :** `feat/annulation-ligne` depuis `main`. Suivi : `docs/plans/annulation-ligne-progress.md` (statut, chiffres de tests .NET / PosKit / web, constats ouverts), mis à jour après chaque tâche. Suite complète avant de clore une tâche : `dotnet test RestaurantPos.slnx`, `./scripts/test.sh unit` (dans `ios/`), Playwright pour les tâches web.

## Global Constraints

- Annulation = ligne `IsDispatched` : `User.CanVoidItems()` (manager ou admin), motif obligatoire, quantité entre 1 et celle de la ligne, JET `ORDER_LINE_VOIDED` dans la même transaction, bon d'annulation mis en file **après** le `COMMIT`.
- Correction = ligne non envoyée : tout opérateur authentifié, sans motif, sans bon, sans JET.
- 409 `order_has_receipts` (numéros des reçus non annulés dans le message) si la commande a un reçu non annulé ; 409 si la commande n'est plus réglable.
- Après annulation d'un reçu : `Cancelled` seulement si son TTC égale le TTC de la commande (table libérée), sinon `BillRequested`. Le contrôle Z ignore les commandes `Cancelled`.
- Schéma .NET par `ApplySchema(dbContext, schemaLog, "…")` dans `Program.cs`. Côté iPad : nouvelle migration V17, jamais de modification d'un script publié.
- Fixtures fiscales inchangées. `ticket_document_reference.json` régénérée : `TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`. Textes : `ios/scripts/gen_ticket_texts.py` après toute modification de `.resx`.
- L'impression ne fait jamais échouer l'opération ; aucun vrai transport d'imprimante en test UI.
- i18n FR/EN/AR (chiffres arabes), `escapeHtml` pour tout texte saisi affiché en HTML, jetons sur les hubs.

## Review Focus

- Envoi en cuisine et correction simultanés : `IsDispatched` relu dans la transaction ; test deux appels concurrents.
- Annulation partielle (2 sur 3) et quantités invalides (0, supérieure à la ligne) : 400, rien n'est modifié.
- Ligne envoyée avant la fonction (`OrderItemId` nul) : JET écrit, bon imprimé, aucun article de bon marqué.
- Dernière ligne annulée : commande vide, total 0, aucun plantage, table libérable.
- Aucune imprimante résolue : l'annulation réussit, `printQueued: false`.
- Motif `<img src=x onerror=…>` : échappé dans le web, l'écran cuisine et le bon.
- Serveur (rôle Waiter) sur une ligne envoyée : 403, rien n'est modifié.

---

### Task 1: Annuler un reçu partiel rouvre la commande (M2, E14)

**Files:**
- Modify: `src/RestaurantPos.Infrastructure/Services/CheckoutPaymentService.cs:310-320` (`VoidReceiptAsync`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fiscal.swift:24` (`voidReceipt`) et `:104-114` (contrôle Z)
- Test: `tests/RestaurantPos.Api.Tests/VoidOrderStateTests.cs` (créer), `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalVoidReceiptTests.swift` (créer)

**Interfaces:**
- Produces: règle d'état de la commande après annulation de reçu (voir Global Constraints), sans changement de signature. Utilisée par la Tâche 2 (verrou par reçu) et ses tests.

- [ ] **Step 1: Tests .NET en échec** dans `VoidOrderStateTests` : `Void_PartialReceipt_ReopensOrderAsBillRequested` (commande 100 €, reçu de 40 €, annulation : `Status == BillRequested`) ; `Void_FullReceipt_CancelsOrder` (reçu de 100 € : `Cancelled`) ; `Void_FirstOfTwoReceipts_KeepsOrderPayable` (reçus 60 + 40, annuler le premier : `BillRequested`, reste dû 60 €).
- [ ] **Step 2:** `dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~VoidOrderStateTests` : attendu FAIL (statut `Cancelled` partout).
- [ ] **Step 3:** Dans `VoidReceiptAsync`, remplacer l'affectation inconditionnelle `order.Status = OrderStatus.Cancelled` : relire les lignes (`Include(o => o.Items)`) et comparer `original.TtcCents` (TTC signé du reçu d'origine) à `order.CalculateTotalTtc().AmountInCents` ; partiel donne `BillRequested`. Vérifier si le .NET libère déjà la table d'une commande `Cancelled` et le consigner dans le suivi.
- [ ] **Step 4:** Mêmes tests : PASS ; `dotnet test RestaurantPos.slnx` : tout vert hors échecs préexistants.
- [ ] **Step 5: Tests Swift en échec** dans `LocalVoidReceiptTests` : mêmes trois cas, plus `fullVoidFreesTable` (la table n'a plus `activeOrderId` après annulation d'un reçu qui couvrait tout) et `zClosureIgnoresCancelledOrder` (règlement partiel, annulation du reçu, clôture Z : réussit au lieu de 409).
- [ ] **Step 6:** Implémenter dans `voidReceipt` la même règle (comparer le TTC du reçu à celui de la commande, statut `BillRequested` ou `cancelled`, libérer la table sur `cancelled` via `closeOrder(_:as: .cancelled)`), et filtrer dans le contrôle Z les commandes dont `status(of:)` n'est pas modifiable. `cd ios && ./scripts/test.sh unit` : PASS.
- [ ] **Step 7:** Vérifier que `fiscal_reference.json`, `day_scenario.json` et `LocalDaySimulationTests` passent sans régénération. Commit : `fix(fiscal): annuler un reçu partiel rouvre la commande (M2, E14)`.

---

### Task 2: Serveur .NET : annulation, correction, trace, écran cuisine

**Files:**
- Modify: `src/RestaurantPos.Domain/Entities/KitchenTicket.cs` : `TicketItemStatus.Cancelled = 3` ; `KitchenTicketItem` gagne `Guid? OrderItemId` et `int CancelledQuantity`
- Create: `src/RestaurantPos.Domain/Entities/OrderLineVoid.cs` : `Id, OrderId, OrderItemId, TicketId?, ProductId, ProductName, Quantity, UnitPrice (Money), ModifiersSummary?, KitchenComment?, PreparationStationId?, Reason, OperatorId, OperatorName, TableNumber, VoidedAtUtc`
- Modify: `src/RestaurantPos.Domain/Entities/JournalEventTypes.cs` : `OrderLineVoided = "ORDER_LINE_VOIDED"`
- Modify: `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` : `DbSet<OrderLineVoid> OrderLineVoids`
- Modify: `src/RestaurantPos.Api/Program.cs` près de la ligne 563 : trois `ApplySchema` (`ALTER TABLE KitchenTicketItems ADD COLUMN OrderItemId TEXT NULL;`, `… ADD COLUMN CancelledQuantity INTEGER NOT NULL DEFAULT 0;`, `CREATE TABLE IF NOT EXISTS OrderLineVoids (…)`)
- Modify: `src/RestaurantPos.Infrastructure/Services/KitchenRoutingService.cs:~75-90` : renseigner `OrderItemId = item.Id` à la création de chaque article de bon
- Modify: `src/RestaurantPos.Application/Common/Interfaces/IKitchenRoutingService.cs` : `KitchenTicketItemDto` gagne `int CancelledQuantity = 0` en dernier paramètre
- Create: `src/RestaurantPos.Application/Common/Interfaces/IOrderLineVoidService.cs`, `src/RestaurantPos.Infrastructure/Services/OrderLineVoidService.cs` (+ enregistrement DI dans `Program.cs`)
- Modify: `src/RestaurantPos.Api/Endpoints/HospitalityEndpoints.cs` près de la route `/comp` (ligne 78)
- Test: `tests/RestaurantPos.Api.Tests/OrderLineVoidTests.cs` (créer)

**Interfaces:**
- Produces: `Task<OrderLineVoidResult> VoidAsync(Guid orderId, Guid orderItemId, int quantity, string? reason, Guid operatorId, CancellationToken ct = default)` avec `record OrderLineVoidResult(OrderLineVoidOutcome Outcome, Guid? VoidId, string? Detail)` et `enum OrderLineVoidOutcome { Voided, Corrected, NotFound, Forbidden, ReasonRequired, InvalidQuantity, OrderHasReceipts, NotModifiable }`. Le service charge l'opérateur (`Users`) et applique `CanVoidItems()` pour une ligne envoyée. Route : `POST /api/orders/{orderId}/items/{itemId}/void`, corps `VoidOrderItemRequest(int Quantity, string? Reason)`, réponses 200 / 400 / 403 / 404 / 409 (code `order_has_receipts`). Après le `COMMIT`, la route pousse `SendAsync("ReceiveKitchenUpdate", "ITEM_VOIDED", tableNumber)` comme `HospitalityEndpoints.cs:144`, puis (Tâche 3) met le bon en file avec `VoidId`.
- Consumes: règle de la Tâche 1 (reçu non annulé = verrou, via `PaymentSettlementRules.PaidCentsAsync`).

- [ ] **Step 1: Tests en échec** dans `OrderLineVoidTests` : `Void_SentLine_ByManager_RemovesLineWritesVoidAndJet`, `Void_SentLine_Partial_KeepsRemainder` (3 → 2), `Void_SentLine_ByWaiter_Returns403`, `Void_SentLine_WithoutReason_Returns400`, `Void_QuantityZeroOrAboveLine_Returns400AndChangesNothing`, `Void_OrderWithReceipt_Returns409OrderHasReceipts`, `Void_UnsentSavedLine_ByWaiter_RemovesWithoutJetOrVoidRow`, `Void_MarksKitchenTicketItem` (`CancelledQuantity` augmente, statut `Cancelled` quand tout est annulé), `Void_LegacyLineWithoutOrderItemId_StillWritesJet`, `Void_LastLine_LeavesEmptyOrderWithZeroTotal`, `Void_ConcurrentDispatchAndCorrection_ReadsIsDispatchedInTransaction`.
- [ ] **Step 2:** Lancer `--filter FullyQualifiedName~OrderLineVoidTests` : FAIL (types absents).
- [ ] **Step 3:** Entités, `DbSet`, `ApplySchema`, constante JET, `OrderItemId` au routage, DTO. Implémenter `VoidAsync` avec `_dbContext.ExecuteInTransactionAsync` (même modèle que `CompOrderItemAsync`), JET via `IFiscalJournal.AppendAsync<T>` avec une charge utile `{ orderId, tableNumber, productId, productName, quantity, unitPriceCents, reason, operatorId, operatorName, ticketId }`. Statut de commande modifiable : `PaymentSettlementRules.IsSettleable`.
- [ ] **Step 4:** Route et mappage des issues en codes HTTP ; DI. Tests : PASS.
- [ ] **Step 5:** Suite complète. Commit : `feat(commande): annulation d'une ligne envoyée et correction d'une ligne non envoyée (serveur)`.

---

### Task 3: Bon d'annulation imprimé

**Files:**
- Modify: `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs` près de `KitchenTicket(…)` (ligne 346)
- Modify: `src/RestaurantPos.Infrastructure/Printing/PrintDispatcher.cs` près de `QueueKitchenTicketsAsync` (ligne 83)
- Modify: `SharedResource.resx`, `SharedResource.fr.resx`, `SharedResource.ar.resx` (`src/RestaurantPos.Infrastructure/Localization/`) : `kitchen_ticket.cancellation_title`, `kitchen_ticket.cancelled_by`, `kitchen_ticket.cancellation_reason`
- Modify: `tests/RestaurantPos.Infrastructure.Tests/TicketDocumentReferenceFixtureTests.cs` (scénario de bon d'annulation) ; régénérer `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/ticket_document_reference.json`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Printing/TicketBuilder.swift`, `TicketTexts` (via `gen_ticket_texts.py`) ; tests de parité existants (`TicketBuilderReferenceTests`)
- Modify: `src/RestaurantPos.Api/Endpoints/HospitalityEndpoints.cs` (appel après `COMMIT`)

**Interfaces:**
- Produces: `static TicketDocument KitchenCancellation(OrderLineVoid line, string operatorName, string language)` (.NET) et `TicketBuilder.kitchenCancellation(_:operatorName:language:)` (Swift) ; `Task PrintDispatcher.QueueKitchenCancellationAsync(Guid voidId, CancellationToken ct = default)` : imprimante(s) du poste résolu (`PreparationStations.Resolve(line.PreparationStationId, produit, catégorie)`), type de job `PrintJobKind.KitchenTicket`, langue `KitchenTicketLanguage`, jamais de tiroir-caisse. Consommé par les Tâches 2 (route) et 6 (miroir Swift).
- Mise en page : titre `*** ANNULATION ***` en gras, puis table, serveur, « Annulé par », heure, `-N  Produit`, modificateurs, motif.

- [ ] **Step 1: Test en échec** : `KitchenCancellation_RendersTitleTableOperatorQuantityAndReason` (assertions sur les lignes du document) et le scénario de fixture ; `QueueKitchenCancellation_NoPrinter_DoesNotThrowAndQueuesNothing`.
- [ ] **Step 2:** Lancer ces tests : FAIL.
- [ ] **Step 3:** Clés `.resx` FR/EN/AR, `KitchenCancellation`, `QueueKitchenCancellationAsync` ; appeler depuis la route après `COMMIT` (échec d'impression journalisé, jamais propagé).
- [ ] **Step 4:** Régénérer la fixture (commande des contraintes), `python3 ios/scripts/gen_ticket_texts.py`, implémenter `kitchenCancellation` en Swift ; `cd ios && ./scripts/test.sh unit` et `dotnet test RestaurantPos.slnx` : PASS. Commit : `feat(impression): bon d'annulation en cuisine`.

---

### Task 4: Client web

**Files:**
- Modify: `src/RestaurantPos.Api/wwwroot/app.js` : rendu des lignes (`row.className = …`, ~2180-2260) pour le bouton et le retrait ; rendu de l'écran cuisine (~2553-2557) pour l'article annulé
- Modify: `src/RestaurantPos.Api/wwwroot/i18n.js` (FR/EN/AR) ; `index.html` (fenêtre motif et quantité si nécessaire)
- Test: `tests/RestaurantPos.Web.E2ETests/tests/line-void.spec.ts` (créer)

**Interfaces:**
- Consumes: route de la Tâche 2. Un seul appel `voidOrderItem(orderId, lineId, quantity, reason)` dans `app.js`.

- [ ] **Step 1: Spec Playwright en échec** : `manager voids a sent line and kitchen shows it cancelled`, `waiter sees the cancel action refused on a sent line`, `waiter corrects a saved unsent line (no reason asked)`, `reason with <img onerror> is escaped` (aucun script exécuté, texte visible).
- [ ] **Step 2:** `POS_WEB_URL=http://localhost:5080 npx playwright test tests/line-void.spec.ts` : FAIL.
- [ ] **Step 3:** Ligne envoyée : action « Annuler l'article » pour manager ou admin, fenêtre motif (obligatoire) et quantité ; pour un serveur, `item_locked_warning` précise « réservé au manager ». Ligne enregistrée non envoyée : `minus` appelle la route (correction) au lieu de ne modifier que le panier local. Tout texte inséré passe par `escapeHtml`. Écran cuisine : article barré et « ANNULÉ ».
- [ ] **Step 4:** Spec et suite Playwright complète : PASS (échec préexistant `happy-hour-multiselect` US4 toléré). Commit : `feat(web): annulation d'article envoyé et correction`.

---

### Task 5: iPad en mode serveur

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift:118`, `HTTPPosAPI.swift:466`, `Testing/InMemoryPosAPI.swift:873`
- Modify: modèles de bon cuisine (`TicketItemStatus` gagne `.cancelled`, `cancelledQuantity` décodé par `decodeIfPresent`) et fixtures de bons recapturées
- Modify: `ios/Packages/PosKit/Sources/PosKit/Stores/TicketStore.swift:163-181` (`decrement` / `remove`) et ajout de `voidLine`
- Modify: `ios/RestaurantPOS/Features/Order/OrderSheets.swift` (feuille d'annulation sur le modèle de l'offert, ligne 235), `Localizable.xcstrings` (FR/EN/AR), écran cuisine (article annulé)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/StoreTests.swift`, `ios/RestaurantPOSUITests/OrderFlowUITests.swift`

**Interfaces:**
- Produces (protocole `PosAPI`) : `func voidLine(orderId: UUID, lineId: UUID, quantity: Int, reason: String?, operatorId: UUID?) async throws` ; `TicketStore.voidLine(lineId: UUID, quantity: Int, reason: String?) async -> Bool`. Le mode autonome (Tâche 6) l'implémente dans `LocalPosAPI`.

- [ ] **Step 1: Tests en échec** : `voidLineRemovesSentLineAfterManagerConfirms`, `removeOnSavedUnsentLineCallsApiAsCorrection`, `waiterCannotSeeVoidActionOnSentLine`, `ticketItemDecodesCancelledStatusAndQuantity` ; XCUITest `testManagerCancelsSentLine` avec transport d'imprimante factice.
- [ ] **Step 2:** `./scripts/test.sh unit` : FAIL.
- [ ] **Step 3:** Protocole, `HTTPPosAPI` (route de la Tâche 2), `InMemoryPosAPI`, store (`remove` / `decrement` d'une ligne enregistrée non envoyée passent par la correction ; une ligne envoyée ouvre la feuille d'annulation pour manager ou admin), vue, chaînes localisées.
- [ ] **Step 4:** `./scripts/test.sh unit`, puis `./scripts/test.sh ui` ciblé sur le nouveau test : PASS. Commit : `feat(ios): annulation d'article envoyé et correction (mode serveur)`.

---

### Task 6: iPad en mode autonome

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV17.swift` (`ALTER TABLE KitchenTicketItems ADD COLUMN OrderItemId TEXT NULL`, `… CancelledQuantity INTEGER NOT NULL DEFAULT 0`, `CREATE TABLE OrderLineVoids` identique à l'entité .NET) ; enregistrer dans le migrateur après V16
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+LineVoid.swift` (`voidLine`) ; modifier `LocalKitchenRepository.swift:36` pour renseigner `OrderItemId`
- Modify: `LocalPosAPI+PrintDispatch.swift` (mise en file du bon après `COMMIT`)
- Test: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalLineVoidTests.swift` (créer)
- Modify: `CLAUDE.md` (migrations jusqu'à V17), `docs/parite-dotnet-swift.md`, `docs/plans/annulation-ligne-progress.md`

**Interfaces:**
- Consumes: protocole de la Tâche 5 ; règles de la Tâche 2 ; `TicketBuilder.kitchenCancellation` de la Tâche 3. Mêmes verrous que l'offert local : `requireManager()` pour une ligne envoyée, `isModifiable`, `paidCents == 0` (sinon `APIError.localOrderPartiallyPaid`).

- [ ] **Step 1: Tests en échec** miroir de la Tâche 2 : `voidSentLineByManagerWritesJetAndVoidRow`, `partialVoidKeepsRemainder`, `waiterCannotVoidSentLine`, `voidWithReceiptIsRefused`, `unsentLineCorrectionWritesNoJet`, `legacyLineWithoutOrderItemIdStillPrintsAndWritesJet`, `voidQueuesCancellationJobAfterCommit` (faux transport), `migrationV17AppliesOnV16Database`.
- [ ] **Step 2:** `./scripts/test.sh unit` : FAIL.
- [ ] **Step 3:** Migration V17, `voidLine` dans une seule transaction (retrait, ligne `OrderLineVoids`, JET `ORDER_LINE_VOIDED`, marquage de l'article de bon), mise en file après `COMMIT` ; `printQueued` réel.
- [ ] **Step 4:** `./scripts/test.sh unit`, XCUITest `-UITestLocal` ciblé : PASS ; `LocalDaySimulationTests` et fixtures fiscales inchangées. Documentation et suivi à jour. Commit : `feat(autonome): annulation d'article envoyé et correction, migration V17`.

---

## Self-review

- **Couverture de la spec :** intention, correction et annulation (Tâches 2, 4, 5, 6) ; bon et KDS (2, 3) ; verrou par reçu et règle M2/E14 (1, 2) ; migration V17 et parité (6) ; lignes héritées et concurrence (Review Focus, tests Tâches 2 et 6) ; tests attendus (chaque tâche).
- **Cohérence des noms :** `OrderLineVoid`, `OrderLineVoidOutcome`, `KitchenCancellation`, `QueueKitchenCancellationAsync`, `voidLine`, `ORDER_LINE_VOIDED`, `order_has_receipts` identiques dans toutes les tâches.
- **Reste à confirmer à l'exécution :** libération de la table côté .NET après `Cancelled` (Tâche 1), nom exact des helpers de transaction et de JET côté Swift (Tâche 6), emplacement du rôle de l'opérateur dans l'état du client web (Tâche 4).
