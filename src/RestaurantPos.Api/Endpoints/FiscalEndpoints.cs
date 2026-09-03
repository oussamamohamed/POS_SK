using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Endpoints;

public static class FiscalEndpoints
{
    public static void MapFiscalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fiscal")
                       .WithTags("Fiscal & NF525")
                       .RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/z-closure", async (ZClosureRequest req, INF525FiscalAuditService fiscal) =>
        {
            var terminalId = string.IsNullOrWhiteSpace(req.TerminalId) ? "POS_MAIN_TERM" : req.TerminalId;

            if (req.ManagerId == Guid.Empty || string.IsNullOrWhiteSpace(req.ManagerName))
            {
                return Results.BadRequest(new { Message = "ManagerId et ManagerName sont obligatoires pour la clôture Z." });
            }

            var closure = await fiscal.ExecuteDailyZClosureAsync(terminalId, req.ManagerId, req.ManagerName);
            return Results.Ok(new
            {
                closure.ClosureSequence,
                TotalSalesTtc = closure.TotalSalesTtcCents / 100.0m,
                TotalSalesHt = closure.TotalSalesHtCents / 100.0m,
                closure.ReceiptCount,
                VatBreakdown = closure.VatBreakdownCents.ToDictionary(k => k.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), v => v.Value / 100.0m),
                PaymentTotals = closure.PaymentTotalsCents.ToDictionary(k => k.Key.ToString(), v => v.Value / 100.0m),
                PerpetualGrandTotal = closure.PerpetualGrandTotalCents / 100.0m,
                closure.SignatureHash,
                closure.ClosedAtUtc
            });
        });

        group.MapGet("/latest-closure", async (string? terminalId, INF525FiscalAuditService fiscal) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var closure = await fiscal.GetLatestZClosureAsync(term);
            if (closure is null)
            {
                return Results.NotFound(new { Message = "Aucune clôture trouvée." });
            }

            return Results.Ok(new
            {
                closure.ClosureSequence,
                TotalSalesTtc = closure.TotalSalesTtcCents / 100.0m,
                TotalSalesHt = closure.TotalSalesHtCents / 100.0m,
                closure.ReceiptCount,
                VatBreakdown = closure.VatBreakdownCents.ToDictionary(k => k.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), v => v.Value / 100.0m),
                PaymentTotals = closure.PaymentTotalsCents.ToDictionary(k => k.Key.ToString(), v => v.Value / 100.0m),
                PerpetualGrandTotal = closure.PerpetualGrandTotalCents / 100.0m,
                closure.SignatureHash,
                closure.ClosedAtUtc
            });
        });

        group.MapGet("/x-report", async (string? terminalId, INF525FiscalAuditService fiscal) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var summary = await fiscal.GenerateXReportAsync(term);
            return Results.Ok(new
            {
                summary.TerminalId,
                TotalSalesTtc = summary.TotalSalesTtcCents / 100.0m,
                TotalSalesHt = summary.TotalSalesHtCents / 100.0m,
                summary.ReceiptCount,
                VatBreakdown = summary.VatBreakdownCents.ToDictionary(k => k.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), v => v.Value / 100.0m),
                PaymentTotals = summary.PaymentTotalsCents.ToDictionary(k => k.Key.ToString(), v => v.Value / 100.0m),
                PerpetualGrandTotal = summary.PerpetualGrandTotalCents / 100.0m,
                summary.PeriodStartUtc,
                summary.PeriodEndUtc
            });
        });

        group.MapGet("/fec", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? siren,
            string? company,
            IFecExportService fecService,
            CancellationToken ct) =>
        {
            var fromDate = from ?? DateTimeOffset.UtcNow.Date.AddDays(-30);
            var toDate = to ?? DateTimeOffset.UtcNow;

            var result = await fecService.GenerateFecAsync(new FecExportRequest(
                StartDateUtc: fromDate,
                EndDateUtc: toDate,
                SirenNumber: siren,
                CompanyName: company
            ), ct);

            return Results.File(
                result.FileBytes,
                contentType: "text/plain; charset=utf-8",
                fileDownloadName: result.FileName
            );
        });
    }
}
