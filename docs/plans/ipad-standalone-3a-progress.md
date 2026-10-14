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
| Correctif : début de période du Z (fix/debut-periode-z) | fait | 758 (Domain 68 + Infrastructure 433 + Api 257) | 443 | - | aucun |

Nombres finaux : .NET 754 (Domain 68 + Infrastructure 429 + Api 257) · PosKit 437.

Corrigé par fix/debut-periode-z : le premier Z d'un poste commence au premier reçu du poste (à défaut à la date du Z) au lieu de MinValue (.NET) / epoch (iPad), donc une clôture mensuelle le compte et ne produit plus `missing_daily_closures`.

Corrigé par fix/debut-periode-z : le Z démarre à la fin du dernier Z de tout type, vide ou non, des deux côtés (suppression de la préférence pour le dernier Z non vide côté .NET).
