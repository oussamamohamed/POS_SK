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
            if (string.IsNullOrWhiteSpace(req.TerminalId))
            {
                return Results.BadRequest(new { Message = "TerminalId est obligatoire pour la clôture Z." });
            }

            if (req.ManagerId == Guid.Empty || string.IsNullOrWhiteSpace(req.ManagerName))
            {
                return Results.BadRequest(new { Message = "ManagerId et ManagerName sont obligatoires pour la clôture Z." });
            }

            var closure = await fiscal.ExecuteDailyZClosureAsync(req.TerminalId, req.ManagerId, req.ManagerName);
            return Results.Ok(new
            {
                closure.ClosureSequence,
                TotalSalesTtc = closure.TotalSalesTtcCents / 100.0m,
                closure.SignatureHash,
                closure.ClosedAtUtc
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
