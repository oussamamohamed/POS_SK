using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

/// <summary>
/// Règles d'acceptation d'un règlement, partagées par <see cref="CheckoutPaymentService"/> et par les endpoints
/// qui doivent refuser AVANT toute écriture (pourboire, numéro de retrait, avoir, commande créée à la volée).
/// </summary>
public static class PaymentSettlementRules
{
    /// <summary>Une commande réglée ou annulée n'est plus encaissable (parité avec <c>isModifiable</c> du mode autonome iPad).</summary>
    public static bool IsSettleable(OrderStatus status) =>
        status is OrderStatus.Open or OrderStatus.SentToKitchen or OrderStatus.BillRequested;

    /// <summary>
    /// Règlements acceptables : commande encaissable, chaque règlement &gt; 0 (rendu monnaie via Tendered, jamais Amount),
    /// somme ≤ reste dû (pourboire inclus) ; liste vide uniquement pour une commande à total nul avec des lignes.
    /// </summary>
    public static bool AreTendersAcceptable(IReadOnlyList<PaymentTenderRequest> tenders, OrderStatus status, bool hasItems, long totalDueCents, long previousPaidCents)
    {
        bool isFreeOrder = tenders.Count == 0 && totalDueCents == 0 && hasItems;
        bool tendersValid = isFreeOrder
            || (tenders.Count > 0 && tenders.All(t => t.AmountInCents > 0 && t.TenderedInCents >= 0));
        long remainingCents = Math.Max(0, totalDueCents - previousPaidCents);
        return IsSettleable(status) && tendersValid && tenders.Sum(t => t.AmountInCents) <= remainingCents;
    }

    /// <summary>Total déjà encaissé pour la commande : reçus non annulés (hors avoirs et reçus annulés).</summary>
    public static async Task<long> PaidCentsAsync(AppDbContext db, Guid orderId, CancellationToken ct = default)
    {
        var voidedIds = await db.FiscalReceipts
            .Where(r => r.OrderId == orderId && r.VoidedReceiptId != null)
            .Select(r => r.VoidedReceiptId!.Value)
            .ToListAsync(ct).ConfigureAwait(false);
        var receipts = await db.FiscalReceipts.Include(r => r.Tenders)
            .Where(r => r.OrderId == orderId && r.VoidedReceiptId == null && !voidedIds.Contains(r.Id))
            .ToListAsync(ct).ConfigureAwait(false);
        return receipts.SelectMany(r => r.Tenders).Sum(t => t.Amount.AmountInCents);
    }
}
