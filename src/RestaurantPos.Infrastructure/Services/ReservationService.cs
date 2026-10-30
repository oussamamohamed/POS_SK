using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed partial class ReservationService : IReservationService
{
    private static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);
    /// <summary>Le téléphone est effacé 2 jours après l'heure de la réservation.</summary>
    public static readonly TimeSpan PhoneRetention = TimeSpan.FromDays(2);
    /// <summary>La ligne entière (nom, note) est supprimée après 90 jours.</summary>
    public static readonly TimeSpan RowRetention = TimeSpan.FromDays(90);

    private readonly AppDbContext _db;
    private readonly ITableManagementService _tables;
    private readonly TimeProvider _clock;

    public ReservationService(AppDbContext db, ITableManagementService tables, TimeProvider clock)
    {
        _db = db;
        _tables = tables;
        _clock = clock;
    }

    [GeneratedRegex(@"^[+0-9 ().-]{6,20}$")]
    private static partial Regex PhonePattern();

    public async Task<IReadOnlyList<ReservationDto>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, bool includePhone, CancellationToken ct = default)
    {
        // shortcut: filtre en mémoire (DateTimeOffset non traduisible par SQLite) ; la purge à 90 jours borne la table.
        var rows = (await _db.Reservations.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .Where(r => r.StartsAtUtc >= fromUtc && r.StartsAtUtc <= toUtc)
            .OrderBy(r => r.StartsAtUtc).ThenBy(r => r.CreatedAtUtc)
            .Select(r => ToDto(r, includePhone))
            .ToList();
        return rows;
    }

    public async Task<ReservationDto> CreateAsync(ReservationInput input, bool includePhone, CancellationToken ct = default)
    {
        var clean = Validate(input);
        await CheckTableAsync(clean, ignoreId: null, ct).ConfigureAwait(false);
        var reservation = new Reservation { GuestName = clean.GuestName };
        Apply(reservation, clean);
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(reservation, includePhone);
    }

    public async Task<ReservationDto> UpdateAsync(Guid id, ReservationInput input, bool includePhone, CancellationToken ct = default)
    {
        var reservation = await FindAsync(id, ct).ConfigureAwait(false);
        if (reservation.Status != ReservationStatus.Booked)
        {
            throw new ReservationException(ReservationError.InvalidState);
        }
        var clean = Validate(input);
        await CheckTableAsync(clean, ignoreId: id, ct).ConfigureAwait(false);
        // Un non-responsable ne voit pas le téléphone : sa modification ne doit pas l'effacer.
        Apply(reservation, includePhone ? clean : clean with { Phone = reservation.Phone });
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(reservation, includePhone);
    }

    public async Task<ReservationDto> SeatAsync(Guid id, string? tableNumber, Guid operatorId, string operatorName, bool includePhone, CancellationToken ct = default)
    {
        var reservation = await FindAsync(id, ct).ConfigureAwait(false);
        if (reservation.Status != ReservationStatus.Booked)
        {
            throw new ReservationException(ReservationError.InvalidState);
        }
        var target = string.IsNullOrWhiteSpace(tableNumber) ? reservation.TableNumber : tableNumber.Trim();
        if (string.IsNullOrEmpty(target))
        {
            throw new ReservationException(ReservationError.TableRequired);
        }
        var table = await _db.DiningTables.AsNoTracking().FirstOrDefaultAsync(t => t.TableNumber == target, ct).ConfigureAwait(false)
            ?? throw new ReservationException(ReservationError.UnknownTable);
        if (reservation.Covers > table.Capacity)
        {
            throw new ReservationException(ReservationError.TableCapacity);
        }
        if (table.Status != TableStatus.Free || table.ActiveOrderId.HasValue)
        {
            throw new ReservationException(ReservationError.TableOccupied);
        }

        await _tables.OpenTableAsync(target, reservation.Covers, operatorId, operatorName, ct).ConfigureAwait(false);
        reservation.TableNumber = target;
        reservation.Status = ReservationStatus.Seated;
        reservation.UpdatedAtUtc = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(reservation, includePhone);
    }

    public async Task<ReservationDto> SetStatusAsync(Guid id, ReservationStatus status, bool includePhone, CancellationToken ct = default)
    {
        var reservation = await FindAsync(id, ct).ConfigureAwait(false);
        bool allowed = (reservation.Status, status) switch
        {
            (ReservationStatus.Booked, ReservationStatus.Cancelled or ReservationStatus.NoShow) => true,
            (ReservationStatus.Seated, ReservationStatus.Done) => true,
            _ => false
        };
        if (!allowed)
        {
            throw new ReservationException(ReservationError.InvalidState);
        }
        reservation.Status = status;
        reservation.UpdatedAtUtc = _clock.GetUtcNow();
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDto(reservation, includePhone);
    }

    /// <summary>
    /// Une table libérée (paiement soldé, libération manuelle) termine la réservation installée dessus.
    /// Écrit dans le contexte de l'appelant : à sauvegarder avec sa propre transaction.
    /// </summary>
    internal static async Task CompleteSeatedAsync(AppDbContext db, string tableNumber, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var r in await db.Reservations.Where(r => r.TableNumber == tableNumber && r.Status == ReservationStatus.Seated).ToListAsync(ct).ConfigureAwait(false))
        {
            r.Status = ReservationStatus.Done;
            r.UpdatedAtUtc = now;
        }
    }

    public async Task<int> PurgeAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var all = await _db.Reservations.ToListAsync(ct).ConfigureAwait(false);
        var expired = all.Where(r => r.StartsAtUtc < now - RowRetention).ToList();
        var phones = all.Except(expired).Where(r => r.Phone != null && r.StartsAtUtc < now - PhoneRetention).ToList();
        foreach (var r in phones) r.Phone = null;
        _db.Reservations.RemoveRange(expired);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count + phones.Count;
    }

    private async Task<Reservation> FindAsync(Guid id, CancellationToken ct) =>
        await _db.Reservations.FirstOrDefaultAsync(r => r.Id == id, ct).ConfigureAwait(false)
        ?? throw new ReservationException(ReservationError.NotFound);

    private static ReservationInput Validate(ReservationInput input)
    {
        var name = input.GuestName?.Trim() ?? string.Empty;
        if (name.Length is 0 or > 100) throw new ReservationException(ReservationError.InvalidName);
        if (input.Covers is < 1 or > 30) throw new ReservationException(ReservationError.InvalidCovers);
        if (input.DurationMinutes is < 30 or > 240) throw new ReservationException(ReservationError.InvalidDuration);
        if (input.QuoteMinutes is < 1 or > 240) throw new ReservationException(ReservationError.InvalidQuote);
        var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();
        if (phone is not null && !PhonePattern().IsMatch(phone)) throw new ReservationException(ReservationError.InvalidPhone);
        var note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim();
        var table = input.IsWaitlist || string.IsNullOrWhiteSpace(input.TableNumber) ? null : input.TableNumber.Trim();
        return input with { GuestName = name, Phone = phone, Note = note, TableNumber = table, QuoteMinutes = input.IsWaitlist ? input.QuoteMinutes : null };
    }

    private async Task CheckTableAsync(ReservationInput input, Guid? ignoreId, CancellationToken ct)
    {
        if (input.TableNumber is null) return;
        var table = await _db.DiningTables.AsNoTracking().FirstOrDefaultAsync(t => t.TableNumber == input.TableNumber, ct).ConfigureAwait(false)
            ?? throw new ReservationException(ReservationError.UnknownTable);
        if (input.Covers > table.Capacity) throw new ReservationException(ReservationError.TableCapacity);

        var end = input.StartsAtUtc.AddMinutes(input.DurationMinutes);
        var clash = (await _db.Reservations.AsNoTracking()
                .Where(r => r.TableNumber == input.TableNumber && !r.IsWaitlist && r.Id != ignoreId
                            && (r.Status == ReservationStatus.Booked || r.Status == ReservationStatus.Seated))
                .ToListAsync(ct).ConfigureAwait(false))
            .Any(r => r.StartsAtUtc < end && input.StartsAtUtc < r.StartsAtUtc.AddMinutes(r.DurationMinutes));
        if (clash) throw new ReservationException(ReservationError.TableConflict);
    }

    private void Apply(Reservation reservation, ReservationInput input)
    {
        reservation.GuestName = input.GuestName;
        reservation.Covers = input.Covers;
        reservation.StartsAtUtc = input.StartsAtUtc;
        reservation.DurationMinutes = input.DurationMinutes;
        reservation.TableNumber = input.TableNumber;
        reservation.Note = input.Note;
        reservation.Phone = input.Phone;
        reservation.IsWaitlist = input.IsWaitlist;
        reservation.QuoteMinutes = input.QuoteMinutes;
        reservation.UpdatedAtUtc = _clock.GetUtcNow();
    }

    private ReservationDto ToDto(Reservation r, bool includePhone) => new(
        r.Id, r.GuestName, r.Covers, r.StartsAtUtc, r.DurationMinutes, r.TableNumber, r.Note,
        includePhone ? r.Phone : null, !string.IsNullOrEmpty(r.Phone), (int)r.Status, r.IsWaitlist,
        r.Status == ReservationStatus.Booked && !r.IsWaitlist && _clock.GetUtcNow() > r.StartsAtUtc + LateGrace,
        r.CreatedAtUtc, r.QuoteMinutes);
}
