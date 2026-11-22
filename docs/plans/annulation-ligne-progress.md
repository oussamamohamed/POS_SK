# Annulation d'une ligne envoyée et correction : avancement

Spec : `docs/superpowers/specs/2026-11-13-annulation-ligne-envoyee-design.md`. Branche `feat/annulation-ligne`. Tests : .NET (Domain / Infrastructure / Api) / PosKit / web.

| Tâche | État | Commits | Tests |
|-------|------|---------|-------|
| 1. Règle d'état de commande après annulation d'un reçu (R6, R8) | terminée, relue, approuvé | 4d502f3, 480c660, d226609 | .NET 85 / 513 / 358, PosKit 810 |
| 2. Service d'annulation de ligne (.NET), `OrderLineVoid` | terminée, relue, approuvé | b3b0f92 | .NET 85 / 513 / 375, PosKit 812, PosPrinters 41 |
| 3. Bon d'annulation en cuisine (.NET + `TicketBuilder.kitchenCancellation`) | terminée, relue, approuvé | 0c4aa64 | .NET 85 / 519 / 377, PosKit 812, PosPrinters 41 |
| 4. Client web | terminée, relue, approuvé | 3fb5ca2 | line-void 4/4, Playwright 235 OK / 1 KO préexistant |
| 5. iPad mode serveur (`PosAPI.voidLine`) | terminée, relue, approuvé | c90ee78 | PosKit 816, PosPrinters 41, XCUITest `testManagerCancelsSentLine` |
| 6. iPad mode autonome (V17, `LocalPosAPI+LineVoid`) | terminée, relue, approuvé | d10740c | PosKit 824, PosPrinters 41, XCUITest `-UITestLocal` (`testModeChoiceThenFirstRunThenUnlock`, `testFullTableJourneyWorksOnTheLocalBackend`) ; .NET inchangé |

## Simulation de journée

Commit `test(simulation): annulation et correction d'article dans la journée simulée`. Onze commandes imposées `v0` à `v10` ajoutées après les tirages aléatoires (les 74 premières opérations et leurs valeurs ne bougent pas), sur les postes A, B, C : 21 étapes, 13 annulations acceptées (dont une ligne ancienne sans `OrderItemId`), 2 corrections par le serveur avant envoi (aucune trace), 6 refus (403 serveur, 400 motif absent ou blanc, 400 quantité au-delà du reste, 409 `order_has_receipts` avec numéros, 409 `order_not_modifiable`). Cas : annulation totale ; deux annulations partielles d'une ligne à suppléments (R9) dont la seconde au-delà du reste ; annulation partielle puis même produit rajouté sur un second bon et annulé ; correction puis envoi ; offert et remise de 10 % ; vente comptoir à emporter envoyée ; note partagée (refus après le premier et après le dernier règlement) ; premier reçu annulé puis annulation acceptée et solde ; commande vidée (remise fixe bornée, total 0, sans reçu, ne bloque pas la Z) ; dernière commande avant la Z.

Attentes (oracle indépendant `DayLedgerOracle` : `LinesAfter`, `TotalsAfterSteps`, `ExpectedLineVoids`, `TicketLines`) : statut et code de chaque étape, total TTC renvoyé, X du poste identique avant et après chaque étape, X et Z des 3 postes = grand livre, `ORDER_LINE_VOIDED` (13, champs de la charge utile), lignes `OrderLineVoids` (R2 serveur de la table, vide au comptoir ; R9), articles de bon (`Quantity`, `CancelledQuantity`, statut), un bon d'annulation par annulation et aucun pour une correction ou un refus, `RECEIPT_VOIDED` compte le reçu de la commande rouverte, chaînes valides. Fixtures régénérées : `day_scenario.json` (ajouts, totaux A +59,00 €, B +94,27 €, C +53,00 €, nouvelles sections `expectedLineVoids`, `expectedLineVoidTotals`, `expectedTicketLines`), `report_print_data_reference.json` (section `day` seulement : articles, pourboire, remise et annulation de reçu des nouvelles commandes). Aucun bogue trouvé. Tests : .NET 85 / 519 / 383, PosKit 826, PosPrinters 41. Rapport : `.superpowers/sdd/2026-11-14-annulation-ligne-envoyee/simulation-report.md`.

## Constats de revue ouverts (différés)

- T1 : `decorate(order).totalTtcAmount?.cents == original.ttcCents` (Swift) ; `closeOrder(_, as: .cancelled)` appelle aussi `reservationRepository.completeSeated` (absent du .NET) ; pas de test « annuler tous les reçus » ni avec remise globale / pourboire ; le re-lien de la table ne restaure pas `CoversCount` (et vaut pour la table « Comptoir »).
- T1 (hors périmètre, R8) : table prise par une autre commande, la commande rouverte reste sans table ; la Z iPad (par table) ne la voit pas alors que le .NET (par commande) la bloque.
- R7 (parqué) : un rapport qui lit l'état de la commande compte une commande rouverte après annulation d'une part.
- T2 : « non réglable » est contrôlé avant « reçus » (commande entièrement payée : `order_not_modifiable` au lieu de `order_has_receipts`) ; `OrderLineVoid.Reason` sans longueur maximale ; recherche de l'article de bon sans `OrderBy` ; dernière ligne annulée = commande vide sur table occupée, le bon garde son statut.
- T3 : le bon imprime `TABLE Comptoir` pour une vente comptoir/emporter au lieu de « À EMPORTER » + numéro de retrait ; `ILogger<PrintDispatcher>` injecté dans la route ; catch 7002 quasi mort ; filtrage imprimantes/stations copié (2 sites).
- T4 : pas de test du rendu partiel KDS « (ANNULÉ n) », ni du `minus` manager, ni du toast 409 ; helpers dupliqués de `tests/helpers/pairing.ts` ; `line-void.spec.ts` laisse 3 commandes V* ouvertes.
- T4 (préexistant) : Playwright `daily-summary` projet Chromium en échec (Z 409 `open_orders`, commandes P*/R* laissées ouvertes par d'autres specs ; confirmé sans line-void : 227 OK / 1 KO).
- T5 : `InMemoryPosAPI` sans branche `order_not_modifiable` ; pas de test API des branches 403/400/409 ; `TicketStore.voidLine` échoue en silence si busy / `serverLineId` manquant ; article de bon apparié par nom de produit (test double) ; xcstrings non triées ; fixture `kds_tickets.json` éditée à la main.
- T6 : le bon imprime aussi `TABLE Comptoir` pour une vente comptoir (même limite que T3) ; `LocalPosAPI.voidLine` ne lit pas `operatorId` (opérateur de la session) ; pas de test local du rejet « commande non modifiable » (409).
- Échecs préexistants tolérés : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders` (date), happy-hour-multiselect US4 (Playwright), flake minuit Infrastructure.

## Revue finale

Commit `fix(annulation): garde anti double envoi, texte FR, tests de parité et suivi` :
- Web : bouton du modal désactivé pendant l'appel, correction `minus` ignorée tant que l'appel de la même ligne est en vol (plus de double annulation sur double tap). Pas de test Playwright dédié (non trivial).
- FR : « annulation réservée au manager » (web `order.item_locked_warning`, assertion de `line-void.spec.ts` ; le Swift disait déjà « réservée »). Clé `line_void.corrected` supprimée des 3 locales.
- PosKit : test paire reçu + avoir (n'empêche pas l'annulation) suivi de `verifyChains()` valide ; `waiterCannotVoidSentLine` (403) et `voidWithReceiptIsRefused` (409 + numéros de reçus) assertent l'erreur précise.

Tests finaux : .NET 85 / 519 / 377 (Domain / Infrastructure / Api), PosKit 825, PosPrinters 41, Playwright `line-void.spec.ts` 8/8 (API sur base temporaire). Suite Playwright complète et XCUITest non relancés.

Mineurs différés restants : tous ceux listés ci-dessus (T1 à T6), plus :
- Échec préexistant `daily-summary` (Playwright, Z 409 `open_orders`) et `ReportAccuracyTests.FinancialDashboard_DiscountedOrders` (date).
- Limite du bon d'annulation : en-tête « TABLE Comptoir » au lieu de « À EMPORTER » (+ numéro de retrait) pour une vente comptoir/emporter, côté .NET et autonome.

## Audit des lecteurs (après annulation d'un reçu)

États : (a) rouverte après annulation d'une part, non re-réglée ; (b) rouverte puis re-réglée ; (c) annulation intégrale, commande `Cancelled` ; (d) avoir sur une commande qui garde d'autres reçus actifs ; (e) double annulation ; (f) pourboire réparti sur plusieurs reçus ; (g) titres-restaurant / moyens mixtes, règlements ≠ TTC du reçu. Verdict identique .NET / Swift sauf mention.

| Lecteur (.NET / Swift) | a | b | c | d | e | f | g | Test | Correctif |
|---|---|---|---|---|---|---|---|---|---|
| Commandes ouvertes de la Z (`FindOpenOrdersAsync` / `LocalPosAPI.zClosure`) | .NET OK ; Swift **FAUX** (par table : commande sans table ignorée) | OK | OK | = a | OK | Swift **FAUX** (reste sans pourboire) | OK | `zClosureCountsTheTipOfTheActiveShareOfAReopenedOrder`, `zClosureSeesAReopenedOrderWhoseTableWasTaken`, journée v26/v27 | Swift : par commande, reste = TTC + pourboire − règlements actifs |
| Libération de table au règlement soldé (`CheckoutPaymentService` / `closeOrder`) | — | .NET **FAUX** (libère la table et la réservation de l'autre client) ; Swift OK | OK | OK | — | OK | OK | `Pay_ReopenedOrderWhoseTableWasTaken_LeavesTheTableToTheOtherOrder`, journée v27 | .NET : ne libère que la table vide ou liée à la commande |
| Mise en attente (`/api/orders/counter/hold` / `holdOrder`) | .NET **FAUX** (acceptée, l'annulation de l'attente annulait une commande encaissée) ; Swift OK | — | — | = a | — | — | — | `Hold_ReopenedOrderWithAnActiveReceipt_IsRefused` | .NET : 409 `errors.hold_partially_paid` |
| X / Z, totaux et TVA (`GenerateXReportAsync`, `ExecuteDailyZClosureAsync` / `summary`) | OK | OK | OK | OK | OK | OK | OK | journée (X/Z = grand livre) | — |
| Articles et pourboires du ticket X/Z (`ReportPrintDataService` / `reportPrintData`) | OK (comme un règlement partiel : toutes les lignes ; Z impossible) | OK | OK | OK | — | OK | OK | `report_print_data_reference.json` (v26–v28) | — |
| FEC (`FecExportService` / `LocalFecExporter`), compte 468600 | OK | OK | OK | OK | — | OK | OK | `TheFecTipAccountNetsToTheTipsKept`, équilibre par écriture | — |
| Archives, clôtures de période, vérification des chaînes | OK (par reçu / par clôture) | OK | OK | OK | OK | OK | OK | `ChainsAreIntactAndTheJournalHasEveryEvent` | — |
| Encaissements (`FinancialDashboardService` / `takings`) | OK (reçu actif compté ; familles hors commande non réglée, comme un partiel) | OK | OK | OK | — | OK | OK | journée, `TakingsDailySummaryAndMenuAnalysis_…` | — |
| Résumé de Z (`InsightsService` / `dailySummary`), analyse du menu, alertes | OK | OK | OK | OK | — | OK | — | journée | — (99105fd) |
| Stock (`StockConsumption` / `LocalStockRepository`) | OK (rien restitué) | OK (vente idempotente par commande) | OK (restitution si demandée) | OK | — | — | — | `StockDaySimulationTests` | — ; deux parts annulées : décision D1 |
| Espèces attendues (`CashSessionService` / `LocalCashRepository`) | OK (avoir espèces négatif) | OK | OK | OK | — | OK | OK | `CashDaySimulationTests` | — ; décision D2 |
| Plan de salle, total de table, montant dû du ticket (`TableManagementService`, `TicketStore`, web) | total TTC, pas le reste dû | OK | OK | = a | — | — | — | — | non corrigé (contrat DTO) : D5 |
| Titres-restaurant (`MealVoucherPolicyService`) | sans historique : sans effet | OK | — | — | — | — | OK | — | — |
| Double annulation | — | — | — | — | OK (refusée, 400 / erreur) | — | — | `Void_DoubleVoid_…`, v18, v25 | — |

Décisions validées par l'utilisateur le 2026-11-22 : D1 à D4 et D6. D5 résolu (voir « Reste dû de la commande active » ci-dessous). Rappel des décisions : D1 deux parts annulées = commande toujours due (`BillRequested`, pas `Cancelled`), restitution du stock ignorée, la Z reste bloquée jusqu'au re-règlement ou au vidage par annulations de ligne ; D2 un avoir espèces réduit l'attendu de la session de caisse en cours, même pour une vente d'une session précédente (comme une annulation intégrale) ; D3 une commande rouverte non re-réglée compte toutes ses lignes dans les articles du X et les familles des encaissements dès qu'un reçu actif existe (comme un règlement partiel ordinaire) ; D4 une commande rouverte restée sans table n'est atteignable que par son identifiant (aucun écran ne la liste, seule la Z refusée la nomme) ; D5 après rechargement, le ticket (iPad, web) propose le TTC entier et non le reste dû (le DTO de commande ne porte pas les règlements) ; D6 table d'une commande rouverte `Occupied`, pas « addition demandée ».

Journée simulée : v26 (A, pourboire 12 € > part annulée 10 €, Z refusée reste 10 €, re-règlement titre-restaurant + espèces avec rendu), v27 (B, table reprise avant l'annulation de la 2e part, Z refusée reste 14 €, table conservée après annulation et après règlement), v28 (C, deux taux, deux parts annulées dont celle du pourboire, Z refusée reste 25 €, re-règlement carte + espèces). Fixtures : `day_scenario.json` (ajouts : 3 opérations avant v19, champs `occupyTable` / `zBlocked`, section `expectedZBlocked` ; totaux A +3 000 c / 4 reçus, B +2 800 c / 4, C +2 500 c / 5), `report_print_data_reference.json` (section `day` : articles, pourboire waiter1 +1 200 c, annulations +1/+1/+2). `fiscal_reference.json` régénérée sans changement ; stock, caisse, tendances, FEC, archives, clôtures : entrées inchangées. Commits ffb7eeb, ec6d65a, 7c77ca4. Tests : .NET 85 / 522 / 508 (1 échec d'environnement `GetNetworkInfo_ReturnsServerInfo`, résolution DNS du nom d'hôte), PosKit 834, PosPrinters 41.

## Reste dû de la commande active (D5, résolu)

`ActiveTableOrderDto` gagne `AmountPaidTtc` et `AmountDueTtc` (ajoutés en fin, valeurs par défaut 0) ; `PaymentSettlementRules.DueCents` (TTC + pourboire conservé − règlements actifs, jamais négatif) est partagé avec `FindOpenOrdersAsync`. Miroirs Swift : `LocalPosAPI.activeOrder(on:)` et `InMemoryPosAPI`. Clients : iPad `TicketStore.hydrate` (reste dû et « Déjà réglé » dès qu'un règlement existe), web `hydrateCartFromOrder` (`state.amountPaid`, ligne `#payAlreadyPaid`, clé `payment.already_paid` FR/EN/AR). `settleOpenTableOrders` (helper Playwright) encaisse le reste dû et non plus le TTC. Tests : `ActiveOrderDueTests` (.NET, 5 : non réglée, en partie réglée, rouverte puis re-réglée, pourboire conservé / retiré, offert et remise, chaque cas comparé au reste de la Z), `ActiveOrderDueTests` (PosKit : 8 cas autonomes, 4 en mémoire, rechargement du ticket), `ContractDecodingTests` (ancienne et nouvelle forme), Playwright `due-after-void.spec.ts`. Non aligné : `InMemoryPosAPI.voidReceipt` reste un bouchon (cas d'annulation non joués en mémoire) et sa Z reste par table (le pourboire y était déjà compté) ; l'ajout d'un statut de commande dépasse « trivial ». Rapport : `.superpowers/sdd/2026-11-14-annulation-ligne-envoyee/d5-report.md`.
Tests : .NET 85 / 522 / 513, PosKit 837, PosPrinters 41, Playwright 14/14 (`due-after-void`, `line-void`, `daily-summary`, `split-bill`, deux projets, API sur base temporaire).
