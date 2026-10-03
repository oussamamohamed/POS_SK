using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Réveille le PrintWorker dès qu'un job est mis en file (sinon scrutation toutes les 5 s).</summary>
public sealed class PrintSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(true);

    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Délai de scrutation écoulé.
        }
    }
}

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Nom imposé par le contrat de la file d'impression (persistée en base, pas une collection).")]
public sealed class PrintQueue
{
    private readonly AppDbContext _db;
    private readonly PrintSignal _signal;
    private readonly TimeProvider _time;

    public PrintQueue(AppDbContext db, PrintSignal signal, TimeProvider time)
    {
        _db = db;
        _signal = signal;
        _time = time;
    }

    public Task<PrintJob> EnqueueAsync(
        Guid printerId,
        PrintJobKind kind,
        TicketDocument document,
        bool openCashDrawer,
        CancellationToken ct = default)
        => EnqueueAsync(printerId, kind, document, openCashDrawer, null, null, ct);

    public async Task<PrintJob> EnqueueAsync(
        Guid printerId,
        PrintJobKind kind,
        TicketDocument document,
        bool openCashDrawer,
        Guid? duplicateOfDocumentId,
        int? duplicateNumber,
        CancellationToken ct = default)
    {
        var now = _time.GetUtcNow();
        var job = new PrintJob
        {
            PrinterId = printerId,
            Kind = kind,
            DocumentJson = TicketDocumentJson.Serialize(document),
            OpenCashDrawer = openCashDrawer,
            DuplicateOfDocumentId = duplicateOfDocumentId,
            DuplicateNumber = duplicateNumber,
            NextAttemptAtUtc = now,
            DeadlineAtUtc = now + PrintJob.Lifetime,
            CreatedAtUtc = now
        };
        _db.PrintJobs.Add(job);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            // Le job en échec resterait Added dans le contexte partagé et ferait échouer le prochain SaveChanges métier.
            foreach (var entry in _db.ChangeTracker.Entries<PrintJob>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
            throw;
        }
        _signal.Notify();
        return job;
    }
}
