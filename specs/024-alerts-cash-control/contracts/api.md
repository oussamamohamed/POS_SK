# Contrats : Alertes de pilotage et contrôle de caisse

Montants en centimes. Les routes d'écriture de caisse exigent `X-Device-Token`. Les routes de gestion sont derrière `RequireManagerOrAdmin`.

## Caisse

- `POST /api/cash/sessions` : `{ "openingFloatCents": 10000 }` → 201 `{ id, terminalId, openedAtUtc, openingFloatCents }`. 409 `{"code":"cash_session_already_open"}` si une session est déjà ouverte sur ce poste.
- `GET /api/cash/sessions/current` : session ouverte du poste avec `expectedCents` en direct, ou 204. Lisible par l'opérateur qui l'a ouverte et par le responsable.
- `POST /api/cash/sessions/{id}/movements` : `{ "kind": "out", "amountCents": 1200, "reason": "Achat de glace" }` → 201.
- `POST /api/cash/sessions/{id}/count` : `{ "countedCents": 14800, "denominations": [{ "valueCents": 2000, "quantity": 5 }], "varianceNote": "…" }` → 200 `{ expectedCents, countedCents, varianceCents, alertCreated }`. 400 `{"code":"variance_note_required"}` si l'écart dépasse le seuil actif sans note. Rejouer le même comptage renvoie le même résultat (idempotent).
- `GET /api/cash/sessions?from=&to=` (responsable) : historique avec écarts.

## Alertes

- `GET /api/alerts?state=open|resolved&from=&to=` (responsable) : `[{ id, ruleType, operatorId, operatorName, terminalId, periodKey, detail, state, createdAtUtc }]`. Le détail contient le nombre, le montant et les identifiants de tickets ou de commandes.
- `POST /api/alerts/{id}/resolve` : `{ "note": "…" }` → 200.
- `GET /api/alerts/rules` et `PUT /api/alerts/rules/{type}` : `{ "threshold": 3, "enabled": true }`.
- Temps réel : `/hubs/pos` `OnAlertsChanged()`. Le nom ne doit pas être renommé.

## Tendances et résumé

- `GET /api/dashboard/insights?tzOffsetMinutes=` (responsable) : `{ "cards": [{ "metric": "avgTicket", "currentCents": 3120, "referenceCents": 3900, "deltaPercent": -20.0 }], "insufficientData": false }`. Les métriques valent `cumulativeTtc`, `avgTicket`, `perCover` ou `takeawayShare`.
- `GET /api/dashboard/daily-summary?terminalId=&date=` (responsable) : `{ totals (identiques au Z), tipsByServer, cash {floatCents, countedCents, varianceCents}, openAlerts }`.
- `GET /api/fiscal/z-report/last` : gagne les champs facultatifs `cashFloat`, `cashCounted`, `cashVariance` (lecture seule, non signés).

## Réglages

`GET/PUT /api/settings` gagnent `trendThresholdPercent` et `cashVarianceNoteRequired`.

## Fixtures capturées

`cash_session.json`, `alerts.json`, `insights.json`, `daily_summary.json` (capturées) et `cash_reference.json` (générée, `WRITE_CASH_FIXTURE=1`) dans `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/`.
