# Modèle de données : NF525

Montants en centimes (`long` ou `Money` converti en centimes). Horodatages `DateTimeOffset` UTC. Filtres et tris sur `DateTimeOffset` en mémoire (EF Core SQLite ne les traduit pas).

## Entités existantes étendues

### `TransactionJournalEntry` (JET)

Champs existants conservés **sans renommage** : `Id`, `LocalSequence`, `TerminalId`, `OccurredAtUtc`, `IdempotencyKey`, `EventType`, `PayloadJson`, `EntryHash`.

| Ajout | Type | Règle |
|---|---|---|
| `ChainSequence` | `long?` | `NULL` pour les entrées héritées ; sinon 1, 2, 3… unique |
| `PreviousHash` | `string?` (64) | `GenesisHash` pour la première entrée chaînée |
| `OperatorId` | `Guid?` | opérateur connu, sinon `NULL` |

`EntryHash` porte désormais le hash R1 pour les entrées chaînées. Types d'événements (constantes `JournalEventTypes`) : `SERVER_STARTED`, `SERVER_STOPPED`, `LOGIN_SUCCEEDED`, `LOGIN_FAILED`, `Z_CLOSURE`, `PERIOD_CLOSURE`, `RECEIPT_VOIDED`, `DUPLICATE_PRINTED`, `FEC_EXPORTED`, `ARCHIVE_CREATED`, `ARCHIVE_EXPORTED`, `FISCAL_SETTINGS_CHANGED`, `TAX_RATE_CHANGED`, `DEVICE_PAIRED`, `DEVICE_REVOKED`, `CHAIN_BREAK_DETECTED` ; les types existants (`EVENT_HELD_ORDER_VOIDED`, `EVENT_HAPPY_HOUR_*`) passent par la même chaîne.

### `FiscalReceipt`

Aucun champ signé modifié. `IsVoid` : plus écrit ni lu (R4), colonne conservée. Index ajouté sur `VoidedReceiptId`.

### `PrintJob`

| Ajout | Type | Règle |
|---|---|---|
| `DuplicateOfDocumentId` | `Guid?` | reçu ou clôture réimprimé |
| `DuplicateNumber` | `int?` | 1, 2… par document |

### `RestaurantSettings` (singleton)

| Ajout | Type | Règle |
|---|---|---|
| `CompanyName` | `string` | obligatoire à l'impression |
| `AddressLines` | `string` | lignes séparées par `\n` |
| `Siret` | `string` | 14 chiffres |
| `VatNumber` | `string` | `FR` + 11 caractères |
| `CertificateNumber` | `string?` | facultatif (R7) |
| `FiscalYearStartMonth` | `int` | 1–12, défaut 1 |
| `FiscalYearStartDay` | `int` | 1–28, défaut 1 |

Valeurs initiales : celles de l'en-tête actuellement en dur (`TicketDocumentBuilder.cs:17`), pour ne rien changer à l'impression tant que le gérant ne les modifie pas.

## Nouvelles entités

### `FiscalPeriodClosure`

`Id` (UUIDv7), `TerminalId`, `PeriodType` (`Monthly` = 1, `Yearly` = 2), `PeriodKey` (`2026-09` / `2026`), `ClosureSequence` (par terminal et type), `PeriodStartUtc`, `PeriodEndUtc`, `TotalTtcCents`, `TotalHtCents`, `TaxesSummaryJson`, `TenderTotalsJson`, `PerpetualGrandTotalCents`, `DailyClosureCount`, `PreviousSignatureHash`, `SignatureHash`, `SealedByUserId`, `SealedByUserName`, `CreatedAtUtc`.

Unicité : `(TerminalId, PeriodType, PeriodKey)`.

### `FiscalArchive`

`Id` (UUIDv7), `PeriodClosureId`, `PeriodType`, `PeriodKey`, `FileName`, `FileSha256`, `FileSizeBytes`, `ArchiveSequence`, `PreviousSignatureHash`, `SignatureHash`, `CreatedByUserId`, `CreatedAtUtc`.

## SQL idempotent à ajouter dans `Program.cs`

Chaque instruction dans son propre `try { … } catch { }`, comme le bloc existant.

```sql
ALTER TABLE JournalEntries ADD COLUMN ChainSequence INTEGER NULL;
ALTER TABLE JournalEntries ADD COLUMN PreviousHash TEXT NULL;
ALTER TABLE JournalEntries ADD COLUMN OperatorId TEXT NULL;
CREATE UNIQUE INDEX IF NOT EXISTS IX_JournalEntries_ChainSequence ON JournalEntries (ChainSequence) WHERE ChainSequence IS NOT NULL;
CREATE INDEX IF NOT EXISTS IX_FiscalReceipts_VoidedReceiptId ON FiscalReceipts (VoidedReceiptId);
ALTER TABLE PrintJobs ADD COLUMN DuplicateOfDocumentId TEXT NULL;
ALTER TABLE PrintJobs ADD COLUMN DuplicateNumber INTEGER NULL;
ALTER TABLE RestaurantSettings ADD COLUMN CompanyName TEXT NOT NULL DEFAULT '';
-- … une instruction par colonne de RestaurantSettings, puis UPDATE initial vers l'en-tête actuel si CompanyName = ''
CREATE TABLE IF NOT EXISTS FiscalPeriodClosures ( … );
CREATE UNIQUE INDEX IF NOT EXISTS IX_FiscalPeriodClosures_Period ON FiscalPeriodClosures (TerminalId, PeriodType, PeriodKey);
CREATE TABLE IF NOT EXISTS FiscalArchives ( … );
```

Les noms exacts des tables sont à vérifier sur une base réelle (`sqlite3 restaurantpos.db .tables`) avant écriture.
