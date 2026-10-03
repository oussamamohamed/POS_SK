# Contrats d'API : NF525

Toutes les routes sont sous le groupe existant `/api/fiscal` (`RequireManagerOrAdmin`), sauf mention contraire. Montants en centimes, dates en ISO 8601 UTC. Erreurs au format existant `{ code, message }` (message localisé via `Texts.T`).

## Vérification (US1)

### `POST /api/fiscal/verify`

Synchrone. Écrit `CHAIN_BREAK_DETECTED` dans le JET si une chaîne est rompue.

```json
{
  "isValid": false,
  "checkedAtUtc": "2026-09-30T21:00:00Z",
  "chains": [
    { "chain": "receipts", "terminalId": "T01", "checkedCount": 10542, "isValid": true, "break": null },
    { "chain": "receipts", "terminalId": "POS_MAIN_TERM", "checkedCount": 812, "isValid": false,
      "break": { "sequenceNumber": 42, "reference": "POS_MAIN_TERM-000042", "kind": "signature_mismatch" } },
    { "chain": "z_closures", "terminalId": "T01", "checkedCount": 180, "isValid": true, "break": null },
    { "chain": "period_closures", "terminalId": "T01", "checkedCount": 6, "isValid": true, "break": null },
    { "chain": "journal", "terminalId": null, "checkedCount": 5120, "legacyCount": 37, "isValid": true, "break": null },
    { "chain": "archives", "terminalId": null, "checkedCount": 1, "isValid": true, "break": null }
  ]
}
```

`kind` : `previous_hash_mismatch` | `signature_mismatch` | `sequence_gap`.

## Clôtures de période (US4)

### `POST /api/fiscal/period-closures`

```json
{ "terminalId": "POS_MAIN_TERM", "periodType": "monthly", "periodKey": "2026-09" }
```

Le serveur calcule les bornes depuis `periodKey` et l'exercice paramétré ; le client ne fournit pas de dates.

- `200` : la clôture (champs de `FiscalPeriodClosure`, `printQueued`).
- `409 {code:"period_not_ended"}` · `409 {code:"period_already_closed"}` · `409 {code:"missing_daily_closures", days:["2026-09-12"]}` · `409 {code:"missing_monthly_closures", months:["2026-03"]}`.

### `GET /api/fiscal/period-closures?terminalId=…&periodType=…`

Liste, la plus récente d'abord.

## Archives (US5)

### `POST /api/fiscal/archives`

`{ "periodClosureId": "…" }` → `200` `{ id, fileName, fileSha256, fileSizeBytes, signatureHash }` · `409 {code:"archive_exists"}`.

### `GET /api/fiscal/archives/{id}/file`

Téléchargement du ZIP. Écrit une entrée JET `ARCHIVE_EXPORTED`.

### `POST /api/fiscal/archives/verify`

Multipart : fichier ZIP. `200` `{ isValid, archiveId, reason }` (`reason` : `unknown_archive` | `hash_mismatch` | `chain_break`).

## Duplicatas (US6)

### `POST /api/checkout/receipts/{receiptId}/reprint`

Opérateur authentifié. `200` `{ printQueued, duplicateNumber }` · `404` reçu inconnu.

### Existant modifié

- `POST /api/fiscal/latest-closure/print` : ajoute `duplicateNumber` à la réponse ; imprime « DUPLICATA n°N ».
- `POST /api/print-jobs/{id}/retry` : si le job était `Done`, crée un duplicata (réponse `+ duplicateNumber`).

## Identité et exercice (US7)

### `GET/PUT /api/settings` (existant, étendu)

Champs ajoutés : `companyName`, `addressLines`, `siret`, `vatNumber`, `certificateNumber`, `fiscalYearStartMonth`, `fiscalYearStartDay`. `PUT` sans un champ → valeur conservée. `400` si SIRET, TVA ou date invalides ; `409 {code:"fiscal_year_locked"}` si une clôture annuelle existe et que l'exercice change.

## Journal (US2)

### `GET /api/fiscal/journal?from=…&to=…&eventType=…`

Lecture paginée (`page`, `pageSize` ≤ 500), la plus récente d'abord. Aucune route d'écriture ou de suppression.
