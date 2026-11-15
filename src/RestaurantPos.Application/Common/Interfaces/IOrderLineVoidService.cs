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
    InvalidQuantity,
    OrderHasReceipts,
    NotModifiable
}

/// <summary><paramref name="VoidId"/> : trace écrite (annulation seulement). <paramref name="Detail"/> : numéros des reçus non annulés (refus <c>OrderHasReceipts</c>).</summary>
public record OrderLineVoidResult(OrderLineVoidOutcome Outcome, Guid? VoidId, string? Detail);

public interface IOrderLineVoidService
{
    /// <summary>
    /// Ligne envoyée en cuisine : annulation (manager ou admin, motif obligatoire, trace et JET).
    /// Ligne non envoyée : correction (tout opérateur, sans motif ni trace).
    /// </summary>
    Task<OrderLineVoidResult> VoidAsync(Guid orderId, Guid orderItemId, int quantity, string? reason, Guid operatorId, CancellationToken ct = default);
}
