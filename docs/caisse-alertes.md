# Caisse, alertes et tendances (spec 024)

Contrôle de caisse (fond, mouvements, comptage, écart), alertes de pilotage, tendances « à surveiller » et résumé de fin de journée. Spec : `specs/024-alerts-cash-control/`.

## Caisse

- Tables `CashSessions`, `CashMovements`, `CashCounts` en **ajout seul** (triggers SQL `cash_append_only`, `cash_session_already_open`) ; une seule session ouverte par poste.
- **Attendu** = fond + Σ `Amount` des règlements espèces depuis `OpenedAtUtc` (net du rendu ; un avoir est négatif) + entrées − sorties. **Écart** = compté − attendu, figé dans `CashCount`. Le comptage est idempotent.
- Note obligatoire si |écart| dépasse le seuil actif de la règle `cash_variance` et que le réglage `CashVarianceNoteRequired` est vrai.
- Chaque étape écrit une ligne dans le JET (`CASH_SESSION_OPENED`, `CASH_MOVEMENT`, `CASH_COUNTED`) avec la même chaîne ; les chaînes fiscales (reçus, Z) ne sont pas modifiées. Le Z (`/api/fiscal/latest-closure`) porte le comptage dans des champs optionnels non signés.

## Alertes

- Règles : `cash_variance`, `voids_count`, `discount_percent`, `comps_total` (désactivées par défaut ; « dépasse » = strictement supérieur).
- `PeriodKey = terminalId:dernierZ`, `DedupeKey = règle|opérateur|période` (index unique) : une seule alerte par règle, opérateur et période.
- Évaluation par `AlertEvaluationFilter` après encaissement, vente comptoir et synchro (les erreurs sont avalées : le règlement n'échoue jamais) et après un comptage.
- Résolution : note obligatoire, état `open` → `resolved`. Diffusion temps réel `OnAlertsChanged` sur `/hubs/pos`.
- Réglages (seuil des tendances, note obligatoire) : `/api/alerts/settings`, pas `/api/settings` (signé au JET).

## Tendances et résumé

- `GET /api/dashboard/insights` : jour local comparé au même jour de la semaine précédente, à la même heure. Aucune carte sous 3 jours de données (`insufficientData`). Cartes affichées si |delta| ≥ `TrendThresholdPercent` (15 % par défaut). Métriques en centimes (`takeawayShare` en centièmes de pour cent). `DeltaPercent` arrondi à 0,1, demi éloigné de zéro.
- `GET /api/dashboard/daily-summary` : totaux identiques au dernier Z du poste, pourboires par serveur (commandes de la période), comptage de caisse, alertes ouvertes. Aucune écriture fiscale. 404 `no_closure` sans Z.

## Parité .NET / Swift

- iPad : migration locale **V16** (`LocalSchemaV16`), `LocalCashRepository`, `LocalAlertRepository`, `LocalPosAPI+Cash`, `LocalPosAPI+Insights`.
- Fixtures générées par le .NET : `cash_reference.json` (`WRITE_CASH_FIXTURE=1`), `insights_reference.json` (`WRITE_INSIGHTS_FIXTURE=1`). Fixtures `cash_session.json`, `alerts.json` capturées ; `insights.json`, `daily_summary.json` écrites à la main d'après les DTO (à recapturer si les DTO changent).
- Journée scriptée avec oracle commun : `CashDaySimulationTests` (.NET) et `LocalCashDayTests` (iPad).

## Rapports imprimés

À régénérer si les blocs changent : `ticket_document_reference.json` (`TZ=Europe/Paris WRITE_TICKET_FIXTURE=1`) et `report_print_data_reference.json` (`WRITE_REPORT_PRINT_FIXTURE=1`).

## Écarts par rapport au plan

- T003 : l'attendu utilise `Amount` net du rendu (formule ci-dessus).
- T022 : les rapports imprimés (X, Z, clôtures de période) portent deux blocs informatifs, hors totaux et signature : « ACTIVITÉ » (remises, offerts, annulations/avoirs de la période, omis si tout est à zéro) et « CAISSE » (fond, attendu, compté, écart du dernier comptage de la période, omis sans comptage). Source : `ReportPrintData.Activity` / `.Cash` (.NET) et `ReportActivity` / `ReportCash` (Swift).
- Évaluation par filtre d'endpoint plutôt que dans les services de paiement (aucun changement des services fiscaux).
- Les annulations « cumulées sur la semaine » ne sont pas couvertes : la période est celle du dernier Z.
