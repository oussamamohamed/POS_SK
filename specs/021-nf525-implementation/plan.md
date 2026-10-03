# Plan d'implémentation : NF525 — compléter la conformité

**Branche** : `021-nf525-implementation` | **Date** : 2026-09-30 | **Spec** : [spec.md](spec.md)

## Résumé

Compléter le socle NF525 existant sans le refaire : vérification d'intégrité accessible (reçus, Z, JET), JET chaîné et alimenté, annulation sans modification du reçu d'origine, clôtures mensuelles et annuelles chaînées, archives scellées vérifiables, duplicatas numérotés, identité de l'établissement paramétrable. Les chaînes existantes (reçus, Z) ne changent pas.

## Contexte technique (réel)

- **Backend** : .NET 9, ASP.NET Core Minimal API (`src/RestaurantPos.Api/Endpoints/*Endpoints.cs`), services « simples » (`Infrastructure/Services`, interfaces dans `Application/Common/Interfaces`), DI dans `Program.cs`. **Pas de MediatR, pas de PostgreSQL** (les paquets Npgsql des `.csproj` ne sont pas utilisés).
- **Stockage** : SQLite via EF Core, `EnsureCreated()` + bloc de SQL idempotent dans `Program.cs` (hors environnement `Testing`, qui utilise InMemory). **Pas de migrations.**
- **Clients** : web vanilla (`wwwroot/app.js`, `index.html`, `i18n/{en,fr,ar}.json`) et iPad SwiftUI (`ios/Packages/PosKit` + `ios/RestaurantPOS/Features/{Fiscal,Admin}`), à parité.
- **Tests** : xUnit + FluentAssertions (`tests/RestaurantPos.{Domain,Infrastructure,Api}.Tests`), Playwright (`tests/RestaurantPos.Web.E2ETests`), Swift Testing + XCUITest.
- **Contraintes de build** : `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` → `dotnet format` avant build ; `[LoggerMessage]` pour la journalisation ; argent en centimes entiers.
- **Performance** : vérification complète < 30 s pour ~100 000 reçus (SC-002) ; lecture chaîne par chaîne, triée par séquence, sans suivi EF (`AsNoTracking`).

## Contrôle de conformité

La constitution (`.specify/memory/constitution.md`, v2.0.0) a été alignée sur le code (plus de MAUI, MediatR ni PostgreSQL). Le contrôle se fait contre elle, `CLAUDE.md` et les invariants de la spec :

- [x] INV-1 : formule des reçus et des Z inchangée ; nouvelles chaînes avec formules propres (research R1).
- [x] INV-2 : aucune signature côté client ; le serveur calcule tout.
- [x] INV-3 : ajout seul ; `IsVoid` n'est plus écrit (R4) ; garde anti-modification ajoutée **après** R4 (R5).
- [x] INV-4 : centimes ; grand total perpétuel jamais remis à zéro.
- [x] Schéma : chaque colonne/table nouvelle a son SQL idempotent dans `Program.cs`.
- [x] Événements SignalR existants non renommés.

## Structure

### Documentation

```text
specs/021-nf525-implementation/
├── spec.md
├── plan.md            # ce fichier
├── research.md        # décisions techniques
├── data-model.md      # entités et SQL de schéma
├── quickstart.md      # validation manuelle
├── contracts/nf525-api.md
└── tasks.md           # /speckit-tasks
```

### Code touché

```text
src/RestaurantPos.Domain/Entities/
  TransactionJournalEntry.cs      # + ChainSequence, PreviousHash ; EntryHash devient un vrai hash
  FiscalReceipt.cs                # IsVoid conservé en base, plus écrit (R4) ; + FiscalPeriodClosure, FiscalArchive
  PrintJob.cs                     # + DuplicateOfDocumentId, DuplicateNumber
  RestaurantSettings.cs           # + identité, certificat, début d'exercice
src/RestaurantPos.Application/Common/Interfaces/
  INF525FiscalAuditService.cs     # + VerifyAllChainsAsync, clôtures de période, archives
  IFiscalJournal.cs               # nouveau : AppendAsync(eventType, payload, …)
src/RestaurantPos.Infrastructure/
  Services/NF525FiscalAuditService.cs, FiscalJournalService.cs (nouveau), FiscalArchiveService.cs (nouveau)
  Services/CheckoutPaymentService.cs, FecExportService.cs, FinancialDashboardService.cs   # R4
  Printing/ReportPrintDataService.cs, TicketDocumentBuilder.cs, PrintDispatcher.cs        # R4, duplicatas, identité
  Persistence/AppDbContext.cs, FiscalImmutabilityInterceptor.cs (nouveau)
src/RestaurantPos.Api/
  Program.cs (schéma, DI, JET démarrage/arrêt), Endpoints/{Fiscal,Checkout,Auth,Device,Settings,PrintJob}Endpoints.cs
  wwwroot/{app.js,index.html,i18n/*.json}
ios/Packages/PosKit/  Models, Networking (PosAPI, HTTPPosAPI, InMemoryPosAPI), Stores, Tests/Fixtures
ios/RestaurantPOS/Features/{Fiscal,Admin}/
docs/fiscal.md (nouveau : formules, procédure de vérification et d'archivage)
```

## Ordre de réalisation

Les dépendances imposent cet ordre (chaque étape = backend + tests, puis web + iPad) :

1. **JET chaîné** (US2, socle) : `IFiscalJournal`, colonnes, formule, écriture dans la même transaction que l'événement métier quand il y en a une.
2. **Annulation sans modification** (US3) : suppression des écritures de `IsVoid`, lecture par avoir lié (R4) ; totaux identiques avant/après.
3. **Garde anti-modification** (INV-3) : intercepteur sur les entités fiscales (R5) — possible seulement après l'étape 2.
4. **Vérification** (US1) : reçus (par terminal), Z (par terminal), clôtures de période, JET ; route + écrans.
5. **Identité et exercice** (US7 + réglage d'US4) : extension de `RestaurantSettings`, en-tête des tickets.
6. **Clôtures mensuelles et annuelles** (US4).
7. **Duplicatas** (US6).
8. **Archives** (US5), puis purge refusée < 6 ans.
9. **Journalisation des événements restants** (US2.2 : connexion, appairage, paramètres, export FEC…) et doc `docs/fiscal.md`.

## Points transverses

- **Schéma** : SQL idempotent dans `Program.cs` pour chaque ajout (liste dans `data-model.md`).
- **Contrat inter-clients** : DTO .NET ↔ `app.js` ↔ modèles PosKit ; fixtures recapturées sur une base jetable (une Z ou une clôture ne s'efface pas).
- **Chaînes** : en/fr/ar dans `.resx`, `i18n/*.json`, `.xcstrings` PosKit et app.
- **Droits** : toutes les routes nouvelles sous `RequireManagerOrAdmin`, sauf la réimpression de ticket (opérateur authentifié).
- **Environnement `Testing`** : l'intercepteur et le JET doivent fonctionner avec InMemory (pas de SQL brut dans le chemin nominal).

## Suivi de complexité

| Écart | Pourquoi | Alternative plus simple rejetée |
|---|---|---|
| Nouvelle entité `FiscalPeriodClosure` au lieu d'étendre `DailyFiscalClosure` | la Z a sa chaîne et ses routes utilisées par les deux clients | un seul type « Daily/Monthly/Yearly » casserait la chaîne Z et `/z-closure` |
| Intercepteur EF | garantit INV-3 pour tout code futur | revue manuelle seule : pas de garde-fou |
