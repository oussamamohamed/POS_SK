# iPad standalone 3c — suivi d'exécution (export FEC)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-14-ipad-standalone-3c-fec.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `fec_reference.json` | fait | non relevé | - | - | aucun |
| Correctif culture `FormatRateForLabel` (libellés du FEC) | fait | 439 (Infrastructure.Tests) | - | - | aucun |
| 2 Formats et comptes (`LocalFecFormat`) | fait | - | non relevé | - | aucun |
| 3 Ventilation HT / TVA (`LocalFecBreakdown`) | fait | - | non relevé | - | aucun |
| 4 Écritures (`LocalFecWriter`) | fait | - | non relevé | - | aucun |
| 5 Export SQLite + rejeu octet pour octet | fait | - | 516 | - | aucun |
| 6 API, JET `FEC_EXPORTED`, documentation | fait | 765 (Domain 69, Infrastructure 439, Api 257) | 519 (73 suites) | - | aucun |

Vérification finale (tâche 6) : `dotnet test RestaurantPos.slnx` 765/765, `swift test` 519/519.

Mineurs reportés : remise de commande et sur place/emporter non exercés par la fixture (les reçus ne stockent ni commande ni destination) ; quirk de fin de ligne du format ; tests de format sans chaînes tirées de la fixture et sans montant négatif ; `parseIntObject` garde les clés dupliquées (le .NET garde la dernière) ; parseur de taux limité aux décimaux simples ; chargeur ORDER BY SequenceNumber, reçu HT=0/TVA=0 et plusieurs règlements avec pourboire à couvrir (tâche 4, reportés en 5) ; l'échec d'export n'écrit aucune entrée JET (export avant l'ajout, dans la même transaction) mais sans test dédié.
