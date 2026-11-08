# Plan d'implémentation : Alertes de pilotage et contrôle de caisse

**Branche** : `main` | **Date** : 2026-11-04 | **Spec** : [spec.md](spec.md)

## Résumé

Trois blocs de gestion, tous hors chaîne fiscale :

1. **Caisse** (histoire 1) : sessions de caisse par poste, sorties et entrées d'espèces, comptage de clôture, écart. L'espèce attendue se calcule à partir des règlements en espèces déjà portés par `PaymentTender` (montant moins le rendu).
2. **Alertes** (histoire 2) : seuils par type, évaluation à chaque événement concerné (annulation, remise, offert, clôture de caisse), alerte unique par seuil et par période, intégrée à la cloche « à traiter ».
3. **Tendances et résumé** (histoires 3 et 4) : écarts du jour calculés à la lecture à partir des encaissements existants, puis un résumé quotidien dont les totaux sont ceux du rapport Z.

Le mode serveur et le mode autonome de l'iPad reçoivent les mêmes règles ; une fixture de parité générée par le .NET fixe l'espèce attendue et les seuils.

## Contexte technique

**Langage / version** : C# .NET 10, JavaScript vanilla, Swift 6 / iPadOS 17+

**Dépendances** : aucune nouvelle.

**Stockage** : SQLite serveur (`Program.cs`, SQL idempotent) ; SQLite iPad (migration V15, ou la prochaine version libre au moment de l'implémentation, après celles de la spec 023).

**Tests** : xUnit (domaine, API), Playwright, Swift Testing, XCUITest, journée simulée étendue.

**Plateforme cible** : serveur local, web, iPad serveur, iPad autonome.

**Objectifs de performance** : évaluation d'une alerte dans la transaction de l'événement sans ralentir sensiblement l'encaissement (< 50 ms ajoutées) ; calcul du tableau de bord < 200 ms.

**Contraintes** : montants en centimes entiers ; aucune modification de `FiscalHashing`, des chaînes ni du JET existant ; données de gestion en ajout seulement pour les sessions et comptages ; textes FR/EN/AR.

**Périmètre** : une session de caisse ouverte par poste ; « service » = période entre deux clôtures Z du poste (voir D1).

## Vérification de la constitution

| Principe | Verdict | Remarque |
|---|---|---|
| I. Tactile, i18n, parité | Respecté | Pavé numérique à l'écran pour le comptage (pas de clavier système) ; en/fr/ar ; web et iPad au même lot |
| II. Couches | Respecté | `ICashSessionService`, `IAlertService`, `IInsightsService` dans Application ; implémentations dans Infrastructure |
| III. Intégrité transactionnelle | Respecté | Évaluation des alertes après le `COMMIT` métier et jamais bloquante, comme l'impression ; ouverture et clôture de caisse idempotentes |
| IV. Temps réel | Respecté | Nouvel événement `OnAlertsChanged` sur `/hubs/pos` |
| V. Monnaie et traçabilité | Respecté | Centimes entiers ; sessions, mouvements et comptages en ajout seulement (déclencheurs) ; aucune chaîne existante touchée |
| Schéma sans migrations EF | Respecté | SQL idempotent dans `Program.cs`, migration côté iPad |

Re-vérification après conception : aucun écart. Section de suivi de complexité vide.

## Structure du projet

### Documentation

```text
specs/024-alerts-cash-control/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/api.md
└── tasks.md   (étape suivante, /speckit-tasks)
```

### Code source (fichiers touchés ou créés)

```text
src/RestaurantPos.Domain/Entities/
├── CashSession.cs, CashMovement.cs, CashCount.cs, AlertRule.cs, Alert.cs   (nouveaux)
└── JournalEventTypes.cs                                                    (CASH_SESSION_OPENED, CASH_MOVEMENT, CASH_COUNTED, ALERT_RESOLVED : journal technique non fiscal)
src/RestaurantPos.Application/Common/Interfaces/
└── ICashSessionService.cs, IAlertService.cs, IInsightsService.cs
src/RestaurantPos.Infrastructure/Services/
├── CashSessionService.cs, AlertService.cs, InsightsService.cs              (nouveaux)
├── CheckoutPaymentService.cs                                               (appel de l'évaluation après annulation)
├── OrderDiscountService.cs                                                 (appel après remise ou offert)
└── FinancialDashboardService.cs                                            (réutilisé par InsightsService, non modifié)
src/RestaurantPos.Api/
├── Endpoints/CashEndpoints.cs, AlertEndpoints.cs                           (nouveaux)
├── Endpoints/DashboardEndpoints.cs                                         (insights, daily-summary)
├── Hubs/PosHub.cs                                                          (OnAlertsChanged)
└── Program.cs                                                              (DI, SQL idempotent, déclencheurs)
src/RestaurantPos.Api/wwwroot/                                              (app.js, styles.css, i18n)
ios/Packages/PosKit/Sources/PosKit/
├── Core/CashMath.swift, AlertRules.swift                                   (miroirs)
├── Models/CashModels.swift, Stores/CashStores.swift, Stores/NeedsStore.swift (type alert)
└── Local/LocalMigrator.swift (V15), LocalCashRepository.swift, LocalAlertRepository.swift, LocalPosAPI+Cash.swift
ios/RestaurantPOS/Features/Cash/                                            (écrans) ; Localizable.xcstrings
tests/ : Domain, Infrastructure (fixture cash_reference.json), Api, Web.E2ETests, DaySimulation
```

**Décision de structure** : projet existant, aucune nouvelle brique.

## Découpage en lots

| Lot | Contenu | Livrable testable seul |
|---|---|---|
| A | Sessions de caisse, mouvements, comptage, écart, historique, écran web et iPad | Ouvrir à 100 €, encaisser 50 €, compter 148 € : écart de −2 € |
| B | Seuils, alertes, cloche « à traiter », clôture avec note, événement temps réel | Alerte unique d'annulations nommant le serveur |
| C | Tendances du jour (cartes à surveiller) | Carte « ticket moyen en baisse » avec le pourcentage |
| D | Résumé quotidien aux totaux du Z, affichage, impression navigateur et partage iPad | Totaux identiques au rapport Z |
