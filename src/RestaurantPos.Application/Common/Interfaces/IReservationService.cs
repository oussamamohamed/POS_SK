using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public enum ReservationError
{
    NotFound,
    InvalidName,
    InvalidCovers,
    InvalidDuration,
    InvalidPhone,
    UnknownTable,
    TableRequired,
    TableCapacity,
    TableConflict,
    TableOccupied,
    InvalidState
}

public class ReservationException : Exception
{
    public ReservationError Error { get; }

    public ReservationException(ReservationError error) : base(error.ToString())
    {
        Error = error;
    }
}

public interface IReservationService
{
    Task<IReadOnlyList<ReservationDto>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includePhone, CancellationToken ct = default);
    Task<ReservationDto> CreateAsync(ReservationInput input, bool includePhone, CancellationToken ct = default);
    Task<ReservationDto> UpdateAsync(Guid id, ReservationInput input, bool includePhone, CancellationToken ct = default);
    Task<ReservationDto> SeatAsync(Guid id, string? tableNumber, Guid operatorId, string operatorName, bool includePhone, CancellationToken ct = default);
    Task<ReservationDto> SetStatusAsync(Guid id, ReservationStatus status, bool includePhone, CancellationToken ct = default);

    /// <summary>Efface les téléphones des réservations passées et supprime les anciennes lignes. Retourne le nombre de lignes touchées.</summary>
    Task<int> PurgeAsync(CancellationToken ct = default);
}
