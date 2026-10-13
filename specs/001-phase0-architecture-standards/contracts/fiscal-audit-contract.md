# Contract: NF525 Fiscal Cryptographic Ledger & Audit Engine

**Feature**: `001-phase0-architecture-standards`
**Domain**: Fiscal Compliance & Audit Ledger (Norme NF525 / BOI-TVA-DECLA-30-10-30)

## 1. SHA-256 Chaining Formula Specification

*Aligné sur `NF525FiscalAuditService.ComputeReceiptHashSignature` (2026-10-01). Le code fait foi.*

Every finalized receipt (and every Z-closure) computes its signature as:
$$\text{CurrentHash} = \text{Hex}(\text{SHA256}(\text{UTF8}(\text{CanonicalPayload})))$$

### Canonical Payload Construction:
`{PreviousHash}|{TerminalId}|{SequenceNumber}|{AmountCents}|{TimestampUtc:O}|{TaxBreakdownJson}`

- `PreviousHash`: `SignatureHash` of the previous receipt of the same terminal; `GenesisHash` = `GENESIS_` + 64 zeros for the first one.
- `TimestampUtc:O`: round-trip ISO 8601 (e.g. `2026-08-15T20:45:00.0000000+00:00`).
- `TaxBreakdownJson`: the exact string stored in `FiscalReceipt.TaxBreakdownJson`; its serialization MUST NOT change.
- Z-closures use the same function, chained to the previous closure (`PreviousSignatureHash`).

Changing field order, formatting or serialization breaks verification of existing chains.

---

## 2. Technical Event Log (JET)

*Aligné sur `docs/fiscal.md` §2.3 (2026-10-13).* Le JET est la table `TechnicalJournalEntry`, chaîne unique pour tout le restaurant:

`HEX_lower(SHA256("{previousHash}|{chainSequence}|{eventType}|{occurredAtUtc:O}|{terminalId}|{operatorId}|{payloadJson}"))` (`FiscalHashing.ComputeJetHash`).

Événements: `Z_CLOSURE`, `PERIOD_CLOSURE`, `RECEIPT_VOIDED`, `DUPLICATE_PRINTED`, `DEVICE_PAIRED`, `DEVICE_REVOKED`, `FISCAL_SETTINGS_CHANGED`, `FEC_EXPORTED`, `ARCHIVE_CREATED`, `ARCHIVE_EXPORTED`. Les anciennes entrées de `TransactionJournalEntry` (`EVENT_*`) restent hors chaîne.

*Cible non implémentée*: `JET_DRAWER_OPEN` (ouverture de tiroir sans vente), `JET_ITEM_VOID` (annulation de ligne après validation), `JET_OPERATOR_SWITCH`.

---

## 3. Audit Verification Interface Contract

*Interfaces réelles* (`RestaurantPos.Application.Common.Interfaces`):

- `INF525FiscalAuditService`: `ComputeReceiptHashSignature`, `GenerateXReportAsync`, `ExecuteDailyZClosureAsync`, `GetLatestZClosureAsync`, `ValidateAuditChainIntegrityAsync` (reçus), `FindOpenOrdersAsync`, `IsInClosedPeriodAsync`, `ExecutePeriodClosureAsync`, `GetPeriodClosuresAsync`.
- `IFiscalChainVerificationService.VerifyAllChainsAsync`: recalcule les cinq chaînes (reçus, Z, JET, clôtures de période, archives) ; `POST /api/fiscal/verify-chains`.

Les reçus sont scellés dans `CheckoutPaymentService`. Formules des cinq chaînes: `docs/fiscal.md` §2 et `FiscalHashing`.
