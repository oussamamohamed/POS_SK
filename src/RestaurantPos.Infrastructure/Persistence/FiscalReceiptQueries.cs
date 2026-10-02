using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Infrastructure.Persistence;

public static class FiscalReceiptQueries
{
    /// <summary>
    /// Récupère l'ensemble des identifiants des reçus qui ont été annulés par un avoir.
    /// </summary>
    public static async Task<HashSet<Guid>> GetVoidedReceiptIdsAsync(
        IQueryable<FiscalReceipt> receiptsQuery,
        CancellationToken cancellationToken = default)
    {
        var ids = await receiptsQuery
            .Where(r => r.VoidedReceiptId != null)
            .Select(r => r.VoidedReceiptId!.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return [.. ids];
    }

    /// <summary>
    /// Vérifie si un reçu est actif (ni un avoir, ni annulé par un avoir présent dans le jeu d'identifiants).
    /// </summary>
    public static bool IsActiveReceipt(this FiscalReceipt receipt, HashSet<Guid> voidedReceiptIds)
    {
        return receipt.VoidedReceiptId == null && !voidedReceiptIds.Contains(receipt.Id);
    }
}
