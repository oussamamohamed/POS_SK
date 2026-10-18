# iPad standalone 3c — suivi d'exécution (export FEC)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-14-ipad-standalone-3c-fec.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `fec_reference.json` | fait | non relevé | - | - | voir mineurs reportés |
| Correctif culture `FormatRateForLabel` (libellés du FEC) | fait | 439 (Infrastructure.Tests) | - | - | voir mineurs reportés |
| 2 Formats et comptes (`LocalFecFormat`) | fait | - | non relevé | - | voir mineurs reportés |
| 3 Ventilation HT / TVA (`LocalFecBreakdown`) | fait | - | non relevé | - | voir mineurs reportés |
| 4 Écritures (`LocalFecWriter`) | fait | - | non relevé | - | voir mineurs reportés |
| 5 Export SQLite + rejeu octet pour octet | fait | - | 516 | - | voir mineurs reportés |
| 6 API, JET `FEC_EXPORTED`, documentation | fait | 765 (Domain 69, Infrastructure 439, Api 257) | 519 (73 suites) | - | voir mineurs reportés |

Les cellules « non relevé » (tâches 1-4) ne peuvent plus être reconstituées : les comptes de tests n'ont pas été relevés par tâche.

Vérification finale (tâche 6) : `dotnet test RestaurantPos.slnx` 765/765, `swift test` 519/519.

Mineurs reportés : remise de commande et sur place/emporter non exercés par la fixture (les reçus ne stockent ni commande ni destination) ; quirk de fin de ligne du format ; tests de format sans chaînes tirées de la fixture et sans montant négatif ; `parseIntObject` garde les clés dupliquées (le .NET garde la dernière) ; parseur de taux limité aux décimaux simples ; chargeur ORDER BY SequenceNumber, reçu HT=0/TVA=0 et plusieurs règlements avec pourboire à couvrir (tâche 4, reportés en 5) ; l'échec d'export n'écrit aucune entrée JET (export avant l'ajout, dans la même transaction) mais sans test dédié.

Correctifs de revue finale : docs/fiscal.md et CLAUDE.md alignés sur le code ; tests d'échappement JSON (.NET `SystemTextJson_DefaultEscaping_MatchesTheIpadFiscalJsonString`, Swift `fiscalJSONStringEscapesLikeSystemTextJson` et `sirenIsEscapedIdenticallyInTheJournalPayload`). Comptes : PosKit 521 (73 suites), Infrastructure.Tests 440.

Risques pour l'expert-comptable : dates d'écriture au jour UTC (une vente à 00:30 heure de Paris le 2 est datée du 1er, à valider avec l'expert-comptable ; correction = dates de Paris des deux côtés + régénération de la fixture) ; reçu historique volontairement déséquilibré ; compte 580000 avec deux libellés (titres-restaurant et moyen inconnu) ; facturation chambre (411000) présente seulement dans le FEC iPad.
