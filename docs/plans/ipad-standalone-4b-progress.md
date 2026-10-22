# iPad standalone 4b — suivi d'exécution (file d'impression et moteur)

Plan : `docs/superpowers/plans/2026-10-22-ipad-standalone-4b-file-moteur.md`

Base de référence : .NET 768 (Domain 69 + Infrastructure 442 + Api 257) · PosKit 597

| Tâche | Statut | .NET | PosKit | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Schéma V10, modèle `Printer` étendu, dépôt `PrintJobs` | fait | 767 verts + 1 échec hors périmètre (aucun changement .NET) | 610 | voir ci-dessous |
| 2 `PrinterTransport`, transport LAN, faux transport | fait | même état : 1 échec hors périmètre (flake minuit) | 620 | aucun ; tests réseau sur loopback réels (0,6 s), non isolés par tag |

## Constats ouverts

- `ReportAccuracyTests.FinancialDashboard_DiscountedOrders_AccuratelyReflectsNetTotals` échoue entre minuit et 02 h (heure de Paris) : le test compare `now.Date` local à des dates UTC. Sans rapport avec le lot 4b ; reproductible en relançant seul le test.
- `CLAUDE.md` mentionne encore « migrations jusqu'à V9 » : mis à jour à la tâche 7.
