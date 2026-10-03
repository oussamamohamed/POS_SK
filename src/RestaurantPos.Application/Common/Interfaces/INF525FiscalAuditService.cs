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
    int ReceiptCount,
    IReadOnlyDictionary<decimal, long> VatBreakdownCents,
    IReadOnlyDictionary<PaymentMethod, long> PaymentTotalsCents,
    long PerpetualGrandTotalCents);

public record DailyFiscalClosureDto(
    Guid ClosureId,
    string TerminalId,
    long ClosureSequence,
    long TotalSalesTtcCents,
    long TotalSalesHtCents,
    int ReceiptCount,
    IReadOnlyDictionary<decimal, long> VatBreakdownCents,
    IReadOnlyDictionary<PaymentMethod, long> PaymentTotalsCents,
    long PerpetualGrandTotalCents,
    string SignatureHash,
    DateTimeOffset ClosedAtUtc,
    DateTimeOffset PeriodStartUtc,
    string SealedByUserName);

public record AuditValidationResult(
    bool IsChainValid,
    int TotalRecordsVerified,
    string? BrokenLinkReceiptNumber,
    string? ErrorDetails);

/// <summary>Commande qui empêche la clôture Z : Label = table, ou libellé du panier comptoir en attente.</summary>
public record OpenOrderDto(Guid OrderId, string Label, long RemainingTtcCents);

public class PeriodClosureException : Exception
{
    public string Code { get; }
    public IReadOnlyList<string>? Days { get; }
    public IReadOnlyList<string>? Months { get; }

    public PeriodClosureException(string code, string message, IReadOnlyList<string>? days = null, IReadOnlyList<string>? months = null)
        : base(message)
    {
        Code = code;
        Days = days;
        Months = months;
    }
}

public record PeriodClosureDto(
    Guid Id,
    string TerminalId,
    FiscalPeriodType PeriodType,
    string PeriodKey,
    long ClosureSequence,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    long TotalTtcCents,
    long TotalHtCents,
    string TaxesSummaryJson,
    string TenderTotalsJson,
    long PerpetualGrandTotalCents,
    int DailyClosureCount,
    string PreviousSignatureHash,
    string SignatureHash,
    Guid SealedByUserId,
    string SealedByUserName,
    DateTimeOffset CreatedAtUtc);

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

    Task<DailyFiscalClosureDto?> GetLatestZClosureAsync(
        string terminalId,
        CancellationToken cancellationToken = default);

    Task<AuditValidationResult> ValidateAuditChainIntegrityAsync(
        string terminalId,
        CancellationToken cancellationToken = default);

    /// <summary>Commandes à encaisser avant une clôture Z (tout le restaurant).</summary>
    Task<IReadOnlyList<OpenOrderDto>> FindOpenOrdersAsync(CancellationToken cancellationToken = default);

    /// <summary>Vrai si un reçu de ce terminal émis à cette date est couvert par une clôture Z (du terminal ou du terminal principal).</summary>
    Task<bool> IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default);

    Task<PeriodClosureDto> ExecutePeriodClosureAsync(
        string terminalId,
        FiscalPeriodType periodType,
        string periodKey,
        Guid managerId,
        string managerName,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeriodClosureDto>> GetPeriodClosuresAsync(
        string? terminalId = null,
        FiscalPeriodType? periodType = null,
        CancellationToken cancellationToken = default);
}
