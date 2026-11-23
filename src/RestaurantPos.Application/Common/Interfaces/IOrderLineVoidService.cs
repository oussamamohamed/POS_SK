using System;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public enum OrderLineVoidOutcome
{
    Voided,
    Corrected,
    NotFound,
    Forbidden,
    ReasonRequired,
    ReasonTooLong,
    InvalidQuantity,
    OrderHasReceipts,
    NotModifiable,

    /// <summary>Clé d'idempotence déjà utilisée pour une autre demande (commande, ligne, quantité ou motif différents).</summary>
    IdempotencyKeyReuse
}

/// <summary>
/// <paramref name="VoidId"/> : trace écrite (annulation seulement). <paramref name="Detail"/> : numéros des reçus non annulés (refus <c>OrderHasReceipts</c>).
/// <paramref name="TotalTtcCents"/> : total de la commande après l'effet (succès avec clé). <paramref name="Replayed"/> : succès déjà appliqué sous la même clé, rendu sans nouvel effet.
/// </summary>
public record OrderLineVoidResult(OrderLineVoidOutcome Outcome, Guid? VoidId, string? Detail, long TotalTtcCents = 0, bool Replayed = false);

public interface IOrderLineVoidService
{
    /// <summary>
    /// Ligne envoyée en cuisine : annulation (manager ou admin, motif obligatoire, trace et JET).
    /// Ligne non envoyée : correction (tout opérateur, sans motif ni trace).
    /// <paramref name="idempotencyKey"/> : une demande déjà appliquée sous cette clé est rejouée sans nouvel effet ; nulle = pas de déduplication.
    /// </summary>
    Task<OrderLineVoidResult> VoidAsync(Guid orderId, Guid orderItemId, int quantity, string? reason, Guid operatorId, Guid? idempotencyKey = null, CancellationToken ct = default);
}
