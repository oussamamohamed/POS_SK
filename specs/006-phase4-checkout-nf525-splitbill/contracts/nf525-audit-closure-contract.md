# Contract: NF525 Cryptographic Audit & Fiscal Closures

**Feature**: `006-phase4-checkout-nf525-splitbill`
**Domain**: NF525 Hash Chaining & X/Z Reporting

## 1. NF525 Audit & Closure Interface

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public record FiscalSummaryDto(
    string TerminalId,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    long TotalSalesTtcCents,
    long TotalSalesHtCents,
    IReadOnlyDictionary<decimal, long> VatBreakdownCents,
    IReadOnlyDictionary<PaymentMethod, long> PaymentTotalsCents,
    long PerpetualGrandTotalCents);

public record AuditValidationResult(
    bool IsChainValid,
    int TotalRecordsVerified,
    string? BrokenLinkReceiptNumber,
    string? ErrorDetails);

public interface INF525FiscalAuditService
{
    string ComputeReceiptHashSignature(
        string previousHash,
        string terminalId,
        long sequenceNumber,
        long amountCents,
        DateTimeOffset timestampUtc,
        string taxBreakdownJson);

    Task<FiscalSummaryDto> GenerateXReportAsync(
        string terminalId, 
        CancellationToken cancellationToken = default);

    Task<DailyFiscalClosureDto> ExecuteDailyZClosureAsync(
        string terminalId, 
        Guid managerId, 
        string managerName, 
        CancellationToken cancellationToken = default);

    Task<AuditValidationResult> ValidateAuditChainIntegrityAsync(
        string terminalId, 
        CancellationToken cancellationToken = default);
}

public record DailyFiscalClosureDto(
    Guid ClosureId,
    string TerminalId,
    long ClosureSequence,
    long TotalSalesTtcCents,
    long PerpetualGrandTotalCents,
    string SignatureHash,
    DateTimeOffset ClosedAtUtc);
```
