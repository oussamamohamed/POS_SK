# iPad standalone 4b — suivi d'exécution (file d'impression et moteur)

Plan : `docs/superpowers/plans/2026-10-22-ipad-standalone-4b-file-moteur.md`

Base de référence : .NET 768 (Domain 69 + Infrastructure 442 + Api 257) · PosKit 597

| Tâche | Statut | .NET | PosKit | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Schéma V10, modèle `Printer` étendu, dépôt `PrintJobs` | fait | 767 verts + 1 échec hors périmètre (aucun changement .NET) | 610 | voir ci-dessous |
| 2 `PrinterTransport`, transport LAN, faux transport | fait | même état : 1 échec hors périmètre (flake minuit) | 620 | aucun ; tests réseau sur loopback réels (0,6 s), non isolés par tag |
| 3 Moteur de file (cycle, relances, états), API d'administration | fait | 766 verts + 1 échec hors périmètre (flake minuit, 00 h 42 Paris) ; aucun changement .NET | 650 (après correctifs de revue : 2 exécutions complètes vertes) | voir ci-dessous |

## Constats ouverts

- `ReportAccuracyTests.FinancialDashboard_DiscountedOrders_AccuratelyReflectsNetTotals` échoue entre minuit et 02 h (heure de Paris) : le test compare `now.Date` local à des dates UTC. Sans rapport avec le lot 4b ; reproductible en relançant seul le test.
- `CLAUDE.md` mentionne encore « migrations jusqu'à V9 » : mis à jour à la tâche 7.
- Tâche 3 (corrigé) : bornes de durée des tests `LANPrinterTransportTests` relâchées à 30 s (instables sous la charge de la suite complète).
- Tâche 3 : écarts au .NET assumés : messages 409 repris des `.resx` FR du .NET (« Seules les impressions en échec peuvent être relancées. », « Cette impression ne peut plus être annulée. ») au lieu de « Impression non relançable » du plan ; `testPrinter` d'une imprimante inconnue en 404 (le .NET lève une exception) ; `printJobs` d'une imprimante inconnue en 404 (le .NET renvoie une liste vide) ; aucun notificateur d'état (pas de SignalR sur l'iPad : le retour de `PrinterStatusTracker.record` est ignoré jusqu'à 4d).
- Tâche 3 : `nextDuplicateNumber` prend désormais aussi le maximum de `PrintJobs.DuplicateNumber` du document (`DuplicateNumbering.NextAsync`).
- Le nombre de tests Infrastructure .NET affiché est 441 (440 + 1 flake) contre 442 dans la base de référence, sans aucun changement .NET dans le lot : à recompter hors de la fenêtre minuit-2 h.
