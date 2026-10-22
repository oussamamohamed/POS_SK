# Lot 4b — File d'impression et moteur — Plan d'implémentation

> **Pour les agents d'exécution :** SOUS-SKILL REQUIS : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans. Les étapes utilisent des cases `- [ ]`.

**Objectif :** le mode autonome de l'iPad met en file puis imprime (LAN, TCP 9100) tickets, bons de retrait, bons cuisine, duplicata et rapports X / Z / période, comme le .NET, sans jamais faire échouer une vente. `printQueued`, `testPrinter`, `printerStatuses`, `printJobs`, `retryPrintJob` et `cancelPrintJob` deviennent réels.

**Architecture :** `LocalPosAPI` est un `actor` qui possède l'unique `SQLiteDatabase` (non thread-safe) : toute la logique de file vit donc dans des extensions de `LocalPosAPI` plus un petit dépôt SQL `LocalPrintQueueRepository`, et seul l'envoi réseau (`PrinterTransport.send`) est attendu hors transaction. Les documents sont produits par `TicketBuilder` (lot 4a), sérialisés en JSON dans `PrintJobs.DocumentJson`, puis rendus en octets (`TicketPayload.build`) au moment de l'envoi. Un transport est un protocole d'octets, commun à LAN / Star / Epson (le lot 4c ajoute Star et Epson derrière le même protocole).

**Tech Stack :** Swift 6, Swift Testing, SQLite3 (déjà en place), Network.framework (`NWConnection`, `NWListener` pour les tests), .NET 9 + xUnit pour une fixture de parité des données de rapport.

**Spec :** `docs/superpowers/specs/2026-10-22-ipad-standalone-4-impression-design.md` (lot 4b). Référence .NET : `src/RestaurantPos.Infrastructure/Printing/` (`PrintDispatcher.cs`, `PrintQueue.cs`, `PrintQueueProcessor.cs`, `PrinterStatusTracker.cs`, `EscPosSender.cs`, `DuplicateNumbering.cs`, `ReportPrintDataService.cs`), `src/RestaurantPos.Domain/Entities/PrintJob.cs`, `src/RestaurantPos.Api/Endpoints/PrintJobEndpoints.cs` (et l'endpoint `POST /api/printers/{id}/test`, à localiser par `grep -rn "/test" src/RestaurantPos.Api/Endpoints`), `docs/impression.md`.

## Global Constraints

- Branche de travail : `feat/ipad-standalone-4b-file-moteur`, créée depuis `feat/ipad-standalone-4a-rendu`. Pousser à la fin de chaque tâche.
- `PosKit` reste sans dépendance externe (Network.framework, CoreText, SQLite3 sont des frameworks système).
- **L'impression ne fait jamais échouer une vente.** Toute mise en file se fait APRÈS la validation de la transaction métier (jamais dedans, sauf les duplicata, voir tâche 5), n'est jamais `throws` vers l'appelant, et en cas d'erreur journalise puis renvoie `printQueued == false`.
- Mêmes tables et colonnes que `AppDbContext` : `PrintJobs` = entité `PrintJob` du .NET (`Id, PrinterId, Kind, DocumentJson, OpenCashDrawer, Status, Attempts, NextAttemptAtUtc, DeadlineAtUtc, LastError, CreatedAtUtc, SentAtUtc, DuplicateOfDocumentId, DuplicateNumber`). Énumérations stockées en entiers : `Kind` PickupVoucher 0, Receipt 1, KitchenTicket 2, Report 3 ; `Status` Pending 0, Sent 1, Failed 2, Cancelled 3. Dates via `SQLDate`/`FiscalHashing.storedText` selon l'usage actuel de `Local/` (suivre `LocalPrinterRepository`).
- Nouvelle migration V10 (`LocalSchemaV10.swift`, jamais de modification d'un script publié). Colonnes propres à l'iPad dans `PrinterConfigurations` : `TransportKind TEXT NOT NULL DEFAULT 'lan'` (`lan`, `star`, `epson`) et `DeviceIdentifier TEXT NULL`. À consigner dans `CLAUDE.md`.
- Règles de file (port de `PrintQueueProcessor`) : FIFO par imprimante (ordre de création, puis `Id`) ; un job dont la relance n'est pas échue bloque les suivants de la même imprimante ; le premier échec arrête la file de cette imprimante (les autres continuent) ; relances +5 s (tentatives 0 et 1), +15 s (2), +30 s (3), +60 s (4 et plus) ; échéance = création + 30 min (`PrintJob.Lifetime`), ensuite `Failed` ; imprimante désactivée ou supprimée → jobs `Pending` en `Cancelled` (`LastError` « Printer disabled » / « Printer deleted ») ; reprise des `Pending` au lancement ; purge des `Sent` et `Cancelled` de plus de 7 jours (les `Failed` restent).
- Délais du transport LAN : connexion 3 s, écriture 10 s.
- Horloge injectable partout (`LocalPosAPI.clock`) : aucun test ne dort réellement.
- Aucune chaîne d'interface dans ce lot (messages d'API en français comme le reste de `Local/`, textes de ticket via `TicketTexts`).
- Tests : à chaque tâche, `dotnet test RestaurantPos.slnx` et `cd ios/Packages/PosKit && swift test` en entier. Mettre à jour `docs/plans/ipad-standalone-4b-progress.md` après chaque tâche (statut, compteurs .NET / PosKit, constats ouverts).
- Style de `Local/` : commentaires français, pas de commentaire redondant, fichiers ciblés.

## Review Focus

1. Imprimante hors ligne au moment de la vente : la vente est validée, `printQueued == true`, le job reste `Pending`, il part dans l'ordre au rétablissement. Tâches 3 et 6.
2. App tuée avec des jobs `Pending` : ils sont repris au lancement suivant (y compris ceux dont la relance est échue). Tâche 3.
3. Échéance dépassée hors ligne : `Failed` ; la relance manuelle remet `Pending`, tentatives à 0, échéance +30 min. Tâche 3.
4. Deux cycles qui se chevauchent (réentrance de l'acteur pendant l'attente du transport) ne doivent jamais envoyer deux fois le même job. Tâche 3.
5. Échec SQL à la mise en file (disque plein, table verrouillée) : la vente reste validée, `printQueued == false`, aucune exception. Tâche 5.
6. Tiroir-caisse : ouvert seulement si règlement en espèces ET imprimante avec « ouvrir le tiroir » ; jamais pour un duplicata ni un bon cuisine. Tâche 5.

## File Structure

- Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV10.swift` ; modifier `LocalMigrator.swift` (V10), `Models/OperationsModels.swift` (`Printer.transport`, `Printer.deviceIdentifier`), `Local/LocalPrinterRepository.swift`.
- Créer `…/Local/LocalPrintQueueRepository.swift` (SQL de `PrintJobs`) et `…/Printing/PrintJob.swift` (types `PrintJobKind`, `PrintJobStatus`, `PrintJobRecord`).
- Créer `…/Printing/PrinterTransport.swift` (protocole, `PrinterTransportError`, `RoutingPrinterTransport`), `…/Printing/LANPrinterTransport.swift`, `…/Testing/FakePrinterTransport.swift`.
- Créer `…/Printing/PrinterStatusTracker.swift`.
- Créer `…/Local/LocalPosAPI+PrintWorker.swift` (cycle, reprise, purge, signal) ; remplacer les corps de `LocalPosAPI+Printers.swift`.
- Créer `…/Local/LocalReportPrintData.swift` (données de rapport) et `…/Local/LocalPosAPI+PrintDispatch.swift` (mise en file par événement métier).
- Modifier `LocalPosAPI.swift` (paramètres `transport`, état du worker), `LocalPosAPI+CounterCheckout.swift`, `+Payments.swift`, `+Kitchen.swift`, `+Fiscal.swift`, `+PeriodClosures.swift`, `LocalFiscalRepository.swift` (`recordDuplicate…` avec `printQueued`).
- Tests : `Tests/PosKitTests/Printing/` et `Tests/PosKitTests/Local/` (voir chaque tâche) ; .NET : `tests/RestaurantPos.Api.Tests/DaySimulation/…` ou `tests/RestaurantPos.Infrastructure.Tests/ReportPrintDataReferenceFixtureTests.cs`.
- Fixture : `Tests/PosKitTests/Fixtures/report_print_data_reference.json`.

---

### Task 1: Schéma V10, modèle `Printer` étendu et dépôt `PrintJobs`

**Files:**
- Create: `…/Local/LocalSchemaV10.swift`, `…/Local/LocalPrintQueueRepository.swift`, `…/Printing/PrintJob.swift`
- Modify: `…/Local/LocalMigrator.swift`, `…/Models/OperationsModels.swift` (`Printer`), `…/Local/LocalPrinterRepository.swift`
- Test: `Tests/PosKitTests/Local/LocalPrintQueueSchemaTests.swift`, `LocalPrintQueueRepositoryTests.swift`, ajustements de `LocalBackupTests` (restauration d'une base V9)

**Interfaces:**
- Produces :

```swift
enum PrintJobKind: Int, Sendable { case pickupVoucher = 0, receipt, kitchenTicket, report }
enum PrintJobStatus: Int, Sendable { case pending = 0, sent, failed, cancelled }

struct PrintJobRecord: Equatable, Sendable {
    let id: UUID; let printerId: UUID; let kind: PrintJobKind; let documentJson: String; let openCashDrawer: Bool
    var status: PrintJobStatus; var attempts: Int; var nextAttemptAtUtc: Date; var deadlineAtUtc: Date
    var lastError: String?; let createdAtUtc: Date; var sentAtUtc: Date?
    let duplicateOfDocumentId: UUID?; let duplicateNumber: Int?
}

struct LocalPrintQueueRepository {
    let db: SQLiteDatabase
    static let lifetime: TimeInterval = 30 * 60
    @discardableResult func enqueue(printerId: UUID, kind: PrintJobKind, documentJson: String, openCashDrawer: Bool,
                                    duplicateOfDocumentId: UUID?, duplicateNumber: Int?, now: Date) throws -> PrintJobRecord
    func job(id: UUID) throws -> PrintJobRecord?
    func pendingJobs() throws -> [PrintJobRecord]                       // tous les Pending, tri création puis Id
    func jobs(printerId: UUID, statuses: [PrintJobStatus], limit: Int) throws -> [PrintJobRecord]  // décroissant par création
    func save(_ job: PrintJobRecord) throws                             // met à jour statut, tentatives, échéances, erreur, envoi
    func counts() throws -> [UUID: (pending: Int, failed: Int)]
    func purge(olderThan: Date) throws -> Int                           // Sent et Cancelled créés avant la limite
}
```

- `Printer` (modèle public) gagne `transport: PrinterTransportKind` (`lan` par défaut) et `deviceIdentifier: String?` ; `Codable` décode les anciennes réponses sans ces clés (`decodeIfPresent`), les encode quand elles ne sont pas par défaut. `PrinterTransportKind: String, Codable, Sendable { case lan, star, epson }` est défini dans `Models/OperationsModels.swift`.

- [ ] **Step 1 : tests (RED).** Migration : une base neuve migre jusqu'à V10 ; `PrintJobs` existe avec les 14 colonnes ci-dessus ; `PrinterConfigurations` a les deux colonnes et les imprimantes existantes gardent `TransportKind = 'lan'`. Restauration : une base V9 existante (utiliser le patron de `LocalBackupTests` pour une sauvegarde ancienne) se migre en V10 sans perte. Dépôt : `enqueue` fixe `NextAttemptAtUtc = now`, `DeadlineAtUtc = now + 30 min`, `Status = pending`, `Attempts = 0` ; `pendingJobs()` renvoie l'ordre de création puis `Id` (deux jobs de même horodatage) ; `jobs(printerId:statuses:limit:)` filtre et trie décroissant ; `purge` n'efface que `Sent`/`Cancelled` plus vieux que la limite et jamais un `Failed` ; aller-retour des dates à la milliseconde (même format que les autres tables : `FiscalHashing.storedText` si les autres dépôts l'utilisent, sinon `SQLDate`). `LocalPrinterRepository` lit/écrit les deux colonnes ; le CRUD actuel de `savePrinter` conserve `transport` et `deviceIdentifier` (aucune régression des tests de `LocalPosAPI+Printers`).
- [ ] **Step 2 : implémenter** schéma, modèle, dépôts. `ALTER TABLE PrinterConfigurations ADD COLUMN …` pour les deux colonnes (SQLite le permet avec défaut constant).
- [ ] **Step 3 : suites complètes + commit** (`feat(ios): schéma V10 (PrintJobs, transport des imprimantes) et dépôt de la file d'impression (4b)`).

---

### Task 2: Protocole `PrinterTransport`, transport LAN et faux transport

**Files:**
- Create: `…/Printing/PrinterTransport.swift`, `…/Printing/LANPrinterTransport.swift`, `…/Testing/FakePrinterTransport.swift`
- Test: `Tests/PosKitTests/Printing/LANPrinterTransportTests.swift`, `RoutingPrinterTransportTests.swift`

**Interfaces:**
- Produces :

```swift
public protocol PrinterTransport: Sendable {
    /// Envoie les octets ESC/POS déjà rendus ; lève en cas d'échec (réseau, délai, périphérique absent).
    func send(_ payload: [UInt8], to printer: Printer) async throws
}

public enum PrinterTransportError: Error, Equatable, Sendable {
    case unsupported(PrinterTransportKind)   // aucun transport enregistré pour ce type
    case connectTimeout, writeTimeout
    case connection(String)
}

public struct LANPrinterTransport: PrinterTransport {
    public init(connectTimeout: TimeInterval = 3, writeTimeout: TimeInterval = 10)
}

/// Aiguille sur le transport du type de l'imprimante ; `unsupported` sinon (4c enregistre Star et Epson).
public struct RoutingPrinterTransport: PrinterTransport {
    public init(lan: any PrinterTransport = LANPrinterTransport(), star: (any PrinterTransport)? = nil, epson: (any PrinterTransport)? = nil)
}

public final class FakePrinterTransport: PrinterTransport, @unchecked Sendable   // Testing/ : enregistre les envois, échecs programmables, verrou interne
```

`FakePrinterTransport` expose `sent: [(printerId: UUID, payload: [UInt8])]`, `failNext(_ count: Int, error:)`, `failAlways(for printerId:)`, `setOnline(_:for:)`, et un point de suspension optionnel (`gate`) pour simuler un envoi lent dans les tests de réentrance.

- [ ] **Step 1 : tests (RED).** LAN : un `NWListener` sur le port 0 de `127.0.0.1` reçoit exactement les octets envoyés (comparaison octet pour octet, charge de 100 Ko pour couvrir plusieurs paquets), la connexion se ferme proprement ; port fermé → erreur `connection` (immédiate) ; adresse non joignable (utiliser une adresse de la plage de documentation `192.0.2.1` avec `connectTimeout` de 0,3 s) → `connectTimeout` en moins de 2 s ; écriture bloquée (listener qui n'accepte pas de lire, charge > tampons) avec `writeTimeout` court → `writeTimeout`. `Routing` : `lan` aiguillé vers le transport LAN injecté, `star` sans transport enregistré → `unsupported(.star)`, `star` enregistré → appelé avec la même imprimante. `FakePrinterTransport` : les échecs programmés sont consommés dans l'ordre. Si un test réseau local est instable en CI (sandbox sans loopback), isoler ces tests derrière un trait/tag et le consigner dans le fichier de suivi.
- [ ] **Step 2 : implémenter.** `LANPrinterTransport` ouvre un `NWConnection` TCP vers `ipAddress:port`, arme un délai de connexion (annule l'état `preparing`), écrit en une passe avec `send(content:completion:)` et un délai d'écriture, ferme (`cancel`) et rend la main ; toute erreur du framework est enveloppée dans `PrinterTransportError.connection(message)`. Aucune dépendance globale ; chaque appel crée sa connexion (comme `EscPosSender`). Sendable propre sous Swift 6 (pas d'état partagé non protégé).
- [ ] **Step 3 : suites complètes + commit** (`feat(ios): PrinterTransport, transport LAN et faux transport (4b)`).

---

### Task 3: Moteur de file (cycle, relances, états) et API d'administration

**Files:**
- Create: `…/Printing/PrinterStatusTracker.swift`, `…/Local/LocalPosAPI+PrintWorker.swift`
- Modify: `…/Local/LocalPosAPI.swift` (init : `transport: any PrinterTransport = RoutingPrinterTransport()`, `printEnabled`), `…/Local/LocalPosAPI+Printers.swift` (remplacer `testPrinter`, `printerStatuses`, `printJobs`, `retryPrintJob`, `cancelPrintJob`)
- Test: `Tests/PosKitTests/Local/LocalPrintQueueProcessorTests.swift`, `LocalPrinterAdminAPITests.swift`, `PrinterStatusTrackerTests.swift`

**Interfaces:**
- Consumes : tâches 1 et 2 ; `TicketPayload.build(printer:document:openCashDrawer:)`, `TicketDocumentJSON` (lot 4a).
- Produces :

```swift
struct PrinterState: Equatable, Sendable { var isOnline: Bool?; var sinceUtc: Date? }    // nil = aucun envoi depuis le démarrage
struct PrinterStatusTracker: Sendable {
    func get(_ id: UUID) -> PrinterState
    /// true si la transition doit être notifiée (en ligne ↔ hors ligne, ou premier échec) ; le premier succès est silencieux.
    mutating func record(_ id: UUID, success: Bool, at: Date) -> Bool
}

extension LocalPosAPI {
    static func retryDelay(attempts: Int) -> TimeInterval        // 5, 5, 15, 30, 60…
    /// Un cycle complet : annulations d'imprimantes inactives, échéances, puis envoi des jobs échus de chaque imprimante.
    func runPrintCycle() async
    func startPrintWorker()          // lance la boucle d'arrière-plan (idempotent) : réveil par signal ou toutes les 5 s, purge une fois par jour
    func stopPrintWorker()
    func notifyPrintQueue()          // réveille la boucle après un enqueue
}
```

`runPrintCycle` est la pièce testable (les tests l'appellent à la main avec l'horloge injectée). Algorithme (port de `PrepareAsync` + `ProcessPrinterAsync` + `PurgeAsync`) :
1. `prepare` : pour chaque job `Pending` : imprimante absente ou inactive → `Cancelled` (`LastError` « Printer deleted » / « Printer disabled ») ; échéance dépassée → `Failed` (`LastError ??= "Deadline exceeded"`). Imprimantes ayant un `Pending` dont `NextAttemptAtUtc <= now`.
2. `processPrinter(id)` : jobs `Pending` de l'imprimante dans l'ordre de création ; pour chacun : si `NextAttemptAtUtc > now` → arrêt de la file de cette imprimante ; `attempts += 1` ; rendu `TicketPayload.build(printer:document:openCashDrawer:)` puis `try await transport.send(payload, to: printer)` ; succès → `Sent`, `SentAtUtc = now`, `LastError = nil`, `tracker.record(success: true)` ; échec → `LastError` = message, `Failed` si `now >= deadline` sinon `NextAttemptAtUtc = now + retryDelay(attempts)`, `tracker.record(success: false)`, **arrêt de la file de cette imprimante**. Un document JSON illisible compte comme un échec du job (pas de crash).
3. Garde de réentrance : `var printersInFlight: Set<UUID>` ; `processPrinter` sort tout de suite si l'imprimante y est déjà ; le job est marqué (tentative incrémentée et sauvegardée) AVANT l'attente réseau, et sa lecture/écriture suivante se fait en relisant la ligne (un job annulé pendant l'envoi reste `Cancelled` : relire le statut avant d'écrire `Sent`).
4. `purge` : appelé par la boucle une fois par jour d'horloge (et testable directement).

API d'administration (comme `PrintJobEndpoints.cs`) :
- `printerStatuses()` : authentifié ; par imprimante `isActive`, `isOnline`/`sinceUtc` du suivi, `pendingCount`, `failedCount`.
- `printJobs(printerId:)` : responsable ; statuts ouverts `Pending` et `Failed` ; 100 plus récents ; 404 si imprimante inconnue (comportement actuel conservé).
- `retryPrintJob(id:)` : responsable ; `Failed` → `Pending`, tentatives 0, `NextAttemptAtUtc = now`, échéance +30 min, `LastError = nil`, réveil de la boucle. `Sent` d'un reçu ou d'un rapport → duplicata : nouveau job (même imprimante et mêmes `kind`, document `TicketBuilder.withDuplicateNotice`, tiroir jamais ouvert, `duplicateOfDocumentId = job.duplicateOfDocumentId ?? job.id`, numéro = prochain numéro de duplicata de ce document) et entrée JET `DUPLICATE_PRINTED` (`documentType: "print_job"`, `originalJobId`, `duplicateNumber`, `printerId`) dans la même transaction. Autres cas (`Pending`, `Cancelled`, `Sent` d'un bon cuisine ou de retrait, document illisible) → 409 « Impression non relançable ». Introuvable → 404.
- `cancelPrintJob(id:)` : responsable ; `Pending`/`Failed` → `Cancelled` ; sinon 409 ; introuvable 404.
- `testPrinter(id:)` : responsable ; imprimante inconnue 404 ; imprime IMMÉDIATEMENT (hors file) `TicketBuilder.testPage(printer:language:now:)` (langue des tickets des réglages) et répond `TestPrintResult(success:message:)` selon le résultat (le comportement exact, le message et l'éventuelle journalisation suivent l'endpoint .NET `POST /api/printers/{id}/test` : le lire et le reproduire).

- [ ] **Step 1 : tests (RED)** — avec `FakePrinterTransport` et une horloge mutable ; la base est créée par `LocalPosAPI(path: ":memory:", clock:…, transport: fake)` ; les imprimantes sont insérées par `LocalPrinterRepository`, les jobs par `LocalPrintQueueRepository.enqueue`. Au minimum :
  1. un job part et passe `Sent` ; `SentAtUtc` posé ; une seule tentative ;
  2. échec puis succès : après un échec, `Attempts == 1`, `NextAttemptAtUtc == now + 5 s`, `LastError` posé ; un cycle avant l'échéance n'envoie rien ; après avancement de l'horloge de 5 s, le job part ;
  3. les délais de relance successifs sont 5, 15, 30, 60, 60 s (tester `retryDelay` et un scénario complet) ;
  4. échéance : imprimante hors ligne pendant 30 min → `Failed` (« Deadline exceeded » ou le dernier message), la file de l'imprimante est débloquée ;
  5. ordre FIFO strict par imprimante et blocage : job A échoue → job B de la même imprimante n'est pas tenté ; l'imprimante 2 imprime pendant ce temps ;
  6. imprimante désactivée ou supprimée → ses jobs `Pending` passent `Cancelled` avec le bon message ;
  7. réentrance : avec `gate` suspendant `send`, un second `runPrintCycle` lancé pendant l'envoi n'envoie pas le job une seconde fois (`sent.count == 1`) ;
  8. job annulé pendant l'envoi : reste `Cancelled` ;
  9. document JSON illisible → échec géré (pas de crash), job relançable ;
  10. reprise au lancement : des jobs `Pending` créés par une première instance sont envoyés par une seconde instance sur la même base (utiliser un fichier SQLite temporaire) ;
  11. purge : `Sent`/`Cancelled` de plus de 7 jours supprimés, `Failed` et récents conservés ;
  12. suivi d'état (`PrinterStatusTrackerTests`) : premier succès silencieux, premier échec notifié, bascule en ligne ↔ hors ligne notifiée, répétition identique non notifiée ;
  13. API : statuts (compteurs), liste de jobs (statuts ouverts, 404, droits responsable), `retry` (échec → pending ; sent reçu → duplicata numéroté avec JET et sans tiroir ; sent bon de retrait → 409 ; pending → 409), `cancel` (409 sur `Sent`), `testPrinter` (succès et échec du transport).
- [ ] **Step 2 : implémenter** (acteur : aucune capture de `db` dans une tâche détachée ; la boucle `startPrintWorker` est une `Task` qui rappelle `runPrintCycle()` sur l'acteur ; `deinit`/`stop` l'annule).
- [ ] **Step 3 : suites complètes + commit** (`feat(ios): moteur de file d'impression, relances et administration des jobs (4b)`).

---

### Task 4: Données de rapport (`ReportPrintData`) avec fixture .NET

**Files:**
- Create: `…/Local/LocalReportPrintData.swift` ; côté .NET : un test générateur de fixture (voir ci-dessous) ; `Tests/PosKitTests/Fixtures/report_print_data_reference.json`
- Test: `Tests/PosKitTests/Local/LocalReportPrintDataTests.swift` ; extension de `LocalDaySimulationTests` si la journée simulée sert de source

**Interfaces:**
- Consumes : `ReportPrintData` et ses lignes (lot 4a, `TicketInputs.swift`).
- Produces :

```swift
extension LocalFiscalRepository {   // ou LocalReportPrintData struct, au choix de l'implémenteur, tant que l'API suivante existe
    /// Port de ReportPrintDataService.BuildAsync pour le poste autonome T01 (périmètre : reçus du poste, hors annulés, hors avoirs, TTC > 0, dans la période [début ; fin]).
    func reportPrintData(terminalId: String, periodStart: Date?, periodEnd: Date) throws -> ReportPrintData
}
enum FrenchCollation { static func compare(_ a: String, _ b: String) -> ComparisonResult }   // StringComparer(fr, ignoreCase)
```

Règles (lire `ReportPrintDataService.cs` en entier) : commandes des reçus retenus (reçu non annulé, pas un avoir, TTC > 0, période bornée par `CreatedAtUtc`), statut ≠ `Cancelled` ; lignes groupées par famille du produit (famille inconnue → `categoryName == nil`, triée en dernier) puis par nom d'article (somme des quantités, somme `lineTtc` = 0 pour un offert, sinon total TTC de la ligne) ; tri des noms avec `FrenchCollation` ; pourboires par serveur (opérateur de la commande), serveur inconnu → `nil` en dernier ; `totalTipsCents` = somme ; `hasGlobalDiscount` = au moins une commande avec remise globale. Le .NET traite `POS_MAIN_TERM` ou un terminal vide comme « tous les terminaux » : sur l'iPad le poste est toujours `T01`, le résultat est celui de `T01`.

**Preuve .NET** : `report_print_data_reference.json` est GÉNÉRÉ par le .NET. Étudier d'abord comment la journée simulée est produite et rejouée (`tests/RestaurantPos.Api.Tests/DaySimulation`, `day_scenario.json`, `LocalDaySimulationTests.swift`) : si le test .NET de la journée peut aussi appeler `ReportPrintDataService.BuildAsync(terminal, début, fin)` pour les mêmes périodes que les rapports X et Z qu'il compare déjà, ajouter ce relevé à une nouvelle fixture et le comparer côté Swift dans `LocalDaySimulationTests` (même scénario rejoué par l'API Swift). Ajouter à cette fixture une section `collation` : la liste triée par `StringComparer.Create(fr, ignoreCase: true)` d'environ 30 noms pièges (accents, majuscules, espaces, apostrophes, chiffres, « œ », « ß », « Éclair », « eclair », « Ecuelle »…), que Swift doit reproduire dans le même ordre. Commande de génération documentée dans `CLAUDE.md` (variable `WRITE_REPORT_PRINT_FIXTURE=1`). Si la journée simulée ne permet pas de mesurer un cas (pourboires, remise globale, offert, famille inconnue), l'implémenteur ajoute un petit scénario Swift ET le même scénario .NET à la fixture ; tout écart se tranche en faveur de la fixture.

- [ ] **Step 1 : générateur .NET et fixture (RED côté Swift)**, puis **Step 2 : tests Swift** (comparaison section par section : catégories, articles, pourboires, remise, tri, `nil` en dernier ; section `collation`), **Step 3 : implémenter**, **Step 4 : suites complètes + commit** (`feat(ios): données de rapport (articles vendus, pourboires), identiques au .NET (4b)`).

---

### Task 5: Répartiteur (`PrintDispatch`) : documents, imprimantes, tiroir, duplicata

**Files:**
- Create: `…/Local/LocalPosAPI+PrintDispatch.swift`
- Modify: `…/Local/LocalFiscalRepository.swift` (`recordDuplicate…` reçoit `printQueued`)
- Test: `Tests/PosKitTests/Local/LocalPrintDispatchTests.swift`

**Interfaces:**
- Consumes : tâches 1 à 4 ; `TicketBuilder` et `TicketPayload` (lot 4a) ; `LocalFloorRepository.settings()` ; `LocalKitchenRepository` ; `LocalOrderRepository` ; `LocalFiscalRepository`.
- Produces (internes à `LocalPosAPI`, tous **non-`throws`** : ils capturent toute erreur, la journalisent par `os.Logger` (catégorie « print ») et renvoient `false`) :

```swift
extension LocalPosAPI {
    func queueCounterSale(orderId: UUID, receiptNumber: String, withFiscalReceipt: Bool, hasCash: Bool) -> Bool
    func queueTableReceipt(orderId: UUID, receiptNumber: String, hasCash: Bool) -> Bool
    func queueKitchenTickets(ticketIds: [UUID])                       // jamais d'échec visible
    func queueXReport(summary: …) -> Bool
    func queueZClosure(_ closure: DailyFiscalClosureRecord) -> Bool
    func queuePeriodClosure(_ closure: PeriodClosureRecord) -> Bool
    func queueReceiptReprint(_ receipt: FiscalReceiptRecord, operatorId: UUID?, now: Date) throws -> (printQueued: Bool, duplicateNumber: Int)
    func queueZClosureReprint(_ closure: DailyFiscalClosureRecord, operatorId: UUID?, now: Date) throws -> (printQueued: Bool, duplicateNumber: Int)
}
```

Règles (port de `PrintDispatcher.cs`, relu en entier ; ce qui suit liste les pièges) :
- **Imprimante de ticket** : le poste autonome n'a pas d'appareil appairé : première imprimante ACTIVE affectée à la station `RECEIPT`, triée par nom au sens ordinal (le repli du .NET). Aucune → `false` (avertissement journalisé, pas d'exception).
- **Vente comptoir** (`queueCounterSale`) : avec ticket fiscal demandé → un seul job `Receipt` = `TicketBuilder.fiscalReceipt(receipt, order, pickupNumber, buzzer, …)` ; sinon un job `PickupVoucher` = `pickupCoupon(order, pickup, buzzer, language, now)`. Tiroir : `hasCash && printer.openCashDrawerOnReceipt`.
- **Paiement à table** : job `Receipt` = `TicketBuilder.receipt(…)` ; seulement quand l'appelant le demande (voir tâche 6 : commande entièrement réglée ET case « imprimer le ticket »).
- **Langues** : tickets et rapports dans `settings.receiptLanguage`, bons cuisine dans `settings.kitchenTicketLanguage`.
- **Bons cuisine** : pour chaque bon, imprimantes ACTIVES dont `assignedStationIds` contient `ticket.stationId` ; aucune → avertissement et on passe ; un job `KitchenTicket` par imprimante, jamais de tiroir ; une erreur sur une imprimante ne bloque pas les autres.
- **Rapports** : `Report` sur l'imprimante de ticket, tiroir jamais ; X : `TicketBuilder.xReport(summary, data, …, now)` ; Z : `zClosure(closure, data, …)` avec `data = reportPrintData(terminal, closure.periodStart, closure.closedAt)` ; période : `periodClosure(…)` avec `data` sur sa période ; `duplicateOfDocumentId` des rapports d'origine = identifiant de la clôture Z (comme le .NET) sinon `nil`.
- **Duplicata** (`queueReceiptReprint`, `queueZClosureReprint`) : dans UNE transaction : prochain numéro (`nextDuplicateNumber`), si une imprimante de ticket existe un job (`Receipt` ou `Report`, tiroir faux, `duplicateOfDocumentId` = document, `duplicateNumber`) avec le document en duplicata (`duplicateNumber:` passé au constructeur ; `receiptFallback` si la commande du reçu a disparu), et l'entrée JET `DUPLICATE_PRINTED` écrite avec `printQueued` = vrai si un job a été mis en file (le lot 3 l'écrivait toujours à faux : `recordDuplicate`/`recordClosureDuplicate` reçoivent donc un paramètre `printQueued: Bool`, défaut `false` pour les appelants existants jusqu'à la tâche 6). Une erreur ici renvoie `(false, 0)` comme le .NET et ne propage rien, sans laisser de numéro consommé à moitié (transaction annulée).
- **Mapping des données** : écrire des fonctions pures et testées qui convertissent les lignes SQL/enregistrements en entrées `Ticket*` : `TicketReceipt` (numéro, `createdAtUtc` du reçu, poste, TTC/HT, `taxBreakdownJson`, signature), `TicketOrder` (destination, table, numéro de retrait, bip, lignes avec quantité, nom, `productId`, total TTC de la ligne = celui du .NET `CalculateTotalTtc()` : réutiliser `OrderMath.lineTotal` et vérifier remise de ligne, offert, modificateurs, Happy Hour par un test), `TicketSettings` (depuis `RestaurantSettings` ; chaînes vides → valeurs par défaut du builder), `TicketKitchen`, `TicketDailyClosure`, `TicketPeriodClosure`, `TicketReportSummary` (rapport X du poste). `TicketContext` : `timeZone = calendar.timeZone`, `softwareVersion` = constante `LocalPosAPI.softwareVersion` (« 1.0.0 » pour la parité de test ; l'app peut la remplacer par la version du bundle plus tard — la lire de `Bundle.main` est HORS périmètre).
- Les documents sont sérialisés par `TicketDocumentJSON.serialize` dans `DocumentJson` (relus par le moteur avec `deserialize`).

- [ ] **Step 1 : tests (RED)** — scénarios de `docs/impression.md` §3 contre une base `:memory:` démo, avec `FakePrinterTransport` et sans lancer le worker ; on lit les jobs en base : imprimante choisie, `kind`, `openCashDrawer`, document (`TicketDocumentJSON.deserialize`) comparé au document que `TicketBuilder` produit directement pour les mêmes données :
  1. comptoir sans ticket → un job `PickupVoucher`, espèces + imprimante avec tiroir → tiroir vrai ; carte → faux ; imprimante sans tiroir → faux ;
  2. comptoir avec ticket → UN seul job `Receipt` contenant ticket puis bon de retrait (séparateur de coupe) ;
  3. paiement à table → job `Receipt` ; document égal à `TicketBuilder.receipt(…)` ;
  4. envoi cuisine : deux postes → deux jobs, une copie par imprimante du poste, aucun job pour un poste sans imprimante, pas de tiroir ; imprimante inactive ignorée ;
  5. aucune imprimante `RECEIPT` active → `false`, aucun job, aucune exception ; plusieurs → la première par nom ordinal ;
  6. rapports X, Z, période : un job `Report`, tiroir faux, `duplicateOfDocumentId` = id de clôture pour Z ;
  7. duplicata de reçu : numéro 1 puis 2, document avec « DUPLICATA n°N », tiroir faux, JET `DUPLICATE_PRINTED` avec `printQueued` vrai ; sans imprimante : numéro consommé, `printQueued` faux, JET écrit ;
  8. échec SQL simulé à la mise en file (par exemple `DROP TABLE PrintJobs` dans la base de test ou imprimante dont l'insertion échoue) → `false`, jamais d'exception, la vente déjà validée est intacte ;
  9. mapping : une commande avec ligne offerte, ligne remisée, modificateurs payants et Happy Hour donne les mêmes totaux de ligne que `Order.CalculateTotalTtc` du .NET (vérifier avec des valeurs de la fixture `fiscal_reference.json` ou calculées à la main avec `OrderMath`) ; commande à emporter vs sur place → mode correct ; langue `ar` → document RTL.
- [ ] **Step 2 : implémenter. Step 3 : suites complètes + commit** (`feat(ios): répartiteur d'impression (tickets, bons cuisine, rapports, duplicata) (4b)`).

---

### Task 6: Branchement dans les parcours et `printQueued`

**Files:**
- Modify: `…/Local/LocalPosAPI+CounterCheckout.swift`, `+Payments.swift`, `+Kitchen.swift`, `+Fiscal.swift`, `+PeriodClosures.swift`
- Test: `Tests/PosKitTests/Local/LocalPrintFlowTests.swift`, ajustements des tests existants qui attendaient `printQueued == false`

**Interfaces:**
- Consumes : tâches 3 à 5.
- Produces : les réponses d'API portent le vrai `printQueued`.

Règles (docs/impression.md §3 et .NET) :
- `counterCheckout` : après la transaction, `printQueued = queueCounterSale(…)` seulement si le règlement solde la commande (reste à payer nul) ; paiement partiel → `false` sans appel. Tiroir : au moins un règlement en espèces. Le champ `openCashDrawer` actuel du résultat est inchangé.
- Paiement à table (`pay`, `LocalPosAPI+Payments.swift`) : `queueTableReceipt` seulement si la requête demande le ticket ET la commande est entièrement réglée ; sinon `false`.
- `dispatch(table:)` et `fireSuite(table:)` : après la transaction, `queueKitchenTickets(ticketIds:)` avec les identifiants des bons créés (adapter la fonction pour que la transaction renvoie les ids) ; l'échec d'impression ne change jamais le résultat de l'envoi cuisine.
- `printXReport(terminalId:)` renvoie le booléen réel de `queueXReport`.
- Clôture Z (`LocalPosAPI+Fiscal.swift`) : `printQueued` du résultat = `queueZClosure(closure)` (après la transaction de clôture ; une erreur d'impression ne défait jamais la clôture).
- Clôture de période (`LocalPosAPI+PeriodClosures.swift`) : idem avec `queuePeriodClosure`.
- `reprintReceipt` / `reprintLatestClosure` : utilisent `queueReceiptReprint` / `queueZClosureReprint` ; le résultat porte `printQueued` et `duplicateNumber` réels.
- Le worker démarre à l'ouverture de l'API: `LocalPosAPI.init` ne lance pas de tâche (testabilité) ; c'est l'application qui appelle `startPrintWorker()` après création (voir tâche 7 pour l'appel dans l'app, 4d pour l'interface).

- [ ] **Step 1 : tests (RED)** de bout en bout par l'API publique (`counterCheckout`, `pay`, `dispatch`, `fireSuite`, `printXReport`, clôtures, `reprintReceipt`, `reprintLatestClosure`) avec `FakePrinterTransport` : `printQueued` vrai/faux selon chaque règle ci-dessus ; après `runPrintCycle()` le faux transport a reçu les octets attendus (au moins le préfixe d'initialisation et, pour un ticket, que la charge décodée par le rendu texte contient le numéro de reçu) ; imprimante hors ligne (`setOnline(false)`) : vente validée, `printQueued == true`, un cycle plus tard le job est `Pending` avec `LastError`, rétablissement : il part ; panne SQL à la mise en file : vente validée et `printQueued == false` ; deux ventes d'affilée préservent l'ordre d'impression ; les tests existants qui assertaient `printQueued == false` sont mis à jour (ils n'avaient pas d'imprimante RECEIPT : vérifier d'abord que la base démo en a ou pas, et garder leur sens).
- [ ] **Step 2 : implémenter. Step 3 : suites complètes (PosKit, .NET, `./scripts/test.sh unit`) + commit** (`feat(ios): impression branchée sur ventes, cuisine, rapports et duplicata (4b)`).

---

### Task 7: Démarrage du worker dans l'app, documentation et vérification finale

**Files:**
- Modify: le point de création de `LocalPosAPI` dans l'application (`ios/Packages/PosKit/Sources/PosKit/Stores/AppContext.swift` ou l'endroit qui construit le mode autonome : `grep -rn "LocalPosAPI(" ios/`), `CLAUDE.md`, `docs/plans/ipad-standalone-4b-progress.md`

- [ ] **Step 1 : démarrage.** Après création de `LocalPosAPI` pour l'app (jamais dans les tests unitaires), appeler `await api.startPrintWorker()` ; en `-UITestLocal` utiliser un `FakePrinterTransport` (pas d'accès réseau pendant les tests UI) via le même mécanisme de configuration que les autres options de test (`-UITestLocal`). Vérifier que l'app compile (`xcodebuild … -destination 'generic/platform=iOS Simulator' build`). Un test PosKit vérifie que `startPrintWorker()` est idempotent et que `stopPrintWorker()` arrête la boucle (via compteur de cycles du faux transport + horloge réelle courte, ou un point d'observation `cycleCount`).
- [ ] **Step 2 : `CLAUDE.md`.** Étendre le paragraphe « Impression, lot 4a » (ou en ajouter un « lot 4b ») : tables et migration V10, colonnes propres à l'iPad, protocole de transport et `FakePrinterTransport`, règles de file (délais, échéance, blocage FIFO, annulation, purge, reprise), `printQueued` et jamais d'échec de vente, imprimante de ticket = première `RECEIPT` active par nom, `ReportPrintData` et `report_print_data_reference.json` (commande de génération), duplicata (JET `printQueued` réel, `documentType`), limite connue (pas de notification temps réel : l'écran du lot 4d rafraîchit par `printerStatuses()`), et la règle de suivi : toute modification de `PrintDispatcher`, `PrintQueueProcessor`, `PrintJob` ou `ReportPrintDataService` côté .NET impose de reporter dans `Local/` / `Printing/` et de régénérer la fixture.
- [ ] **Step 3 : vérifications finales** : `dotnet test RestaurantPos.slnx`, `cd ios/Packages/PosKit && swift test`, `cd ios && xcodegen generate && ./scripts/test.sh unit`, build simulateur de l'app. Noter les compteurs.
- [ ] **Step 4 : commit** (`docs: lot 4b fait (file d'impression et moteur)`), push, puis relire le diff de la branche entière pour la revue finale.
