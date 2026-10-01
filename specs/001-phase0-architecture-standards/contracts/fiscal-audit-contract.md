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

*État actuel*: le JET est la table `TransactionJournalEntry` (`TerminalId`, `EventType`, `PayloadJson`, `IdempotencyKey`, `EntryHash`). `EntryHash` n'est pas un hash chaîné (préfixe `JET_` + GUID). Événements journalisés: `EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED`, `EVENT_HAPPY_HOUR_OVERRIDE_STOPPED`, `EVENT_HELD_ORDER_VOIDED`, ainsi que les évènements de synchronisation.

*Cible (non implémentée)*:

| Event Code | Action Name | Required Context Data |
| :--- | :--- | :--- |
| `JET_DRAWER_OPEN` | Manual / No-Sale Drawer Kick | `OperatorId`, `ReasonCode`, `TerminalId` |
| `JET_ITEM_VOID` | Line Item Cancellation Post-Validation | `OrderId`, `ItemId`, `OriginalPriceCents`, `Reason` |
| `JET_TICKET_REPRINT` | Duplicate Receipt Printing | `ReceiptId`, `TerminalId`, `SequenceNumber` |
| `JET_OPERATOR_SWITCH` | Fast Operator Sign-In/Out | `PreviousOperatorId`, `NewOperatorId`, `Timestamp` |
| `JET_Z_REPORT_CLOSE` | Fiscal Daily Closure | `FiscalDay`, `GrandTotalCents`, `ZSequenceNumber` |

---

## 3. Audit Verification Interface Contract

*Interface réelle*: `RestaurantPos.Application.Common.Interfaces.INF525FiscalAuditService`.

```csharp
public interface INF525FiscalAuditService
{
    string ComputeReceiptHashSignature(string previousHash, string terminalId, long sequenceNumber,
        long amountCents, DateTimeOffset timestampUtc, string taxBreakdownJson);

    Task<FiscalSummaryDto> GenerateXReportAsync(string terminalId, CancellationToken ct = default);

    Task<DailyFiscalClosureDto> ExecuteDailyZClosureAsync(string terminalId, Guid managerId,
        string managerName, CancellationToken ct = default);

    Task<DailyFiscalClosureDto?> GetLatestZClosureAsync(string terminalId, CancellationToken ct = default);

    Task<AuditValidationResult> ValidateAuditChainIntegrityAsync(string terminalId, CancellationToken ct = default);

    Task<IReadOnlyList<OpenOrderDto>> FindOpenOrdersAsync(CancellationToken ct = default);

    Task<bool> IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken ct = default);
}
```

Les reçus sont scellés dans `CheckoutPaymentService` (pas de `SealReceiptAsync` dédié). La vérification porte sur tout le terminal (pas de plage de séquences).
