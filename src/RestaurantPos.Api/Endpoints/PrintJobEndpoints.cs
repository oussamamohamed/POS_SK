using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Endpoints;

public record PrinterStatusDto(Guid PrinterId, string Name, bool IsActive, bool? IsOnline, DateTimeOffset? SinceUtc, int PendingCount, int FailedCount);
public record PrintJobDto(Guid Id, Guid PrinterId, string Kind, string Status, int Attempts, DateTimeOffset CreatedAtUtc, DateTimeOffset? SentAtUtc, string? LastError, int? DuplicateNumber = null, Guid? DuplicateOfDocumentId = null);

public static class PrintJobEndpoints
{
    private static readonly PrintJobStatus[] OpenStatuses = [PrintJobStatus.Pending, PrintJobStatus.Failed];

    public static void MapPrintJobEndpoints(this IEndpointRouteBuilder app)
    {
        var printers = app.MapGroup("/api/printers").WithTags("Printers").RequireAuthorization();

        printers.MapGet("/status", async (AppDbContext db, PrinterStatusTracker tracker) =>
        {
            var list = await db.PrinterConfigurations.AsNoTracking().OrderBy(p => p.Name).ToListAsync();
            var open = await db.PrintJobs.AsNoTracking()
                .Where(j => j.Status == PrintJobStatus.Pending || j.Status == PrintJobStatus.Failed)
                .Select(j => new { j.PrinterId, j.Status }).ToListAsync();
            return Results.Ok(list.Select(p =>
            {
                var state = tracker.Get(p.Id);
                return new PrinterStatusDto(p.Id, p.Name, p.IsActive, state.IsOnline, state.SinceUtc,
                    open.Count(j => j.PrinterId == p.Id && j.Status == PrintJobStatus.Pending),
                    open.Count(j => j.PrinterId == p.Id && j.Status == PrintJobStatus.Failed));
            }));
        });

        printers.MapGet("/{id:guid}/jobs", async (Guid id, string? status, AppDbContext db) =>
        {
            var wanted = string.IsNullOrWhiteSpace(status)
                ? OpenStatuses
                : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => Enum.TryParse<PrintJobStatus>(s, ignoreCase: true, out var st) ? st : (PrintJobStatus?)null)
                    .OfType<PrintJobStatus>().ToArray();
            var jobs = (await db.PrintJobs.AsNoTracking().Where(j => j.PrinterId == id && wanted.Contains(j.Status)).ToListAsync())
                .OrderByDescending(j => j.CreatedAtUtc).Take(100).Select(ToDto);
            return Results.Ok(jobs);
        }).RequireAuthorization("RequireManagerOrAdmin");

        var jobsGroup = app.MapGroup("/api/print-jobs").WithTags("Printers").RequireAuthorization("RequireManagerOrAdmin");

        jobsGroup.MapPost("/{id:guid}/retry", async (
            Guid id,
            AppDbContext db,
            PrintQueue queue,
            PrintSignal signal,
            TimeProvider time,
            IFiscalJournal fiscalJournal,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            var job = await db.PrintJobs.FindAsync([id], ct);
            if (job is null) return Results.NotFound(new { Message = Texts.T("errors.print_job_not_found") });
            if (job.Status != PrintJobStatus.Failed && job.Status != PrintJobStatus.Sent)
                return Results.Conflict(new { Message = Texts.T("errors.print_job_not_retryable") });
            // Duplicata fiscal : uniquement reçus et rapports (pas tickets cuisine ni bons de retrait).
            if (job.Status == PrintJobStatus.Sent && job.Kind is not (PrintJobKind.Receipt or PrintJobKind.Report))
                return Results.Conflict(new { Message = Texts.T("errors.print_job_not_retryable") });

            if (job.Status == PrintJobStatus.Failed)
            {
                var now = time.GetUtcNow();
                job.Status = PrintJobStatus.Pending;
                job.Attempts = 0;
                job.NextAttemptAtUtc = now;
                job.DeadlineAtUtc = now + PrintJob.Lifetime;
                job.LastError = null;
                await db.SaveChangesAsync(ct);
                signal.Notify();
                return Results.Ok(ToDto(job));
            }

            // job.Status == PrintJobStatus.Sent: creates duplicate print job
            Guid? opId = Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var parsedOpId) ? parsedOpId : null;
            var docId = job.DuplicateOfDocumentId ?? job.Id;

            TicketDocument original;
            try
            {
                original = TicketDocumentJson.Deserialize(job.DocumentJson);
            }
            catch (System.Text.Json.JsonException)
            {
                // Document illisible : pas de duplicata vide présenté comme un succès.
                return Results.Conflict(new { Message = Texts.T("errors.print_job_not_retryable") });
            }

            var newJob = await db.ExecuteInTransactionAsync(async txCt =>
            {
                var duplicateNumber = await db.NextAsync(docId, txCt);

                var doc = TicketDocumentBuilder.WithDuplicateNotice(original, duplicateNumber);

                var createdJob = await queue.EnqueueAsync(
                    job.PrinterId,
                    job.Kind,
                    doc,
                    false, // un duplicata n'ouvre jamais le tiroir-caisse
                    docId,
                    duplicateNumber,
                    txCt);

                await fiscalJournal.AppendAsync(
                    JournalEventTypes.DuplicatePrinted,
                    new
                    {
                        documentId = docId,
                        documentType = "print_job",
                        originalJobId = job.Id,
                        duplicateNumber,
                        printerId = job.PrinterId
                    },
                    terminalId: null,
                    operatorId: opId,
                    cancellationToken: txCt);

                return createdJob;
            }, System.Data.IsolationLevel.Serializable, ct);

            return Results.Ok(ToDto(newJob));
        });

        jobsGroup.MapPost("/{id:guid}/cancel", async (Guid id, AppDbContext db) =>
        {
            var job = await db.PrintJobs.FindAsync(id);
            if (job is null) return Results.NotFound(new { Message = Texts.T("errors.print_job_not_found") });
            if (!OpenStatuses.Contains(job.Status)) return Results.Conflict(new { Message = Texts.T("errors.print_job_not_cancellable") });
            job.Status = PrintJobStatus.Cancelled;
            await db.SaveChangesAsync();
            return Results.Ok(ToDto(job));
        });
    }

    private static PrintJobDto ToDto(PrintJob j) =>
        new(j.Id, j.PrinterId, j.Kind.ToString(), j.Status.ToString(), j.Attempts, j.CreatedAtUtc, j.SentAtUtc, j.LastError, j.DuplicateNumber, j.DuplicateOfDocumentId);
}
