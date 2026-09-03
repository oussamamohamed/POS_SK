using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

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

public record DailyFiscalClosureDto(
    Guid ClosureId,
    string TerminalId,
    long ClosureSequence,
    long TotalSalesTtcCents,
    long PerpetualGrandTotalCents,
    string SignatureHash,
    DateTimeOffset ClosedAtUtc);

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
