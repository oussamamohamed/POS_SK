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

/// <summary>
/// Demande d'annulation / correction de ligne déjà appliquée, par clé d'idempotence (en-tête <c>Idempotency-Key</c>) : la même clé
/// rejoue ce résultat sans nouvel effet. Seuls les succès sont gardés ; purge après <see cref="RetentionDays"/> jours.
/// </summary>
public class OrderLineVoidRequest
{
    public const int RetentionDays = 7;

    public Guid IdempotencyKey { get; init; }
    public Guid OrderId { get; init; }
    public Guid OrderItemId { get; init; }
    public int Quantity { get; init; }

    /// <summary>SHA-256 (hex) du motif sans espaces de bord (vide si absent).</summary>
    public required string ReasonHash { get; init; }

    /// <summary>Trace écrite : non nulle pour une annulation, nulle pour une correction.</summary>
    public Guid? VoidId { get; init; }
    public long TotalTtcCents { get; init; }

    /// <summary>DateTime UTC (et non DateTimeOffset) : SQLite sait le comparer côté base pour la purge.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
