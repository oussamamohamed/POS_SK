using System;

namespace RestaurantPos.Application.DTOs;

public sealed record ReservationInput(
    string GuestName,
    int Covers,
    DateTimeOffset StartsAtUtc,
    int DurationMinutes,
    string? TableNumber,
    string? Note,
    string? Phone,
    bool IsWaitlist);

/// <summary><c>Phone</c> n'est renseigné que pour un responsable ; les autres voient seulement <c>HasPhone</c>.</summary>
public sealed record ReservationDto(
    Guid Id,
    string GuestName,
    int Covers,
    DateTimeOffset StartsAtUtc,
    int DurationMinutes,
    string? TableNumber,
    string? Note,
    string? Phone,
    bool HasPhone,
    int Status,
    bool IsWaitlist,
    bool IsLate,
    DateTimeOffset CreatedAtUtc);
