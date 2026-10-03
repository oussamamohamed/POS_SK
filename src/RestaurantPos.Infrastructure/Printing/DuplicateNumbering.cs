using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

public static class DuplicateNumbering
{
    /// <summary>
    /// Prochain n° de duplicata d'un document. Le journal fiscal (JET) fait foi : il compte aussi les
    /// duplicata sans impression (aucune imprimante). Les jobs gardent la numérotation d'avant le journal.
    /// </summary>
    public static async Task<int> NextAsync(this AppDbContext db, Guid documentId, CancellationToken ct)
    {
        var fromJobs = await db.PrintJobs
            .Where(j => j.DuplicateOfDocumentId == documentId)
            .MaxAsync(j => (int?)j.DuplicateNumber, ct)
            .ConfigureAwait(false) ?? 0;
        var id = documentId.ToString();
        var fromJournal = await db.JournalEntries
            .CountAsync(e => e.EventType == JournalEventTypes.DuplicatePrinted && e.PayloadJson.Contains(id), ct)
            .ConfigureAwait(false);
        return Math.Max(fromJobs, fromJournal) + 1;
    }
}
