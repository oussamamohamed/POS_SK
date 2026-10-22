# iPad standalone 4b — suivi d'exécution (file d'impression et moteur)

Plan : `docs/superpowers/plans/2026-10-22-ipad-standalone-4b-file-moteur.md`

Base de référence : .NET 768 (Domain 69 + Infrastructure 442 + Api 257) · PosKit 597

| Tâche | Statut | .NET | PosKit | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Schéma V10, modèle `Printer` étendu, dépôt `PrintJobs` | fait | 767 verts + 1 échec hors périmètre (aucun changement .NET) | 610 | voir ci-dessous |
| 2 `PrinterTransport`, transport LAN, faux transport | fait | même état : 1 échec hors périmètre (flake minuit) | 620 | aucun ; tests réseau sur loopback réels (0,6 s), non isolés par tag |
| 3 Moteur de file (cycle, relances, états), API d'administration | fait | 766 verts + 1 échec hors périmètre (flake minuit, 00 h 42 Paris) ; aucun changement .NET | 650 (après correctifs de revue : 2 exécutions complètes vertes) | voir ci-dessous |
| 4 Données de rapport (`ReportPrintData`) avec fixture .NET | fait | 767 verts + 1 échec hors périmètre (flake minuit, 01 h 23 Paris) : Domain 69, Infrastructure 440 + 1, Api 258 (+1 : générateur `ReportPrintDataReferenceFixtureTests`) | 657 | voir ci-dessous |
| 5 Répartiteur `PrintDispatch` (tickets, bons cuisine, rapports, duplicata) | fait | 767 verts + 1 échec hors périmètre (flake minuit, 01 h 37 Paris) : Domain 69, Infrastructure 440 + 1, Api 258 ; aucun changement .NET | 673 | voir ci-dessous |
| 6 Impression branchée dans les parcours, `printQueued` réel | fait | 767 verts + 1 échec hors périmètre (flake minuit, 01 h 45 Paris) : Domain 69, Infrastructure 440 + 1, Api 258 ; aucun changement .NET | 687 (`./scripts/test.sh unit` : 687) | voir ci-dessous |

## Constats ouverts

- `ReportAccuracyTests.FinancialDashboard_DiscountedOrders_AccuratelyReflectsNetTotals` échoue entre minuit et 02 h (heure de Paris) : le test compare `now.Date` local à des dates UTC. Sans rapport avec le lot 4b ; reproductible en relançant seul le test.
- `CLAUDE.md` mentionne encore « migrations jusqu'à V9 » : mis à jour à la tâche 7.
- Tâche 3 (corrigé) : bornes de durée des tests `LANPrinterTransportTests` relâchées à 30 s (instables sous la charge de la suite complète).
- Tâche 3 : écarts au .NET assumés : messages 409 repris des `.resx` FR du .NET (« Seules les impressions en échec peuvent être relancées. », « Cette impression ne peut plus être annulée. ») au lieu de « Impression non relançable » du plan ; `testPrinter` d'une imprimante inconnue en 404 (le .NET lève une exception) ; `printJobs` d'une imprimante inconnue en 404 (le .NET renvoie une liste vide) ; aucun notificateur d'état (pas de SignalR sur l'iPad : le retour de `PrinterStatusTracker.record` est ignoré jusqu'à 4d).
- Tâche 3 : `nextDuplicateNumber` prend désormais aussi le maximum de `PrintJobs.DuplicateNumber` du document (`DuplicateNumbering.NextAsync`).
- Le nombre de tests Infrastructure .NET affiché est 441 (440 + 1 flake) contre 442 dans la base de référence, sans aucun changement .NET dans le lot : à recompter hors de la fenêtre minuit-2 h.
- Tâche 4 : fixture `report_print_data_reference.json` générée par `WRITE_REPORT_PRINT_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests --filter ReportPrintDataReferenceFixtureTests` (sections `collation`, `collationPairs`, `scenarios`, `day`) ; commande à consigner dans `CLAUDE.md` (tâche 7).
- Tâche 4 : tri français mesuré sur l'ICU système de macOS (.NET 10 pour Api.Tests, sans `InvariantGlobalization`) ; `FrenchCollation` = Foundation `.caseInsensitive` (locale « fr » figée) + trois écarts du .NET reproduits (pleine chasse triée juste après la lettre ASCII ; ß/ss et ſ/s distincts ; NFKC et casse égaux). Un .NET en mode invariant ou un autre ICU (Linux) peut trier autrement.
- Tâche 4 (écart corrigé, trouvé par la journée simulée) : l'encaissement comptoir iPad n'attribuait pas la vente à l'opérateur qui encaisse (`OperatorId` restait `Guid.Empty`) ; porté depuis `CounterSaleEndpoints` (`claimOperatorIfUnknown`). Les pourboires comptoir vont désormais au caissier, comme le .NET.
- Tâche 4 : les égalités de tri (noms identiques à la casse près, serveurs inconnus) suivent l'ordre de création des commandes, puis des lignes ; côté .NET l'ordre est celui de la base (InMemory = insertion). Pas de garantie au-delà.
- Tâche 5 : les deux duplicata (`queueReceiptReprint`, `queueZClosureReprint`) ne sont pas `throws` (comme le .NET : erreur → `(false, 0)`, transaction annulée) ; le brief les déclarait `throws`.
- Tâche 5 : le JET `DUPLICATE_PRINTED` est écrit avant le job dans la transaction (il fixe le numéro via `nextDuplicateNumber`) ; le .NET écrit le job puis le JET : même état final, tout est annulé ensemble.
- Tâche 5 (écart corrigé) : le JET d'un duplicata de Z portait `documentType` « z_closure » ; le .NET écrit « closure ». Aligné sur le .NET.
- Tâche 5 : `duplicateOfDocumentId` vaut l'identifiant du reçu pour les tickets de caisse d'origine (comptoir avec ticket, table), comme le .NET (`receipt?.Id`) ; le brief ne le citait que pour la Z.
- Tâche 5 : `FiscalReceiptRecord` porte désormais `createdAtUtc` (date du ticket). Journalisation via le `Logger` existant de la tâche 3 (catégorie « Impression », pas « print »).
- Tâche 5 : `queueXReport(summary:terminalId:)` prend `LocalFiscalRepository.FiscalSummary` (type renvoyé par `summary(terminalId:now:)`) ; le poste est passé à part car ce type ne le porte pas.
- Tâche 5 : bons cuisine : commande d'un bon introuvable → arrêt de la boucle (comme le `FirstAsync` du .NET) ; bon introuvable → ignoré.
- Tâche 6 : toutes les mises en file ont lieu après le `COMMIT` de la transaction métier ; `reprintReceipt` et `reprintLatestClosure` n'ouvrent plus de transaction (recherche puis `queue*Reprint`, seule transaction) et n'appellent plus `recordDuplicate` / `recordClosureDuplicate` (un seul JET par duplicata, vérifié).
- Tâche 6 : comme le .NET, un échec d'écriture du duplicata rend désormais `ReprintResult(printQueued: false, duplicateNumber: 0)` au lieu d'une erreur.
- Tâche 6 : la base démo a une imprimante de ticket active (« Imprimante Caisse Comptoir », poste RECEIPT) : une vente comptoir soldée et un duplicata y rendent `printQueued == true` (tests existants mis à jour ; le test « sans imprimante » désactive les imprimantes démo).
- Tâche 6 (constat, hors périmètre) : les jobs d'une même imprimante créés à la même milliseconde (bons de plusieurs postes d'un même envoi vers une imprimante commune) sont départagés par `Id`, un UUID aléatoire : leur ordre d'impression entre eux n'est pas garanti. Un ordre par `rowid` (ou un UUID v7) le fixerait.
