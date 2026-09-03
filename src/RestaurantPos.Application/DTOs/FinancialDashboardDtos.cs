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

public sealed record FinancialDashboardReportDto(
    FinancialKpisDto Kpis,
    List<ServiceBreakdownDto> Services,
    List<TopProductSaleDto> TopProducts,
    List<ServerProductivityDto> StaffPerformance,
    List<PaymentMethodSummaryDto> PaymentMethods,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc);
