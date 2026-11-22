# Annulation d'une ligne envoyée et correction : avancement

Spec : `docs/superpowers/specs/2026-11-13-annulation-ligne-envoyee-design.md`. Branche `feat/annulation-ligne`. Tests : .NET (Domain / Infrastructure / Api) / PosKit / web.

| Tâche | État | Commits | Tests |
|-------|------|---------|-------|
| 1. Règle d'état de commande après annulation d'un reçu (R6, R8) | terminée, relue | 4d502f3, 480c660, d226609 | .NET 85 / 513 / 358, PosKit 810 |
| 2. Service d'annulation de ligne (.NET), `OrderLineVoid` | terminée, relue | b3b0f92 | .NET 85 / 513 / 375, PosKit 812, PosPrinters 41 |
| 3. Bon d'annulation en cuisine (.NET + `TicketBuilder.kitchenCancellation`) | terminée, relue | 0c4aa64 | .NET 85 / 519 / 377, PosKit 812, PosPrinters 41 |
| 4. Client web | terminée, relue | 3fb5ca2 | line-void 4/4, Playwright 235 OK / 1 KO préexistant |
| 5. iPad mode serveur (`PosAPI.voidLine`) | terminée, relue | c90ee78 | PosKit 816, PosPrinters 41, XCUITest `testManagerCancelsSentLine` |
| 6. iPad mode autonome (V17, `LocalPosAPI+LineVoid`) | terminée, revue à faire | `feat(autonome): annulation d'article envoyé et correction, migration V17` | PosKit 824, PosPrinters 41, XCUITest `-UITestLocal` (`testModeChoiceThenFirstRunThenUnlock`, `testFullTableJourneyWorksOnTheLocalBackend`) ; .NET inchangé |

## Constats de revue ouverts (différés)

- T1 : `decorate(order).totalTtcAmount?.cents == original.ttcCents` (Swift) ; `closeOrder(_, as: .cancelled)` appelle aussi `reservationRepository.completeSeated` (absent du .NET) ; pas de test « annuler tous les reçus » ni avec remise globale / pourboire ; le re-lien de la table ne restaure pas `CoversCount` (et vaut pour la table « Comptoir »).
- T1 (hors périmètre, R8) : table prise par une autre commande, la commande rouverte reste sans table ; la Z iPad (par table) ne la voit pas alors que le .NET (par commande) la bloque.
- R7 (parqué) : un rapport qui lit l'état de la commande compte une commande rouverte après annulation d'une part.
- T2 : « non réglable » est contrôlé avant « reçus » (commande entièrement payée : `order_not_modifiable` au lieu de `order_has_receipts`) ; `OrderLineVoid.Reason` sans longueur maximale ; recherche de l'article de bon sans `OrderBy` ; dernière ligne annulée = commande vide sur table occupée, le bon garde son statut.
- T3 : le bon imprime `TABLE Comptoir` pour une vente comptoir/emporter au lieu de « À EMPORTER » + numéro de retrait ; `ILogger<PrintDispatcher>` injecté dans la route ; catch 7002 quasi mort ; filtrage imprimantes/stations copié (2 sites).
- T4 : pas de garde « en cours » sur le submit du modal ni sur la correction `minus` (double tap = double annulation partielle, 2 entrées JET) : à corriger en revue finale ; « annulation réservé » -> « réservée » (fr `order.item_locked_warning`, la spec exige « réservé au manager » : changer les deux) ; clé `line_void.corrected` inutilisée ; pas de test du rendu partiel KDS « (ANNULÉ n) », ni du `minus` manager, ni du toast 409 ; helpers dupliqués de `tests/helpers/pairing.ts` ; `line-void.spec.ts` laisse 3 commandes V* ouvertes.
- T4 (préexistant) : Playwright `daily-summary` projet Chromium en échec (Z 409 `open_orders`, commandes P*/R* laissées ouvertes par d'autres specs ; confirmé sans line-void : 227 OK / 1 KO).
- T5 : `InMemoryPosAPI` sans branche `order_not_modifiable` ; pas de test API des branches 403/400/409 ; `TicketStore.voidLine` échoue en silence si busy / `serverLineId` manquant ; article de bon apparié par nom de produit (test double) ; xcstrings non triées ; fixture `kds_tickets.json` éditée à la main.
- T6 : le bon imprime aussi `TABLE Comptoir` pour une vente comptoir (même limite que T3) ; `LocalPosAPI.voidLine` ne lit pas `operatorId` (opérateur de la session) ; pas de test local du rejet « commande non modifiable » (409).
- Échecs préexistants tolérés : `ReportAccuracyTests.FinancialDashboard_DiscountedOrders` (date), happy-hour-multiselect US4 (Playwright), flake minuit Infrastructure.
