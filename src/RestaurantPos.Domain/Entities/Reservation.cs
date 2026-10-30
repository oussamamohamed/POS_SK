using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public enum ReservationStatus
{
    Booked = 0,
    Seated = 1,
    Done = 2,
    NoShow = 3,
    Cancelled = 4
}

/// <summary>
/// Réservation ou entrée de la liste d'attente (<see cref="IsWaitlist"/> : pas de table, <see cref="StartsAtUtc"/> = heure d'arrivée).
/// Le téléphone est une donnée personnelle : facultatif, réservé aux responsables, purgé peu après le service.
/// </summary>
public class Reservation
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string GuestName { get; set; }
    public int Covers { get; set; }
    public DateTimeOffset StartsAtUtc { get; set; }
    public int DurationMinutes { get; set; } = 90;
    public string? TableNumber { get; set; }
    public string? Note { get; set; }
    public string? Phone { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Booked;
    public bool IsWaitlist { get; set; }
    /// <summary>Attente annoncée au client en liste d'attente, en minutes ; dépassée, elle remonte dans « à traiter ».</summary>
    public int? QuoteMinutes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
