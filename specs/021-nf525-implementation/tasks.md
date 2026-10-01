# Tâches : NF525 — compléter la conformité

**Entrée** : `specs/021-nf525-implementation/` (spec, plan, research, data-model, contracts, quickstart)

**Tests** : inclus. Chaque récit a un « test indépendant » dans la spec et le projet travaille en TDD : écrire le test, le voir échouer, puis implémenter.

**Règles communes à chaque tâche backend** : `dotnet format RestaurantPos.slnx` avant build (ne garder que ses propres lignes), `[LoggerMessage]` pour la journalisation, centimes entiers, SQL idempotent dans `Program.cs` pour chaque colonne/table, chaînes en/fr/ar (`SharedResource*.resx`, `wwwroot/i18n/*.json`, `.xcstrings` PosKit et app). Fixtures iOS recapturées sur une base jetable.

## Format : `[ID] [P?] [Récit] Description`

- **[P]** : parallélisable (fichiers différents, pas de dépendance ouverte)
- **[USn]** : récit de la spec

---

## Phase 1 : Préparation

- [ ] T001 Créer la branche `021-nf525-implementation` depuis `main` et y déplacer `specs/021-nf525-implementation/`
- [ ] T002 [P] Obtenir de l'organisme certificateur la table officielle des codes d'événements JET (NF525 R19) et ses exigences de signature des archives ; consigner le résultat dans `specs/021-nf525-implementation/research.md` (R9) — bloque T012 et T061
- [ ] T003 [P] Relever les noms réels des tables SQLite (`sqlite3 restaurantpos.db .tables`) et corriger les noms dans `specs/021-nf525-implementation/data-model.md`

---

## Phase 2 : Fondations (bloquantes)

**⚠️** Aucun récit ne commence avant la fin de cette phase.

- [ ] T004 Extraire le calcul SHA-256 générique (champs séparés par `|`, dates `:O`) dans `src/RestaurantPos.Infrastructure/Services/FiscalHashing.cs` ; `ComputeReceiptHashSignature` l'appelle sans changer son résultat
- [ ] T005 Test de non-régression : les signatures de reçus et de Z calculées avant/après T004 sont identiques, dans `tests/RestaurantPos.Infrastructure.Tests/FiscalHashingTests.cs`
- [ ] T006 Ajouter l'index `IX_FiscalReceipts_VoidedReceiptId` (EF dans `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` + SQL idempotent dans `src/RestaurantPos.Api/Program.cs`)

**Checkpoint** : hachage commun en place, chaînes existantes inchangées.

---

## Phase 3 : US2 — JET chaîné et complet (P1) 🎯 socle du MVP

**Objectif** : chaque événement produit une entrée JET chaînée (R1, R2).

**Test indépendant** : connexion + changement de taux de TVA + Z → trois entrées chaînées ; altérer une entrée → la vérification (US1) la détecte.

### Tests

- [ ] T007 [P] [US2] Tests du service : séquence continue, `PreviousHash` = hash précédent, première entrée sur `GenesisHash`, entrées héritées (`ChainSequence = NULL`) ignorées, dans `tests/RestaurantPos.Infrastructure.Tests/FiscalJournalServiceTests.cs`
- [ ] T008 [P] [US2] Tests API : connexion réussie et échouée (`/api/auth`), `POST /api/devices/pair`, `POST /api/devices/{id}/revoke` écrivent chacun une entrée, dans `tests/RestaurantPos.Api.Tests/JournalEventsApiTests.cs`

### Implémentation

- [ ] T009 [US2] Ajouter `ChainSequence`, `PreviousHash`, `OperatorId` à `src/RestaurantPos.Domain/Entities/TransactionJournalEntry.cs` (sans renommer les champs existants) + SQL idempotent et index unique partiel dans `src/RestaurantPos.Api/Program.cs`
- [ ] T010 [US2] Créer `IFiscalJournal` (`AppendAsync(eventType, payload, terminalId, operatorId, ct)`) dans `src/RestaurantPos.Application/Common/Interfaces/IFiscalJournal.cs`
- [ ] T011 [US2] Implémenter `FiscalJournalService` (transaction `Serializable` si aucune n'est ouverte, sinon participation à la transaction courante ; formule R1) dans `src/RestaurantPos.Infrastructure/Services/FiscalJournalService.cs` ; l'enregistrer dans `src/RestaurantPos.Api/Program.cs`
- [ ] T012 [US2] Créer les constantes d'événements (table officielle de T002, sinon noms de `data-model.md`) dans `src/RestaurantPos.Domain/Entities/JournalEventTypes.cs`
- [ ] T013 [US2] Faire passer les écritures JET existantes par `IFiscalJournal` : `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs:194`, `src/RestaurantPos.Infrastructure/Services/HappyHourPricingService.cs:282` et `:338`
- [ ] T014 [US2] Journaliser connexion réussie/échouée dans `src/RestaurantPos.Api/Endpoints/AuthEndpoints.cs`, appairage et révocation dans `src/RestaurantPos.Api/Endpoints/DeviceEndpoints.cs`
- [ ] T015 [US2] Journaliser démarrage et arrêt du serveur (`IHostApplicationLifetime.ApplicationStarted/ApplicationStopping`) dans `src/RestaurantPos.Api/Program.cs`
- [ ] T016 [US2] Journaliser la Z dans la même transaction que la clôture, dans `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs` (`ExecuteDailyZClosureAsync`)
- [ ] T017 [US2] Journaliser les changements de taux de TVA d'un produit dans `src/RestaurantPos.Infrastructure/Services/BackOfficeCatalogService.cs` et l'export FEC dans `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs` (`/fec`)
- [ ] T018 [US2] Route de lecture paginée `GET /api/fiscal/journal` dans `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs`

**Checkpoint** : journal chaîné et alimenté ; rien d'autre n'a changé.

---

## Phase 4 : US3 — Annulation sans modifier le reçu (P1)

**Objectif** : `IsVoid` n'est plus écrit ni lu ; « annulé » = un avoir référence le reçu (R4).

**Test indépendant** : la ligne du reçu d'origine est identique avant et après annulation ; X, Z, rapports, tableau de bord et FEC gardent leurs totaux.

### Tests

- [ ] T019 [P] [US3] Test : après `VoidReceiptAsync`, toutes les colonnes du reçu d'origine sont inchangées ; une seconde annulation est refusée, dans `tests/RestaurantPos.Infrastructure.Tests/VoidWithoutMutationTests.cs`
- [ ] T020 [P] [US3] Tests de non-régression des totaux (X, rapport imprimé, tableau de bord, FEC, reste dû au paiement) sur un scénario vente + vente annulée, dans `tests/RestaurantPos.Api.Tests/VoidTotalsRegressionTests.cs` — à écrire et faire passer **avant** T021

### Implémentation

- [ ] T021 [US3] Ajouter un prédicat réutilisable « reçu annulé » (existence d'un reçu avec `VoidedReceiptId == r.Id`, ou jeu d'identifiants chargé une fois) dans `src/RestaurantPos.Infrastructure/Persistence/FiscalReceiptQueries.cs`
- [ ] T022 [US3] Remplacer écriture et lectures de `IsVoid` dans `src/RestaurantPos.Infrastructure/Services/CheckoutPaymentService.cs` (l.231 et reçus antérieurs) ; journaliser `RECEIPT_VOIDED` dans la même transaction
- [ ] T023 [P] [US3] Remplacer la lecture de `IsVoid` dans `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs:100`
- [ ] T024 [P] [US3] Remplacer la lecture de `IsVoid` dans `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs:347`
- [ ] T025 [P] [US3] Remplacer la lecture de `IsVoid` dans `src/RestaurantPos.Infrastructure/Services/FecExportService.cs:89`
- [ ] T026 [P] [US3] Remplacer la lecture de `IsVoid` dans `src/RestaurantPos.Infrastructure/Services/FinancialDashboardService.cs:44` (garder l'exclusion des avoirs)
- [ ] T027 [P] [US3] Remplacer la lecture de `IsVoid` dans `src/RestaurantPos.Infrastructure/Printing/ReportPrintDataService.cs:33`
- [ ] T028 [US3] Marquer `FiscalReceipt.IsVoid` comme colonne historique (plus d'affectation, commentaire) dans `src/RestaurantPos.Domain/Entities/FiscalReceipt.cs` ; vérifier par `grep` qu'aucune lecture ne subsiste

**Checkpoint** : T019 et T020 verts.

---

## Phase 5 : Garde anti-modification (INV-3, transverse)

**Dépend de** : Phase 4.

- [ ] T029 Test : modifier ou supprimer un `FiscalReceipt`, `PaymentTender`, `DailyFiscalClosure` ou une entrée JET chaînée lève `InvalidOperationException` ; une entrée JET héritée reste modifiable, dans `tests/RestaurantPos.Infrastructure.Tests/FiscalImmutabilityInterceptorTests.cs`
- [ ] T030 Recenser (`grep`) toute affectation restante sur ces entités hors création et la traiter
- [ ] T031 Implémenter `FiscalImmutabilityInterceptor` dans `src/RestaurantPos.Infrastructure/Persistence/FiscalImmutabilityInterceptor.cs` et l'enregistrer (SQLite et InMemory) dans `src/RestaurantPos.Api/Program.cs`
- [ ] T032 Lancer toute la suite .NET : aucune régression

---

## Phase 6 : US1 — Vérifier l'intégrité (P1) 🎯 MVP

**Objectif** : une vérification de toutes les chaînes, accessible au gérant (contrat `POST /api/fiscal/verify`).

**Test indépendant** : altérer un montant en base → la vérification désigne le reçu et écrit `CHAIN_BREAK_DETECTED`.

### Tests

- [ ] T033 [P] [US1] Tests service : chaînes intactes ; rupture par hash précédent ; rupture par signature ; trou de séquence ; chaîne vide ; entrées JET héritées comptées à part, dans `tests/RestaurantPos.Infrastructure.Tests/ChainVerificationTests.cs`
- [ ] T034 [P] [US1] Test API : `POST /api/fiscal/verify` (droits gérant, format du contrat, entrée JET en cas de rupture), dans `tests/RestaurantPos.Api.Tests/ChainVerificationApiTests.cs`
- [ ] T035 [P] [US1] Test de performance : 100 000 reçus vérifiés en moins de 30 s (SC-002), dans `tests/RestaurantPos.Infrastructure.Tests/ChainVerificationTests.cs` (`Trait("Category","Slow")`)

### Implémentation

- [ ] T036 [US1] Ajouter `VerifyAllChainsAsync` et les DTO du contrat à `src/RestaurantPos.Application/Common/Interfaces/INF525FiscalAuditService.cs`
- [ ] T037 [US1] Implémenter dans `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs` en réutilisant `ValidateAuditChainIntegrityAsync` (reçus par terminal, `AsNoTracking`), plus Z par terminal et JET
- [ ] T038 [US1] Route `POST /api/fiscal/verify` dans `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs`
- [ ] T039 [P] [US1] Web : bouton « Vérifier l'intégrité » et affichage par chaîne dans l'écran fiscal (`src/RestaurantPos.Api/wwwroot/index.html`, `app.js`, `i18n/*.json`) + spec `tests/RestaurantPos.Web.E2ETests/tests/fiscal-verify.spec.ts`
- [ ] T040 [P] [US1] iPad : modèle, `PosAPI.verifyChains`, `HTTPPosAPI`, `InMemoryPosAPI`, `FiscalStore.verify()` dans `ios/Packages/PosKit/Sources/PosKit/` (`Models/OperationsModels.swift`, `Networking/`, `Testing/InMemoryPosAPI.swift`, `Stores/OperationsStores.swift`) + écran `ios/RestaurantPOS/Features/Fiscal/FiscalScreen.swift` + fixture `verify.json` + tests `StoreTests` / `ContractDecodingTests`

**Checkpoint** : MVP (JET chaîné, annulation propre, garde, vérification).

---

## Phase 7 : US7 — Identité et exercice (P3, requis par US4)

**Objectif** : identité, certificat et exercice saisis dans la gestion ; en-tête des tickets paramétré (R7).

**Test indépendant** : changer la raison sociale → le prochain ticket l'imprime ; entrée `FISCAL_SETTINGS_CHANGED`.

- [ ] T041 [P] [US7] Tests : `PUT /api/settings` partiel conserve les autres champs ; SIRET/TVA/date invalides → 400 ; changement d'exercice après une clôture annuelle → 409 ; entrée JET, dans `tests/RestaurantPos.Api.Tests/SettingsEndpointsTests.cs`
- [ ] T042 [P] [US7] Test : l'en-tête du ticket reprend les réglages ; sans certificat, seule la version ; avec certificat, il est imprimé, dans `tests/RestaurantPos.Infrastructure.Tests/TicketDocumentBuilderTests.cs`
- [ ] T043 [US7] Ajouter les champs à `src/RestaurantPos.Domain/Entities/RestaurantSettings.cs` + SQL idempotent avec valeurs initiales = en-tête actuel, dans `src/RestaurantPos.Api/Program.cs`
- [ ] T044 [US7] Étendre `IRestaurantSettingsService`, sa requête de mise à jour et la validation, et journaliser, dans `src/RestaurantPos.Infrastructure/Services/` et `src/RestaurantPos.Api/Endpoints/SettingsEndpoints.cs`
- [ ] T045 [US7] Remplacer l'en-tête en dur (`TicketDocumentBuilder.cs:17`) par les réglages + version (`AssemblyInformationalVersion`) + certificat, dans `src/RestaurantPos.Infrastructure/Printing/TicketDocumentBuilder.cs` et `PrintDispatcher.cs`
- [ ] T046 [P] [US7] Web : formulaire d'identité et d'exercice dans la gestion (`index.html`, `app.js`, `i18n/*.json`) + spec E2E
- [ ] T047 [P] [US7] iPad : modèle des réglages, store, écran `ios/RestaurantPOS/Features/Admin/AdminScreen.swift`, fixture `settings.json`, tests

---

## Phase 8 : US4 — Clôtures mensuelles et annuelles (P2)

**Dépend de** : US2 (JET), US7 (exercice).

**Test indépendant** : trois Z d'un mois → clôture mensuelle = somme au centime ; seconde demande → 409.

- [ ] T048 [P] [US4] Tests service : somme des Z au centime ; grand total repris de la dernière Z ; `period_not_ended`, `period_already_closed`, `missing_daily_closures`, `missing_monthly_closures` ; exercice décalé (début au 1er avril) ; chaîne par terminal et par type, dans `tests/RestaurantPos.Infrastructure.Tests/PeriodClosureTests.cs`
- [ ] T049 [P] [US4] Tests API du contrat `POST/GET /api/fiscal/period-closures`, dans `tests/RestaurantPos.Api.Tests/PeriodClosureApiTests.cs`
- [ ] T050 [US4] Créer `FiscalPeriodClosure` dans `src/RestaurantPos.Domain/Entities/FiscalReceipt.cs` + `DbSet` et index unique dans `AppDbContext.cs` + `CREATE TABLE IF NOT EXISTS` dans `Program.cs` ; l'ajouter à l'intercepteur (T031)
- [ ] T051 [US4] Implémenter `ExecutePeriodClosureAsync` (bornes calculées par le serveur en heure locale, formule R1, JET dans la même transaction) dans `src/RestaurantPos.Infrastructure/Services/NF525FiscalAuditService.cs` ; ajouter la chaîne `period_closures` à `VerifyAllChainsAsync`
- [ ] T052 [US4] Routes dans `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs` ; impression de la clôture via `PrintDispatcher` (`printQueued`)
- [ ] T053 [P] [US4] Web : section « Clôtures de période » de l'écran fiscal + spec E2E (réponses 409 simulées par `page.route`)
- [ ] T054 [P] [US4] iPad : modèles, API, `FiscalStore`, écran, fixture capturée sur base jetable, tests

---

## Phase 9 : US6 — Duplicatas (P2)

**Test indépendant** : réimprimer deux fois un ticket → « DUPLICATA n°1 » puis « n°2 », deux entrées JET.

- [ ] T055 [P] [US6] Tests : numérotation par document, `latest-closure/print` devient un duplicata, `retry` d'un job `Done` crée un duplicata mais pas celui d'un job en échec, entrée JET, dans `tests/RestaurantPos.Api.Tests/DuplicatePrintTests.cs`
- [ ] T056 [US6] Ajouter `DuplicateOfDocumentId`, `DuplicateNumber` à `src/RestaurantPos.Domain/Entities/PrintJob.cs` + SQL idempotent
- [ ] T057 [US6] Numérotation en transaction et mention « DUPLICATA n°N » dans `src/RestaurantPos.Infrastructure/Printing/PrintDispatcher.cs` et `TicketDocumentBuilder.cs` (rendus image et texte)
- [ ] T058 [US6] Route `POST /api/checkout/receipts/{receiptId}/reprint` dans `src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs` ; adapter `FiscalEndpoints.cs` (`latest-closure/print`) et `PrintJobEndpoints.cs` (`retry`)
- [ ] T059 [P] [US6] Web : bouton « Réimprimer » sur un ticket, `duplicateNumber` affiché + spec E2E
- [ ] T060 [P] [US6] iPad : API, store, bouton, tests

---

## Phase 10 : US5 — Archives et conservation (P2)

**Dépend de** : US4. **Bloqué par** : T002 (exigences de signature).

**Test indépendant** : archiver un mois, modifier un octet du ZIP → vérification `hash_mismatch`.

- [ ] T061 [P] [US5] Tests : contenu du ZIP (5 fichiers, manifeste), empreinte, chaîne des archives, `archive_exists`, vérification `hash_mismatch` et `unknown_archive`, dans `tests/RestaurantPos.Infrastructure.Tests/FiscalArchiveServiceTests.cs`
- [ ] T062 [P] [US5] Tests API du contrat (création, téléchargement avec JET, vérification multipart), dans `tests/RestaurantPos.Api.Tests/FiscalArchiveApiTests.cs`
- [ ] T063 [US5] Créer `FiscalArchive` (entité, `DbSet`, SQL idempotent, intercepteur)
- [ ] T064 [US5] Implémenter `FiscalArchiveService` (ZIP en flux, `Archives:Path`, formule R1, JET) dans `src/RestaurantPos.Infrastructure/Services/FiscalArchiveService.cs` ; ajouter la chaîne `archives` à `VerifyAllChainsAsync`
- [ ] T065 [US5] Routes `POST /api/fiscal/archives`, `GET /api/fiscal/archives/{id}/file`, `POST /api/fiscal/archives/verify` dans `FiscalEndpoints.cs` ; `Archives:Path` dans `src/RestaurantPos.Api/appsettings.json`
- [ ] T066 [P] [US5] Web : archiver une clôture, télécharger, vérifier un fichier + spec E2E
- [ ] T067 [P] [US5] iPad : archiver et vérifier (pas de téléchargement sur iPad), store, écran, tests

---

## Phase 11 : Finitions

- [ ] T068 [P] Rédiger `docs/fiscal.md` : formules des chaînes (reçus, Z, JET, périodes, archives), procédures de vérification, d'archivage et de conservation
- [ ] T069 [P] Mettre à jour `CLAUDE.md` (section NF525 : nouvelles chaînes, `IsVoid` historique, intercepteur)
- [ ] T070 Dérouler `specs/021-nf525-implementation/quickstart.md` sur une base jetable
- [ ] T071 Passer l'export FEC d'un mois dans l'outil DGFiP « Test Compta Demat » (SC-006)
- [ ] T072 Suites complètes : `dotnet test RestaurantPos.slnx`, Playwright (`--project='iPad Pro 11'`), `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`

---

## Dépendances et ordre

### Phases

- Phase 1 → Phase 2 → Phase 3 (US2) → Phase 4 (US3) → Phase 5 (garde) → Phase 6 (US1) = **MVP**
- Phase 7 (US7) dès la Phase 3 terminée (a besoin du JET) ; requise par la Phase 8
- Phase 8 (US4) → Phase 10 (US5)
- Phase 9 (US6) dès la Phase 3 terminée
- Phase 11 en dernier

### Récits

| Récit | Dépend de |
|---|---|
| US2 | Fondations |
| US3 | US2 (journalise l'annulation) |
| US1 | US2, US3 et la garde (Phase 5) |
| US7 | US2 |
| US4 | US2, US7 |
| US6 | US2 |
| US5 | US4, T002 |

### Dans chaque récit

Tests d'abord (rouges) → entités et schéma → service → routes → web et iPad en parallèle.

### Parallélisme

- T002 et T003 en parallèle.
- Dans US3 : T023 à T027 (fichiers distincts), après T021.
- Dans chaque récit : les tâches web et iPad marquées [P] en parallèle, une fois les routes livrées.
- Après le MVP : US7 et US6 en parallèle.

## Exemple parallèle : US3

```text
Après T021 :
  T023 CheckoutEndpoints.cs:100
  T024 NF525FiscalAuditService.cs:347
  T025 FecExportService.cs:89
  T026 FinancialDashboardService.cs:44
  T027 ReportPrintDataService.cs:33
```

## Stratégie

1. **MVP** (Phases 1 à 6) : journal chaîné, annulation sans modification, garde anti-modification, vérification accessible. Livrable seul : il couvre l'inaltérabilité et sa preuve.
2. **Incrément 2** : US7 puis US4 (clôtures de période).
3. **Incrément 3** : US6 (duplicatas).
4. **Incrément 4** : US5 (archives), dès que T002 a fixé les exigences de signature.
