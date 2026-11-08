# Spec 024 — Caisse, alertes, tendances : avancement

Étape par lot, tests : .NET / PosKit / web (décomptes finaux en bas).

| Lot | État |
|-----|------|
| A — Caisse et écart | serveur + web + iPad (local, mémoire, HTTP) terminés |
| B — Alertes | serveur + web + iPad terminés |
| C — Tendances | serveur + web + iPad terminés (`InsightsCard`, seuil dans Gestion → Alertes) |
| D — Résumé de fin de journée | serveur + web + iPad terminés (`DailySummarySheet`, `ShareLink`) |

## Écarts assumés

- T003 : attendu = `Amount` net du rendu. T022 fait : blocs « ACTIVITÉ » et « CAISSE » sur les rapports X/Z/période.
- T033/T051/T065/T070 : plutôt qu'étendre `day_scenario.json` (qui régénérerait les fixtures fiscales), journée de caisse dédiée avec oracle commun : `CashDaySimulationTests` (.NET) et `LocalCashDayTests` (iPad) ; `verify-chains` valide dans les deux. Les fixtures fiscales ne changent pas.
- T072 (quickstart à la main) : couvert par les tests automatisés (Playwright, XCUITest), pas déroulé manuellement.
- Annulations cumulées sur la semaine : non couvert (période = dernier Z).
- `insights.json` et `daily_summary.json` écrites d'après les DTO (non capturées sur une API vivante).

## Décomptes finaux

Voir la fin de ce fichier (mis à jour après les suites complètes).

- .NET : Domain 85 / Infrastructure 506 / Api 344, 0 échec, 0 avertissement.
- PosKit : 791 tests passés. Contrat live (`LiveAPI`) : 7 passés.
- Web (Playwright, base SQLite neuve) : 114 passés.
- UI iPad : 89 tests ; seuls `CashUITests` échouaient (simulateur resté en paysage, total compté hors écran) → `swipeUp` ajouté, relancés : verts (2/2). Les autres 87 passent.
- Revue : dérive de format du .NET restaurée (`ReportPrintDataReferenceFixtureTests`) ; XSS (`xss.spec.ts`) et clés i18n (`LocalizationTests`) verts.

- T022 : .NET 85 / 506 / 344, PosKit 792, 0 échec. Correctif au passage : `LocalCashRepository.expectedCents` excluait un règlement de la même milliseconde que le comptage (dates stockées arrondies) ; tolérance de 1 ms.

## Correctifs « Must fix » des revues

- `/api/kds/tickets` n'est plus anonyme ; les 50 derniers bons sont choisis sur deux colonnes puis chargés avec leurs lignes (test `GetTickets_ReturnsTheFiftyNewestWithTheirLines_NewestFirst`).
- Plus de clé JWT publique en repli : clé aléatoire par démarrage si `Jwt:Secret` est vide hors production.
- `AlertService.EvaluateAsync` ne charge les commandes que pour la période en cours de chaque poste.
- Migrations de schéma : `ApplySchema` ignore seulement « colonne/objet déjà présent » et journalise le reste (base ancienne et base neuve : 0 échec).
- `AlertEvaluationFilter` journalise l'erreur au lieu de l'avaler.
- Résultats : .NET 85 / 506 / 345, Playwright 114.
