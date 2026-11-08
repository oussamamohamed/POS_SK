# Modèle de données : Alertes de pilotage et contrôle de caisse

Tous les montants en centimes entiers ; dates en texte ISO à la milliseconde. SQL idempotent dans `Program.cs` ; migration iPad correspondante.

## CashSessions (ajout seulement)

| Champ | Type | Règle |
|---|---|---|
| `Id` | UUIDv7 | |
| `TerminalId` | texte | pris de l'appareil appairé |
| `OpenedAtUtc` | texte | |
| `OpenedByOperatorId` | UUID | |
| `OpeningFloatCents` | INTEGER ≥ 0 | |

Index unique partiel : une seule session sans comptage par poste.

## CashMovements (ajout seulement)

`Id`, `SessionId`, `Kind` (`in` ou `out`), `AmountCents` > 0, `Reason` (texte obligatoire, 3 à 200 caractères), `OperatorId`, `CreatedAtUtc`.

## CashCounts (ajout seulement)

`Id`, `SessionId` (unique), `ClosedAtUtc`, `ClosedByOperatorId`, `CountedCents`, `ExpectedCents`, `VarianceCents`, `DenominationsJson` (liste `{valeurEnCentimes, quantité}` facultative), `VarianceNote` (obligatoire si l'écart dépasse le seuil actif).

L'attendu et l'écart sont figés à la clôture. Déclencheurs `RAISE(ABORT, 'cash_append_only')` sur UPDATE et DELETE des trois tables.

## AlertRules

| Champ | Type | Règle |
|---|---|---|
| `Type` | `cash_variance`, `voids_count`, `discount_percent`, `comps_total` | clé primaire |
| `Threshold` | INTEGER | centimes, nombre ou pourcentage selon le type |
| `Enabled` | INTEGER | 0 par défaut |
| `UpdatedAtUtc`, `UpdatedBy` | | |

Ligne par défaut insérée au démarrage avec `Enabled = 0`.

## Alerts

| Champ | Type | Règle |
|---|---|---|
| `Id` | UUIDv7 | |
| `RuleType` | texte | |
| `OperatorId` | UUID NULL | auteur concerné |
| `TerminalId` | texte | |
| `PeriodKey` | texte | `terminalId:numéroDuDernierZ` |
| `DedupeKey` | texte UNIQUE | `RuleType|OperatorId|PeriodKey` |
| `DetailJson` | texte | nombre, montant, identifiants des reçus ou commandes concernés |
| `State` | `open` ou `resolved` | |
| `CreatedAtUtc` | texte | |
| `ResolvedAtUtc`, `ResolvedBy`, `ResolutionNote` | | note obligatoire à la clôture |

## RestaurantSettings (colonnes ajoutées)

`TrendThresholdPercent` (15), `CashVarianceNoteRequired` (1).

## Journal technique (non fiscal)

Nouveaux types dans `JournalEventTypes` : `CASH_SESSION_OPENED`, `CASH_MOVEMENT`, `CASH_COUNTED`, `ALERT_CREATED`, `ALERT_RESOLVED`. Écrits par le service de journal technique existant, donc dans sa chaîne globale : leur ajout est un nouveau type d'événement dans la même formule, sans changement de format. Libellés `journal.<TYPE>` dans les trois i18n.

## Transitions

- Session : ouverte → comptée (insertion du comptage, jamais de mise à jour).
- Alerte : `open` → `resolved` avec note.
