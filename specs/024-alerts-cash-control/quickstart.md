# Guide de validation : Alertes de pilotage et contrôle de caisse

Prérequis : API en `Development` sur le port 5080 avec données de test ; PIN 1234 (responsable), 2468 (serveur) ; iPad ou web appairé.

## Lot A, caisse

1. Ouvrir une session avec un fond de 100 € ; encaisser 50 € en espèces avec rendu ; saisir une sortie de 12 € « Achat de glace ».
2. Le montant attendu est 138 € ; compter 136 € : l'écart de −2 € s'affiche, la note est demandée si l'écart dépasse le seuil.
3. Rejouer le comptage : même résultat. Tenter de modifier une ligne en base : refus (`cash_append_only`).
4. Tests : `dotnet test --filter CashSession`, `swift test --filter LocalCash`.

## Lot B, alertes

1. Activer le seuil « 3 annulations » ; un serveur fait 4 avoirs : une alerte unique nomme le serveur, le nombre et le montant, avec le lien vers les tickets.
2. Rafraîchir ou refaire une annulation : pas de doublon. Clôturer l'alerte avec une note : elle quitte la cloche et reste dans l'historique.
3. Tests : `dotnet test --filter AlertService`, Playwright `alerts.spec.ts`, `BookingsUITests` / nouvel `AlertsUITests`.

## Lot C, tendances

1. Avec 7 jours de ventes simulées, forcer un jour à ticket moyen réduit de 20 % : la carte « ticket moyen » apparaît avec −20 %.
2. Avec moins de 3 jours de données : aucune carte.

## Lot D, résumé

1. Clôturer la journée simulée ; le résumé affiche les mêmes totaux que `GET /api/fiscal/z-report/last`.

## Journée simulée

`dotnet test tests/RestaurantPos.Api.Tests --filter DaySimulation` : l'oracle donne l'espèce attendue et la liste des alertes ; rejoué par `LocalDaySimulationTests` (SC-002, SC-003).

## Critères de sortie

Suites .NET, PosKit, Playwright et UI iPad complètes ; textes FR/EN/AR cohérents ; la chaîne des reçus, des Z et le JET restent valides (`verify-chains`) ; `docs/plans/alerts-cash-progress.md` mis à jour.
