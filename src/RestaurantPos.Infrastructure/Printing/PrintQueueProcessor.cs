using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

public interface IPrinterStatusNotifier
{
    Task PrinterStatusChangedAsync(Guid printerId, string printerName, bool isOnline, int pendingCount);
}

/// <summary>
/// Logique du PrintWorker. Par imprimante : jobs dus dans l'ordre de création, un à la fois ; le premier échec
/// arrête la file de cette imprimante (ordre préservé). Filtres de dates en mémoire (limite EF Core SQLite).
/// </summary>
public sealed class PrintQueueProcessor
{
    public static readonly TimeSpan PurgeAge = TimeSpan.FromDays(7);
    private readonly IServiceScopeFactory _scopes;
    private readonly IPrinterTransport _transport;
    private readonly PrinterStatusTracker _tracker;
    private readonly IPrinterStatusNotifier _notifier;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintQueueProcessor> _logger;

    public PrintQueueProcessor(IServiceScopeFactory scopes, IPrinterTransport transport, PrinterStatusTracker tracker,
        IPrinterStatusNotifier notifier, TimeProvider time, ILogger<PrintQueueProcessor> logger)
    {
        _scopes = scopes;
        _transport = transport;
        _tracker = tracker;
        _notifier = notifier;
        _time = time;
        _logger = logger;
    }

    public static TimeSpan RetryDelay(int attempts) => attempts switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(15),
        3 => TimeSpan.FromSeconds(30),
        _ => TimeSpan.FromSeconds(60)
    };

    /// <summary>Annule les jobs d'imprimantes inactives/supprimées, passe Failed les jobs échus, renvoie les imprimantes ayant un job dû.</summary>
    public async Task<IReadOnlyList<Guid>> PrepareAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Pending).ToListAsync(ct).ConfigureAwait(false);
        if (pending.Count == 0) return [];

        var now = _time.GetUtcNow();
        var printers = await db.PrinterConfigurations.AsNoTracking().ToDictionaryAsync(p => p.Id, ct).ConfigureAwait(false);
        var expiredOn = new HashSet<Guid>();
        foreach (var job in pending)
        {
            if (!printers.TryGetValue(job.PrinterId, out var printer) || !printer.IsActive)
            {
                job.Status = PrintJobStatus.Cancelled;
                job.LastError = printer is null ? "Printer deleted" : "Printer disabled";
            }
            else if (now >= job.DeadlineAtUtc)
            {
                job.Status = PrintJobStatus.Failed;
                job.LastError ??= "Deadline exceeded";
                expiredOn.Add(job.PrinterId);
            }
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        foreach (var id in expiredOn) await NotifyAsync(db, printers[id], ct).ConfigureAwait(false);

        return pending.Where(j => j.Status == PrintJobStatus.Pending && j.NextAttemptAtUtc <= now).Select(j => j.PrinterId).Distinct().ToList();
    }

    public async Task ProcessPrinterAsync(Guid printerId, CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = await db.PrinterConfigurations.AsNoTracking().FirstOrDefaultAsync(p => p.Id == printerId, ct).ConfigureAwait(false);
        if (printer is null || !printer.IsActive) return;

        var jobs = (await db.PrintJobs.Where(j => j.PrinterId == printerId && j.Status == PrintJobStatus.Pending).ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(j => j.CreatedAtUtc).ThenBy(j => j.Id).ToList();

        foreach (var job in jobs)
        {
            var now = _time.GetUtcNow();
            if (job.NextAttemptAtUtc > now) break;
            job.Attempts++;
            try
            {
                // SendAsync peut lever de façon synchrone (rendu) ou via une Task en échec : le try englobe l'appel lui-même.
                await _transport.SendAsync(printer, TicketDocumentJson.Deserialize(job.DocumentJson), job.OpenCashDrawer, ct).ConfigureAwait(false);
                job.Status = PrintJobStatus.Sent;
                job.SentAtUtc = now;
                job.LastError = null;
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (_tracker.Record(printerId, true, now)) await NotifyAsync(db, printer, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                PrintLog.SendFailed(_logger, ex, printer.Name, job.Id, job.Attempts);
                job.LastError = ex.Message;
                if (now >= job.DeadlineAtUtc) job.Status = PrintJobStatus.Failed;
                else job.NextAttemptAtUtc = now + RetryDelay(job.Attempts);
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                if (_tracker.Record(printerId, false, now) || job.Status == PrintJobStatus.Failed)
                    await NotifyAsync(db, printer, ct).ConfigureAwait(false);
                break;
            }
        }
    }

    public async Task PurgeAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var limit = _time.GetUtcNow() - PurgeAge;
        var old = (await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Sent || j.Status == PrintJobStatus.Cancelled).ToListAsync(ct).ConfigureAwait(false))
            .Where(j => j.CreatedAtUtc < limit).ToList();
        db.PrintJobs.RemoveRange(old);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task NotifyAsync(AppDbContext db, PrinterConfiguration printer, CancellationToken ct)
    {
        var pending = await db.PrintJobs.CountAsync(j => j.PrinterId == printer.Id && j.Status == PrintJobStatus.Pending, ct).ConfigureAwait(false);
        await _notifier.PrinterStatusChangedAsync(printer.Id, printer.Name, _tracker.Get(printer.Id).IsOnline != false, pending).ConfigureAwait(false);
    }
}
