using System;

namespace RestaurantPos.Domain.Entities;

/// <summary>
/// Réponse de succès d'une route d'encaissement, gardée par clé d'idempotence (en-tête <c>Idempotency-Key</c>) :
/// la même clé avec la même demande rejoue cette réponse sans nouvel effet (ni reçu, ni TPE, ni impression).
/// Seuls les succès sont gardés ; purge après <see cref="RetentionDays"/> jours.
/// </summary>
public class IdempotentRequest
{
    public const int RetentionDays = 7;

    public Guid IdempotencyKey { get; init; }

    /// <summary>SHA-256 (hex) de la route, du poste et de la demande : une même clé pour une autre demande est refusée.</summary>
    public required string RequestHash { get; init; }

    public required string ResponseJson { get; init; }

    /// <summary>DateTime UTC (et non DateTimeOffset) : SQLite sait le comparer côté base pour la purge.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
