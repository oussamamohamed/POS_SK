# iPad standalone 3a — suivi d'exécution (clôtures de période)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md`

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Fixture .NET `period_closure_reference.json` | fait | 754 | 437 | - | aucun |
| 2 Signature des clôtures de période | fait | 754 | 437 | - | aucun |
| 3 Migration V8 (table `PeriodClosures`) | fait | 754 | 437 | - | aucun |
| 4 Dépôt (`LocalFiscalRepository+PeriodClosures`) | fait | 754 | 437 | - | mineurs différés |
| 5 API `LocalPosAPI` | fait | 754 | 437 | - | mineurs différés |
| 6 Vérification et documentation | fait | 754 | 437 | - | aucun |

Nombres finaux : .NET 754 (Domain 68 + Infrastructure 429 + Api 257) · PosKit 437.

Écart .NET relevé, hors lot 3a : le premier Z d'un poste a PeriodStartUtc = MinValue (NF525FiscalAuditService.cs:63), donc une clôture mensuelle l'exclut de ses totaux (le CA du premier jour de service n'est dans aucune clôture mensuelle ni annuelle). Le Swift reproduit ce comportement pour la parité ; à corriger des deux côtés après décision de l'utilisateur.

Écarts .NET connus (à trancher avec l'utilisateur, avant le lot 3d) :
- Premier Z : `PeriodStartUtc` vaut MinValue côté .NET et l'epoch 1970 côté iPad ; il est exclu des totaux mensuels. Effet : si le premier Z est le seul de son mois et que ce jour a des reçus, la clôture mensuelle échoue avec `missing_daily_closures` bien qu'un Z existe (contournement : lancer un autre Z).
- Z vide : le Z .NET démarre à la fin du dernier Z non vide (`NF525FiscalAuditService.cs:57-63`, `TotalSalesTtc > 0`), le Z iPad à la fin du dernier Z de tout type (`latestClosure`) ; les totaux mensuels peuvent différer quand un Z vide précède. Comportement Swift conservé.
