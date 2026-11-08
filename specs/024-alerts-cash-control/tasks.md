---
description: "Tâches : Alertes de pilotage et contrôle de caisse"
---

# Tâches : Alertes de pilotage et contrôle de caisse

**Entrée** : `specs/024-alerts-cash-control/` (spec.md, plan.md, research.md, data-model.md, contracts/api.md, quickstart.md)

**Prérequis** : plan.md, spec.md. Constitution 2.0.0 respectée (plan.md, tableau de vérification).

**Tests** : inclus (constitution, parité .NET/Swift, journée simulée).

**Organisation** : par histoire. Lots du plan : A = US1 (caisse), B = US2 (alertes), C = US3 (tendances), D = US4 (résumé). Chaque histoire se livre et se teste seule.

## Format : `[ID] [P?] [Histoire] Description avec chemin`

- **[P]** : parallélisable (fichiers différents, aucune dépendance sur une tâche inachevée)
- **[USn]** : histoire de spec.md
- Notation courte : `Api.Tests` = `tests/RestaurantPos.Api.Tests`, `Infra.Tests` = `tests/RestaurantPos.Infrastructure.Tests`, `Domain.Tests` = `tests/RestaurantPos.Domain.Tests`, `PosKit` = `ios/Packages/PosKit/Sources/PosKit`, `PosKitTests` = `ios/Packages/PosKit/Tests/PosKitTests`, `web` = `src/RestaurantPos.Api/wwwroot`

## Règles transverses (valables pour toutes les tâches)

- Montants en centimes entiers, jamais de `float` ni de `double`. Arrondis identiques en .NET et en Swift.
- Chaque texte visible est écrit en FR, EN et AR (web `i18n/{fr,en,ar}.json`, iPad `Localizable.xcstrings`).
- **Aucune modification** de `FiscalHashing`, des chaînes de reçus ou de Z, du contenu signé du rapport Z. Les nouveaux types de journal technique s'ajoutent sans changer la formule de la chaîne.
- Sessions, mouvements et comptages de caisse : ajout seulement (déclencheurs `cash_append_only`).
- Les routes d'écriture de caisse exigent `X-Device-Token` ; accès aux comptages et alertes réservé aux responsables.
- L'évaluation des alertes a lieu **après** le `COMMIT` métier et ne bloque ni n'annule jamais une vente, une annulation ou une remise.
- Après chaque tâche : mettre à jour `docs/plans/alerts-cash-progress.md` (statut, décomptes .NET / PosKit / web, constats ouverts).
- Avant de marquer une tâche faite : suite complète verte (`dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit`, et Playwright quand le web est touché).

---

## Phase 1 : Mise en place

- [X] T001 Créer `docs/plans/alerts-cash-progress.md` (tableau des tâches, décomptes de tests, constats ouverts)
- [X] T002 Lancer les suites actuelles (`dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit`) et noter les décomptes de départ
- [X] T003 Lever la décision « `PaymentTender.Amount` avec ou sans rendu » : lire `src/RestaurantPos.Infrastructure/Services/CheckoutPaymentService.cs`, `src/RestaurantPos.Domain/Entities/PaymentTender.cs` et `PosKit/Local/LocalFiscalRepository.swift`, consigner dans `research.md` la formule exacte de l'espèce gardée (`Tendered − ChangeGiven` ou `Amount`), y compris pour les avoirs payés en espèces et la facturation chambre. Toutes les tâches de calcul d'attendu en dépendent.
- [X] T004 Vérifier la numérotation iPad : lire `PosKit/Local/LocalMigrator.swift`, noter la prochaine version libre (V15 si la spec 023 n'a pas encore livré V13 et V14, sinon la suivante) et l'inscrire dans `docs/plans/alerts-cash-progress.md`

---

## Phase 2 : Fondations (bloquent toutes les histoires)

**Objectif** : tables, entités, journal et temps réel posés des deux côtés pour que chaque histoire se code sans toucher au schéma.

- [X] T005 [P] Créer les entités `src/RestaurantPos.Domain/Entities/CashSession.cs`, `CashMovement.cs`, `CashCount.cs`, `AlertRule.cs`, `Alert.cs` (champs de data-model.md) et les enregistrer dans `src/RestaurantPos.Infrastructure/AppDbContext.cs`
- [X] T006 `src/RestaurantPos.Api/Program.cs` : `CREATE TABLE IF NOT EXISTS` pour les cinq tables, index unique partiel « une seule session sans comptage par poste », index unique `DedupeKey`, déclencheurs `RAISE(ABORT,'cash_append_only')` sur UPDATE et DELETE de `CashSessions`, `CashMovements`, `CashCounts`, lignes `AlertRules` par défaut avec `Enabled = 0`, colonnes `TrendThresholdPercent` (15) et `CashVarianceNoteRequired` (1) sur `RestaurantSettings`
- [X] T007 [P] `src/RestaurantPos.Domain/Entities/JournalEventTypes.cs` : `CASH_SESSION_OPENED`, `CASH_MOVEMENT`, `CASH_COUNTED`, `ALERT_CREATED`, `ALERT_RESOLVED` (journal technique non fiscal, même formule de chaîne) ; test `Infra.Tests` qui vérifie que la chaîne du JET reste valide avec ces nouveaux types
- [X] T008 [P] Libellés `journal.<TYPE>` pour les cinq types dans `web/i18n/{fr,en,ar}.json` et `ios/RestaurantPOS/Localizable.xcstrings`
- [X] T009 [P] Interfaces `src/RestaurantPos.Application/Common/Interfaces/ICashSessionService.cs`, `IAlertService.cs`, `IInsightsService.cs`
- [X] T010 [P] `src/RestaurantPos.Api/Hubs/IPosHubClient.cs` et `PosHub.cs` : `OnAlertsChanged()` (ajout, aucun nom existant renommé)
- [X] T011 `PosKit/Local/LocalMigrator.swift` : migration (version notée en T004) créant les mêmes tables, index et déclencheurs que T006 (colonnes identiques à `AppDbContext`) ; test `PosKitTests/Local/LocalCashMigrationTests.swift` : migration depuis la version précédente, déclencheurs actifs, un deuxième comptage de la même session refusé
- [X] T012 [P] `PosKit/Networking/` : `RealtimeEvent.alertsChanged` et abonnement au nom exact `OnAlertsChanged`

**Point de contrôle** : build vert (avertissements = erreurs), migration testée, chaîne JET valide.

---

## Phase 3 : Histoire 1 : Comptage de caisse et écart (P1) 🎯 MVP

**Objectif** : ouvrir une session avec un fond, encaisser, saisir des sorties, compter à la clôture, voir l'écart et le garder en historique.

**Test indépendant** : fond 100 €, encaissement espèces 50 €, sortie 12 €, comptage : l'écart se calcule et reste consultable.

### Tests

- [X] T013 [P] [US1] `Infra.Tests/CashSessionServiceTests.cs` : attendu = fond + espèces gardées (formule de T003) + entrées − sorties − remboursements espèces ; une seule session ouverte par poste (`cash_session_already_open`) ; comptage idempotent (même résultat si rejoué) ; `variance_note_required` quand l'écart dépasse le seuil actif et que la note est vide ; motif de sortie obligatoire (3 à 200 caractères) ; fond jamais déclaré = attendu part de zéro
- [X] T014 [P] [US1] `Infra.Tests/CashAppendOnlyTests.cs` : `UPDATE` et `DELETE` directs sur les trois tables échouent avec `cash_append_only`
- [X] T015 [P] [US1] `Api.Tests/CashEndpointsTests.cs` : jeton d'appareil requis, session lisible par son ouvreur et par le responsable seulement, historique `GET /api/cash/sessions` réservé au responsable, un serveur ne voit pas la session d'un autre, `terminalId` pris de l'appareil
- [X] T016 [P] [US1] `Api.Tests/CashJournalTests.cs` : chaque ouverture, sortie et comptage écrit son événement JET (FR-012)
- [X] T017 [P] [US1] `Infra.Tests/CashReferenceFixtureTests.cs` : génère `PosKitTests/Fixtures/cash_reference.json` (`WRITE_CASH_FIXTURE=1`), couvrant espèces avec rendu, avoirs espèces, entrées et sorties, écarts positifs et négatifs, comptage par coupures

### Serveur

- [X] T018 [US1] `src/RestaurantPos.Infrastructure/Services/CashSessionService.cs` : ouverture, mouvements, `expectedCents` calculé à la lecture à partir de `PaymentTender` (aucune écriture dans les reçus), comptage figeant attendu et écart, comptage idempotent, entrées de journal
- [X] T019 [US1] `src/RestaurantPos.Api/Endpoints/CashEndpoints.cs` : `POST /api/cash/sessions`, `GET /api/cash/sessions/current`, `POST …/{id}/movements`, `POST …/{id}/count`, `GET /api/cash/sessions` (responsable) ; DI dans `Program.cs`
- [X] T020 [US1] `GET/PUT /api/settings` : `trendThresholdPercent` et `cashVarianceNoteRequired`
- [X] T021 [US1] Rapport Z : `GET /api/fiscal/z-report/last` gagne `cashFloat`, `cashCounted`, `cashVariance` facultatifs, non signés, lus depuis `CashCounts` ; test que la signature et la chaîne du Z restent inchangées (FR : « le comptage et l'écart y figurent sans modifier la chaîne fiscale »)
- [X] T022 [US1] Ticket Z imprimé : ajouter les trois valeurs dans `TicketDocumentBuilder` (.NET) quand elles existent, textes dans les `.resx`, régénérer `ticket_document_reference.json` (`TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 …`) et `ios/scripts/gen_ticket_texts.py`, puis porter dans `PosKit/Printing/` (`TicketBuilderReferenceTests` verts). Si le Z imprimé ne doit pas changer, supprimer cette tâche et l'indiquer dans `research.md`.
- [X] T023 [US1] Recapturer `PosKitTests/Fixtures/cash_session.json` sur l'API en marche

### Web

- [X] T024 [US1] `web/app.js` + `styles.css` : écran Caisse (ouvrir avec fond, attendu en direct, sorties et entrées avec motif, comptage avec **pavé numérique à l'écran** et saisie par coupures, écart, note si requise), historique pour le responsable ; cibles de 56 pt
- [X] T025 [US1] Textes FR/EN/AR de l'écran dans `web/i18n/` ; noms et motifs échappés (XSS)
- [X] T026 [US1] `tests/RestaurantPos.Web.E2ETests/tests/cash.spec.ts` : fond 100 €, encaissement 50 € avec rendu, sortie, comptage, écart affiché et conservé

### iPad

- [X] T027 [P] [US1] `PosKit/Core/CashMath.swift` (attendu, écart, total par coupures, mêmes arrondis que T018) ; `PosKitTests/CashMathTests.swift` rejoue `cash_reference.json`
- [X] T028 [P] [US1] `PosKit/Models/CashModels.swift` et `PosKit/Stores/CashStores.swift` (`@Observable`)
- [X] T029 [US1] `PosKit/Local/LocalCashRepository.swift` et `LocalPosAPI+Cash.swift` : mêmes règles, mêmes refus et mêmes messages que le .NET ; le poste autonome est le terminal `T01` ; JET locaux pour les trois événements ; test `PosKitTests/Local/LocalCashTests.swift`
- [X] T030 [US1] `PosAPI` : méthodes de caisse dans `HTTPPosAPI`, `InMemoryPosAPI` (données de démo) et `LocalPosAPI` ; le Z local expose aussi `cashFloat`, `cashCounted`, `cashVariance`
- [X] T031 [US1] `ios/RestaurantPOS/Features/Cash/` : ouverture, mouvements, comptage avec pavé numérique, écart, historique ; textes FR/EN/AR ; identifiants d'accessibilité `cash.*`
- [X] T032 [P] [US1] `CashUITests` en `-UITestMode` et un cas `-UITestLocal`
- [X] T033 [US1] Étendre la journée simulée : `tests/RestaurantPos.Api.Tests/DaySimulation/day_scenario.json` et `DayRunner` (fond, encaissements espèces avec rendu, avoirs, sortie, comptage avec écart imposé). L'oracle donne l'attendu et l'écart par poste. Régénérer `fiscal_reference.json` (`WRITE_FISCAL_FIXTURE=1`) et la fixture de rapport (`WRITE_REPORT_PRINT_FIXTURE=1`), puis rejouer dans `LocalDaySimulationTests` (SC-003)

**Point de contrôle** : quickstart lot A. **Le lot A est livrable.**

---

## Phase 4 : Histoire 2 : Alertes sur comportements anormaux (P1)

**Objectif** : le responsable règle des seuils (écart de caisse, annulations, remises, offerts) ; un franchissement crée une alerte unique par seuil et par période, visible dans la cloche « à traiter », clôturable avec une note.

**Test indépendant** : un serveur dépasse le seuil d'annulations, une alerte nomme le serveur, le nombre et le montant et ouvre la liste des avoirs.

### Tests

- [X] T034 [P] [US2] `Infra.Tests/AlertServiceTests.cs` : alerte créée au franchissement, **une seule** par `DedupeKey` (relance de l'évaluation sans doublon), seuil désactivé = aucune alerte, nouvelle période après un Z = nouvelle alerte possible, détail (nombre, montant, identifiants des tickets), remise supérieure au pourcentage mentionne commande et auteur, écart de caisse au-dessus du seuil, clôture avec note obligatoire, alerte clôturée conservée dans l'historique
- [X] T035 [P] [US2] `Api.Tests/AlertEndpointsTests.cs` : `GET /api/alerts?state=` filtré, `POST …/resolve` avec note, `GET/PUT /api/alerts/rules`, accès réservé au responsable, `OnAlertsChanged` diffusé
- [X] T036 [P] [US2] `Api.Tests/AlertNonBlockingTests.cs` : une erreur dans l'évaluation n'annule ni l'avoir, ni la remise, ni l'encaissement
- [X] T037 [P] [US2] Étendre `Infra.Tests/CashReferenceFixtureTests.cs` : le même fichier `cash_reference.json` porte aussi les cas de seuils et de clés de dédoublonnage (régénérer avec `WRITE_CASH_FIXTURE=1`)

### Serveur

- [X] T038 [US2] `src/RestaurantPos.Infrastructure/Services/AlertService.cs` : évaluation par type (`cash_variance`, `voids_count`, `discount_percent`, `comps_total`), `PeriodKey = terminalId:numéroDuDernierZ`, `DedupeKey`, journal `ALERT_CREATED` et `ALERT_RESOLVED`, diffusion `OnAlertsChanged`
- [X] T039 [US2] Brancher l'évaluation après le `COMMIT` dans `CheckoutPaymentService.cs` (annulation), `OrderDiscountService.cs` (remise et offert) et `CashSessionService.cs` (écart au comptage, renseigne `alertCreated`) ; échec avalé et journalisé, jamais propagé
- [X] T040 [US2] `src/RestaurantPos.Api/Endpoints/AlertEndpoints.cs` : liste, résolution, règles ; DI dans `Program.cs`
- [X] T041 [US2] Recapturer `alerts.json` sur l'API en marche

### Web

- [X] T042 [US2] `web/app.js` : seuils dans Gestion (activation, valeur, types), cloche « à traiter » avec le type `alert` dans `refreshNeeds`, abonnement à `OnAlertsChanged`, détail avec lien vers les tickets concernés (via `/api/dashboard/receipts`), clôture avec note, historique ; repérage d'une anomalie en moins de 10 s (SC-004)
- [X] T043 [US2] Textes FR/EN/AR (noms d'alertes, seuils, notes) ; noms d'opérateurs échappés
- [X] T044 [US2] `tests/RestaurantPos.Web.E2ETests/tests/alerts.spec.ts` : quatre annulations pour un seuil de trois, une seule alerte, clôture avec note, absence de doublon après rafraîchissement

### iPad

- [X] T045 [P] [US2] `PosKit/Core/AlertRules.swift` (mêmes seuils, mêmes clés et même `PeriodKey` que T038) ; `PosKitTests/AlertRulesTests.swift` rejoue les cas de `cash_reference.json`
- [X] T046 [US2] `PosKit/Local/LocalAlertRepository.swift` et `LocalPosAPI+Alerts` : évaluation après le `COMMIT` local (annulation, remise, comptage), échec avalé ; test `PosKitTests/Local/LocalAlertTests.swift`
- [X] T047 [US2] `PosKit/Stores/NeedsStore.swift` : nouveau `Kind` `alert` alimenté par `GET /api/alerts` (ou local) ; rafraîchissement sur `alertsChanged`
- [X] T048 [US2] `PosAPI` : alertes et règles dans `HTTPPosAPI`, `InMemoryPosAPI` et `LocalPosAPI`
- [X] T049 [US2] `ios/RestaurantPOS/Features/Cash/` ou `Alerts/` : écran des seuils, liste, détail, clôture avec note ; textes FR/EN/AR
- [X] T050 [P] [US2] `AlertsUITests` en `-UITestMode`
- [X] T051 [US2] Étendre la journée simulée : annulations et remises imposées dans `day_scenario.json`; l'oracle liste les alertes attendues (SC-002 : toutes créées, aucune en double) ; régénérer les fixtures (`WRITE_FISCAL_FIXTURE=1`, `WRITE_REPORT_PRINT_FIXTURE=1`) et rejouer dans `LocalDaySimulationTests`

**Point de contrôle** : quickstart lot B. **Le lot B est livrable.**

---

## Phase 5 : Histoire 3 : Tendances et comparaisons du jour (P2)

**Objectif** : cartes « à surveiller » quand le chiffre d'affaires cumulé à l'heure, le ticket moyen ou le panier par couvert s'écartent de la référence ; aucune carte sous 3 jours de données.

**Test indépendant** : sept jours de ventes simulées, un jour à ticket moyen réduit de 20 %, la carte « ticket moyen en baisse » apparaît avec le pourcentage.

### Tests

- [X] T052 [P] [US3] `Infra.Tests/InsightsServiceTests.cs` : écart de ticket moyen de −20 % déclenche la carte, écart inférieur au seuil non, moins de 3 jours = `insufficientData:true` et aucune carte, journée sans vente = aucune carte, changement d'heure (tolérance d'une heure du rapport d'encaissements), part de la vente à emporter
- [X] T053 [P] [US3] `Api.Tests/InsightsEndpointTests.cs` : `GET /api/dashboard/insights?tzOffsetMinutes=` réservé au responsable, forme de réponse du contrat
- [X] T054 [P] [US3] Étendre `CashReferenceFixtureTests` (ou fixture voisine `insights_reference.json`) pour les calculs de deltas en pourcentage, arrondis identiques côté Swift

### Serveur

- [X] T055 [US3] `src/RestaurantPos.Infrastructure/Services/InsightsService.cs` : s'appuie sur `FinancialDashboardService` **sans le modifier**, compare au même jour de la semaine précédente à la même heure, seuil `TrendThresholdPercent`, métriques `cumulativeTtc`, `avgTicket`, `perCover`, `takeawayShare`
- [X] T056 [US3] `DashboardEndpoints.cs` : `GET /api/dashboard/insights` ; DI ; recapturer `insights.json`

### Web et iPad

- [X] T057 [US3] `web/app.js` + `i18n` : cartes « à surveiller » sur le tableau de bord, réglage du seuil de tendance dans Gestion ; `tests/RestaurantPos.Web.E2ETests/tests/insights.spec.ts`
- [X] T058 [P] [US3] `PosKit/Core/` miroir du calcul de delta ; `LocalPosAPI+Insights` bâti sur `LocalPosAPI.takings(...)`; `PosKitTests/Local/LocalInsightsTests.swift`
- [X] T059 [US3] `PosAPI` (`HTTPPosAPI`, `InMemoryPosAPI`, `LocalPosAPI`), store et vues des cartes sur le tableau de bord iPad ; textes FR/EN/AR ; `InsightsUITests`

**Point de contrôle** : quickstart lot C. **Le lot C est livrable.**

---

## Phase 6 : Histoire 4 : Rapport quotidien (P3)

**Objectif** : un résumé de fin de journée (chiffre d'affaires, couverts, ticket moyen, pourboires par serveur, écart de caisse, alertes ouvertes) aux **mêmes totaux** que le rapport Z, affichable, imprimable depuis le navigateur et partageable sur iPad. Le premier lot n'envoie rien par courriel.

**Test indépendant** : clôturer la journée simulée, les totaux du résumé sont ceux du Z.

### Tests

- [X] T060 [P] [US4] `Api.Tests/DailySummaryTests.cs` : totaux identiques à `GET /api/fiscal/z-report/last` dans 100 % des cas du jeu de test (SC-005), pourboires par serveur égaux à ceux de `GET /api/dashboard/takings`, résumé consultable sans canal d'envoi configuré, résumé consolidé de plusieurs postes, aucune écriture fiscale produite
- [X] T061 [P] [US4] `PosKitTests/Local/LocalDailySummaryTests.swift` : mêmes totaux que le Z local

### Implémentation

- [X] T062 [US4] `InsightsService` (ou service voisin) : `GET /api/dashboard/daily-summary?terminalId=&date=` (responsable) lisant le Z, `CashCounts`, les pourboires et les alertes ouvertes ; recapturer `daily_summary.json`
- [X] T063 [US4] Web : écran Résumé du jour (consolidé par poste), impression via `window.print()` avec feuille de style d'impression ; textes FR/EN/AR ; `daily-summary.spec.ts`
- [X] T064 [US4] iPad : `LocalPosAPI+Insights` pour le résumé, écran avec `ShareLink` (PDF ou texte) ; textes FR/EN/AR ; `DailySummaryUITests`
- [X] T065 [US4] Journée simulée : vérifier que le résumé de chaque poste égale son Z (oracle) côté .NET et iPad

**Point de contrôle** : quickstart lot D. **Le lot D est livrable.**

---

## Phase 7 : Finitions transverses

- [X] T066 [P] Documenter dans `docs/` (nouveau `docs/caisse-alertes.md`) : formule de l'espèce attendue, période de service entre deux Z, clés de dédoublonnage, seuils par défaut désactivés, ce qui reste hors périmètre (courriel, SMS, prévisions)
- [X] T067 [P] Mettre à jour `CLAUDE.md` : section « Caisse et alertes » (migration iPad, fixtures `cash_reference.json`, règle de suivi .NET/Swift, événement `OnAlertsChanged`, aucun impact sur les chaînes NF525)
- [X] T068 `dotnet format RestaurantPos.slnx` puis build (avertissements = erreurs) ; ne pas committer les fichiers hors sujet que le formateur modifie d'habitude
- [X] T069 Listes de contrôle du projet : XSS (noms d'opérateurs, motifs, notes), jetons sur les hubs, durée de vie du `DbContext` dans les services d'évaluation, cohérence des clés i18n FR/EN/AR (chiffres arabes), `LocalizationTests`
- [X] T070 `verify-chains` valide après la journée simulée (reçus, Z, périodes, archives, JET) ; test dédié si absent
- [X] T071 Suites complètes : `dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`, Playwright, contrat contre l'API réelle ; décomptes finaux dans `docs/plans/alerts-cash-progress.md`
- [ ] T072 Dérouler `quickstart.md` à la main (lots A à D) sur le simulateur et le web

---

## Dépendances et ordre d'exécution

- Phase 1, puis Phase 2 (bloque tout).
- **US1** (caisse) : seule dépendance, la phase 2 et T003.
- **US2** (alertes) : dépend de la phase 2 ; le branchement `cash_variance` (T039) dépend de US1 ; les autres types (annulations, remises, offerts) sont indépendants de US1.
- **US3** (tendances) : dépend de la phase 2 et des rapports d'encaissements existants ; indépendante de US1 et US2.
- **US4** (résumé) : dépend de US1 (écart de caisse) et lit les alertes ouvertes de US2 ; les totaux viennent du Z existant.
- Dans chaque histoire : tests, entités, services, endpoints, puis web et iPad (parallélisables entre eux).
- **Avec la spec 023** : les deux specs modifient `LocalMigrator.swift`, `NeedsStore.swift`, `app.js`, `day_scenario.json` et les fixtures de la journée simulée. Ne jamais lancer les deux implémentations en parallèle sur ces fichiers : finir un lot, committer, puis commencer l'autre.

## Exemples d'exécution parallèle

- Phase 2 : T005, T007, T008, T009, T010, T012 ensemble ; T011 après T006.
- US1 tests : T013 à T017 ensemble ; iPad T027 et T028 en parallèle du web T024 à T026.
- US2 tests : T034 à T037 ensemble.

## Stratégie de livraison

1. **MVP = lot A** (phases 1, 2, 3) : caisse et écart, démontrable seul.
2. **Lot B** : alertes et cloche (argument de démonstration fort).
3. **Lot C** : cartes de tendance.
4. **Lot D** : résumé quotidien.
5. Chaque lot se termine par les suites complètes vertes et `docs/plans/alerts-cash-progress.md` à jour.

## Points ouverts à trancher en cours de route

- **Cumul hebdomadaire** : le cas limite « serveur qui répartit ses annulations sur plusieurs services, visible dans le cumul de la semaine » n'est pas couvert par le modèle (une alerte par `PeriodKey` = un service). À trancher avant T038 : ajouter un type d'alerte à période hebdomadaire (`PeriodKey` = semaine ISO), ou retirer ce cas limite de la spec.
- **Seuils par défaut** : valeurs proposées à fixer à l'implémentation de T006 (la spec les veut désactivés tant que le responsable ne les active pas).
- **Courriel et SMS** : hors lot D (affichage, impression navigateur et partage iPad seulement).
