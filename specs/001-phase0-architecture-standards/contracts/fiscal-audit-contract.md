# Contract: NF525 Fiscal Cryptographic Ledger & Audit Engine

**Feature**: `001-phase0-architecture-standards`
**Domain**: Fiscal Compliance & Audit Ledger (Norme NF525 / BOI-TVA-DECLA-30-10-30)

## 1. SHA-256 Chaining Formula Specification

Every finalized transaction computes its cryptographic signature as:
$$\text{CurrentHash} = \text{Hex}(\text{SHA256}(\text{CanonicalPayload}))$$

### Canonical Payload Construction:
$$\text{CanonicalPayload} = \text{PreviousHash} + "|" + \text{TerminalPrefix} + "-" + \text{SequentialNumber} + "|" + \text{Iso8601UtcTimestamp} + "|" + \text{TotalTtcCents} + "|" + \text{TaxSummaryString}$$

**Example**:
- `PreviousHash`: `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` (Genesis hash for first ticket)
- `Ticket`: `POS01-001042`
- `Iso8601UtcTimestamp`: `2026-08-15T20:45:00.0000000Z`
- `TotalTtcCents`: `2900` (29.00 EUR)
- `TaxSummaryString`: `[10.00:2636:264]` (Base HT 26.36 EUR, Tax 2.64 EUR)

Canonical String:
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855|POS01-001042|2026-08-15T20:45:00.0000000Z|2900|[10.00:2636:264]`

---

## 2. Technical Event Log (JET) Event Types

The JET engine captures all non-sales operational events with strict audit immutability:

| Event Code | Action Name | Required Context Data |
| :--- | :--- | :--- |
| `JET_DRAWER_OPEN` | Manual / No-Sale Drawer Kick | `OperatorId`, `ReasonCode`, `TerminalId` |
| `JET_ITEM_VOID` | Line Item Cancellation Post-Validation | `OrderId`, `ItemId`, `OriginalPriceCents`, `Reason` |
| `JET_TICKET_REPRINT` | Duplicate Receipt Printing | `ReceiptId`, `TerminalPrefix`, `SequentialNumber` |
| `JET_OPERATOR_SWITCH` | Fast Operator Sign-In/Out | `PreviousOperatorId`, `NewOperatorId`, `Timestamp` |
| `JET_Z_REPORT_CLOSE` | Fiscal Daily Closure | `FiscalDay`, `GrandTotalCents`, `ZSequenceNumber` |

---

## 3. Audit Verification Interface Contract

```csharp
public interface IFiscalAuditService
{
    ValueTask<FiscalReceiptRecord> SealReceiptAsync(
        FiscalReceiptDraft draft, 
        CancellationToken cancellationToken = default);

    ValueTask<AuditChainValidationResult> VerifyLedgerChainAsync(
        string terminalPrefix, 
        long startSequence, 
        long endSequence, 
        CancellationToken cancellationToken = default);

    ValueTask<FiscalZReport> GenerateZReportAsync(
        DateTime date, 
        Guid authorizedManagerId, 
        CancellationToken cancellationToken = default);
}
```
