using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

/// <summary>Trace d'une ligne envoyée en cuisine puis annulée (retirée de la commande) : copie de l'article, motif et opérateur.</summary>
public class OrderLineVoid
{
    /// <summary>Longueur maximale du motif (après suppression des espaces), comme <c>OrderDiscountAudit.Reason</c> : il entre dans le JET haché.</summary>
    public const int MaxReasonLength = 256;

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid? TicketId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; }

    /// <summary>Prix unitaire facturé (prix de base + suppléments des options).</summary>
    public Money UnitPrice { get; set; } = Money.Zero();
    public string? ModifiersSummary { get; set; }
    public string? KitchenComment { get; set; }
    public string? PreparationStationId { get; set; }
    public required string Reason { get; set; }
    public Guid OperatorId { get; set; }
    public required string OperatorName { get; set; }

    /// <summary>Serveur de la table au moment de l'annulation (vide s'il n'y en a pas).</summary>
    public string ServerName { get; set; } = string.Empty;
    public required string TableNumber { get; set; }
    public DateTimeOffset VoidedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}
