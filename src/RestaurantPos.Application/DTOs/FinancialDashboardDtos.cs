using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.DTOs;

public sealed record FinancialDashboardFilterDto(
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc);

public sealed record FinancialKpisDto(
    decimal TotalSalesTtc,
    decimal TotalSalesHt,
    decimal AverageOrderTtc,
    decimal AverageCoverTtc,
    int TotalOrdersCount,
    int TotalCoversCount);

public sealed record ServiceBreakdownDto(
    string ServiceName,
    decimal SalesTtc,
    decimal SalesHt,
    int OrdersCount,
    int CoversCount,
    decimal AverageCoverTtc);

public sealed record TopProductSaleDto(
    Guid ProductId,
    string ProductName,
    int QuantitySold,
    decimal TotalSalesTtc,
    decimal PercentageOfTotal);

public sealed record ServerProductivityDto(
    Guid? ServerId,
    string ServerName,
    int TablesServedCount,
    decimal TotalSalesTtc,
    decimal AverageTableTtc);

public sealed record PaymentMethodSummaryDto(
    PaymentMethod Method,
    string MethodName,
    decimal TotalAmount,
    int TransactionsCount,
    decimal PercentageOfTotal);

/// <summary>Un point de la courbe : une heure locale (période d'un jour) ou un jour (période plus longue).</summary>
public sealed record TakingsPointDto(string Label, decimal Current, decimal Compare);

public sealed record CategoryTakingDto(string Name, int Quantity, decimal SalesTtc, decimal Share);

public sealed record VatLineDto(decimal RatePercent, decimal BaseHt, decimal Vat);

public sealed record ServerTipDto(string ServerName, decimal TipsTtc);

/// <summary>
/// Encaissements d'une période [FromUtc, ToUtc) comparés à une période de référence (même jour de la semaine précédente
/// ou période juste avant, de même durée). Série par heure locale pour un jour, par jour au-delà.
/// </summary>
public sealed record TakingsReportDto(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    string Granularity,
    string Compare,
    decimal TotalTtc,
    decimal CompareTotalTtc,
    decimal CompareByNowTtc,
    int Covers,
    int TablesTurned,
    decimal SpendPerCover,
    List<TakingsPointDto> Series,
    List<PaymentMethodSummaryDto> PaymentMethods,
    List<ServerProductivityDto> StaffPerformance,
    decimal TipsTotalTtc,
    List<ServerTipDto> TipsByServer,
    decimal DiscountsTtc,
    decimal CompsTtc,
    int VoidsCount,
    decimal VoidsTtc,
    List<CategoryTakingDto> Categories,
    List<TopProductSaleDto> TopProducts,
    List<VatLineDto> Vat);

/// <summary>Un reçu de la période. <c>IsCredit</c> : avoir (annulation) ; <c>IsCancelled</c> : reçu d'origine annulé par un avoir.</summary>
public sealed record TakingsReceiptDto(
    Guid Id,
    string ReceiptNumber,
    DateTimeOffset AtUtc,
    string? TableNumber,
    string ServerName,
    List<string> Methods,
    decimal TotalTtc,
    bool IsCredit,
    bool IsCancelled);

/// <summary>Une écriture du journal technique (JET) du jour ; <c>PayloadJson</c> est affiché tel quel par les clients.</summary>
public sealed record JournalEntryDto(
    Guid Id,
    DateTimeOffset AtUtc,
    string EventType,
    string? TerminalId,
    string? OperatorName,
    string PayloadJson);

public sealed record FinancialDashboardReportDto(
    FinancialKpisDto Kpis,
    List<ServiceBreakdownDto> Services,
    List<TopProductSaleDto> TopProducts,
    List<ServerProductivityDto> StaffPerformance,
    List<PaymentMethodSummaryDto> PaymentMethods,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc);
