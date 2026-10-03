using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Endpoints;

public static class FiscalEndpoints
{
    public static void MapFiscalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/fiscal")
                       .WithTags("Fiscal & NF525")
                       .RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/verify", async (
            IFiscalChainVerificationService verificationService,
            CancellationToken ct) =>
        {
            var result = await verificationService.VerifyAllChainsAsync(ct);
            return Results.Ok(result);
        });

        group.MapPost("/z-closure", async (ZClosureRequest req, INF525FiscalAuditService fiscal, PrintDispatcher printing) =>
        {
            var terminalId = string.IsNullOrWhiteSpace(req.TerminalId) ? "POS_MAIN_TERM" : req.TerminalId;

            if (req.ManagerId == Guid.Empty || string.IsNullOrWhiteSpace(req.ManagerName))
            {
                return Results.BadRequest(new { Message = Texts.T("errors.z_closure_manager_required") });
            }

            var open = await fiscal.FindOpenOrdersAsync();
            if (open.Count > 0)
            {
                var list = string.Join(", ", open.Select(o => $"{o.Label} ({(o.RemainingTtcCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})"));
                return Results.Json(new
                {
                    code = "open_orders",
                    message = Texts.T("errors.z_closure_open_orders", ("count", open.Count), ("orders", list)),
                    openOrders = open.Select(o => new { tableNumber = o.Label, remainingTtc = o.RemainingTtcCents / 100m })
                }, statusCode: StatusCodes.Status409Conflict);
            }

            var closure = await fiscal.ExecuteDailyZClosureAsync(terminalId, req.ManagerId, req.ManagerName);
            var printQueued = await printing.QueueZClosureAsync(closure);
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
                closure.ClosedAtUtc,
                PrintQueued = printQueued
            });
        });

        group.MapPost("/period-closures", async (
            PeriodClosureRequest req,
            ClaimsPrincipal user,
            INF525FiscalAuditService fiscal,
            PrintDispatcher printing,
            CancellationToken ct) =>
        {
            var terminalId = string.IsNullOrWhiteSpace(req.TerminalId) ? "POS_MAIN_TERM" : req.TerminalId;
            if (!System.Text.RegularExpressions.Regex.IsMatch(terminalId, "^[A-Za-z0-9_-]{1,32}$"))
            {
                return Results.BadRequest(new { code = "invalid_terminal_id", message = "Invalid terminal id." });
            }
            if (string.IsNullOrWhiteSpace(req.PeriodKey))
            {
                return Results.BadRequest(new { code = "missing_period_key", message = "Period key is required." });
            }

            FiscalPeriodType type;
            if (string.Equals(req.PeriodType, "monthly", StringComparison.OrdinalIgnoreCase))
            {
                type = FiscalPeriodType.Monthly;
            }
            else if (string.Equals(req.PeriodType, "annual", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(req.PeriodType, "yearly", StringComparison.OrdinalIgnoreCase))
            {
                type = FiscalPeriodType.Annual;
            }
            else
            {
                return Results.BadRequest(new { code = "invalid_period_type", message = "Period type must be 'monthly' or 'annual'." });
            }

            var keyPattern = type == FiscalPeriodType.Monthly ? "^(19|2[0-9])[0-9]{2}-(0[1-9]|1[0-2])$" : "^(19|2[0-9])[0-9]{2}$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(req.PeriodKey, keyPattern))
            {
                return Results.BadRequest(new { code = "invalid_period_key", message = "Invalid period key format." });
            }

            var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var name = user.FindFirst(ClaimTypes.Name)?.Value ?? user.FindFirst("name")?.Value ?? "Manager";
            if (!Guid.TryParse(sub, out var managerId))
            {
                return Results.Unauthorized();
            }

            try
            {
                var closure = await fiscal.ExecutePeriodClosureAsync(terminalId, type, req.PeriodKey, managerId, name, cancellationToken: ct);
                var printQueued = await printing.QueuePeriodClosureAsync(closure, ct);
                return Results.Ok(new
                {
                    closure.Id,
                    closure.TerminalId,
                    periodType = closure.PeriodType == FiscalPeriodType.Monthly ? "monthly" : "annual",
                    closure.PeriodKey,
                    closure.ClosureSequence,
                    closure.PeriodStartUtc,
                    closure.PeriodEndUtc,
                    TotalTtc = closure.TotalTtcCents / 100.0m,
                    TotalHt = closure.TotalHtCents / 100.0m,
                    totalTtcCents = closure.TotalTtcCents,
                    totalHtCents = closure.TotalHtCents,
                    taxesSummaryJson = closure.TaxesSummaryJson,
                    tenderTotalsJson = closure.TenderTotalsJson,
                    perpetualGrandTotal = closure.PerpetualGrandTotalCents / 100.0m,
                    perpetualGrandTotalCents = closure.PerpetualGrandTotalCents,
                    closure.DailyClosureCount,
                    closure.PreviousSignatureHash,
                    closure.SignatureHash,
                    closure.SealedByUserId,
                    closure.SealedByUserName,
                    closure.CreatedAtUtc,
                    printQueued
                });
            }
            catch (PeriodClosureException ex)
            {
                return Results.Json(new
                {
                    code = ex.Code,
                    message = ex.Message,
                    days = ex.Days,
                    months = ex.Months
                }, statusCode: StatusCodes.Status409Conflict);
            }
        });

        group.MapGet("/period-closures", async (
            string? terminalId,
            string? periodType,
            INF525FiscalAuditService fiscal,
            CancellationToken ct) =>
        {
            FiscalPeriodType? type = null;
            if (!string.IsNullOrWhiteSpace(periodType))
            {
                if (string.Equals(periodType, "monthly", StringComparison.OrdinalIgnoreCase))
                    type = FiscalPeriodType.Monthly;
                else if (string.Equals(periodType, "annual", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(periodType, "yearly", StringComparison.OrdinalIgnoreCase))
                    type = FiscalPeriodType.Annual;
            }

            var list = await fiscal.GetPeriodClosuresAsync(terminalId, type, ct);
            return Results.Ok(list.Select(c => new
            {
                c.Id,
                c.TerminalId,
                periodType = c.PeriodType == FiscalPeriodType.Monthly ? "monthly" : "annual",
                c.PeriodKey,
                c.ClosureSequence,
                c.PeriodStartUtc,
                c.PeriodEndUtc,
                TotalTtc = c.TotalTtcCents / 100.0m,
                TotalHt = c.TotalHtCents / 100.0m,
                totalTtcCents = c.TotalTtcCents,
                totalHtCents = c.TotalHtCents,
                taxesSummaryJson = c.TaxesSummaryJson,
                tenderTotalsJson = c.TenderTotalsJson,
                perpetualGrandTotal = c.PerpetualGrandTotalCents / 100.0m,
                perpetualGrandTotalCents = c.PerpetualGrandTotalCents,
                c.DailyClosureCount,
                c.PreviousSignatureHash,
                c.SignatureHash,
                c.SealedByUserId,
                c.SealedByUserName,
                c.CreatedAtUtc
            }));
        });

        group.MapGet("/latest-closure", async (string? terminalId, INF525FiscalAuditService fiscal) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var closure = await fiscal.GetLatestZClosureAsync(term);
            if (closure is null)
            {
                return Results.NotFound(new { Message = Texts.T("errors.no_closure_found") });
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

        group.MapPost("/x-report/print", async (string? terminalId, INF525FiscalAuditService fiscal, PrintDispatcher printing) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var summary = await fiscal.GenerateXReportAsync(term);
            return Results.Ok(new { PrintQueued = await printing.QueueXReportAsync(summary) });
        });

        group.MapPost("/latest-closure/print", async (
            string? terminalId,
            INF525FiscalAuditService fiscal,
            PrintDispatcher printing,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var closure = await fiscal.GetLatestZClosureAsync(term, ct);
            if (closure is null)
            {
                return Results.NotFound(new { Message = Texts.T("errors.no_closure_found") });
            }

            Guid? opId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var parsedOpId) ? parsedOpId : null;
            var (printQueued, duplicateNumber) = await printing.QueueZClosureReprintAsync(closure, opId, ct);
            return Results.Ok(new { printQueued, duplicateNumber });
        });

        group.MapGet("/fec", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? siren,
            string? company,
            IFecExportService fecService,
            IFiscalJournal fiscalJournal,
            ClaimsPrincipal user,
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

            Guid? opId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var parsedOpId) ? parsedOpId : null;
            await fiscalJournal.AppendAsync(
                JournalEventTypes.FecExported,
                new
                {
                    StartDateUtc = fromDate,
                    EndDateUtc = toDate,
                    Siren = siren,
                    Company = company,
                    FileName = result.FileName
                },
                terminalId: null,
                operatorId: opId,
                cancellationToken: ct
            );

            return Results.File(
                result.FileBytes,
                contentType: "text/plain; charset=utf-8",
                fileDownloadName: result.FileName
            );
        });

        group.MapGet("/journal", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            string? eventType,
            int? page,
            int? pageSize,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 50, 1, 200);

            var query = db.JournalEntries.AsNoTracking().AsQueryable();

            if (from.HasValue)
            {
                query = query.Where(j => j.OccurredAtUtc >= from.Value);
            }

            if (to.HasValue)
            {
                query = query.Where(j => j.OccurredAtUtc <= to.Value);
            }

            if (!string.IsNullOrWhiteSpace(eventType))
            {
                query = query.Where(j => j.EventType == eventType);
            }

            var totalCount = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(j => j.ChainSequence.HasValue)
                .ThenByDescending(j => j.ChainSequence)
                .ThenByDescending(j => j.Id) // UUIDv7 = ordre chronologique ; SQLite ne trie pas les DateTimeOffset
                .Skip((p - 1) * ps)
                .Take(ps)
                .Select(j => new
                {
                    j.Id,
                    j.EventType,
                    j.PayloadJson,
                    j.OccurredAtUtc,
                    j.TerminalId,
                    j.OperatorId,
                    j.ChainSequence,
                    j.PreviousHash,
                    HashSignature = j.EntryHash
                })
                .ToListAsync(ct);

            return Results.Ok(new
            {
                items,
                totalCount,
                page = p,
                pageSize = ps
            });
        });

        group.MapPost("/archives", async (
            CreateArchiveRequest req,
            ClaimsPrincipal user,
            IFiscalArchiveService archiveService,
            CancellationToken ct) =>
        {
            if (req.PeriodClosureId == Guid.Empty)
            {
                return Results.BadRequest(new { message = "periodClosureId is required." });
            }

            Guid? opId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var parsedOpId) ? parsedOpId : null;
            var userId = opId ?? Guid.Empty;

            try
            {
                var archive = await archiveService.CreateArchiveAsync(req.PeriodClosureId, userId, ct);
                return Results.Ok(archive);
            }
            catch (ArchiveExistsException ex)
            {
                return Results.Json(new { code = ex.Code, message = ex.Message }, statusCode: StatusCodes.Status409Conflict);
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        group.MapGet("/archives/{id:guid}/file", async (
            Guid id,
            IFiscalArchiveService archiveService,
            CancellationToken ct) =>
        {
            var file = await archiveService.GetArchiveFileAsync(id, ct);
            if (file == null)
            {
                return Results.NotFound(new { message = "Archive file not found." });
            }

            return Results.File(file.Value.Stream, file.Value.ContentType, file.Value.FileName);
        });

        group.MapPost("/archives/verify", async (
            HttpRequest request,
            IFiscalArchiveService archiveService,
            CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { message = "Multipart form expected." });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.Count > 0 ? form.Files[0] : null;
            if (file == null || file.Length == 0)
            {
                return Results.BadRequest(new { message = "No file uploaded." });
            }

            using var stream = file.OpenReadStream();
            var result = await archiveService.VerifyArchiveAsync(stream, file.FileName, ct);
            return Results.Ok(result);
        }).DisableAntiforgery();

        group.MapGet("/archives", async (
            IFiscalArchiveService archiveService,
            CancellationToken ct) =>
        {
            var archives = await archiveService.GetArchivesAsync(ct);
            return Results.Ok(archives);
        });
    }
}

public record PeriodClosureRequest(string? TerminalId, string? PeriodType, string? PeriodKey);
public record CreateArchiveRequest(Guid PeriodClosureId);

