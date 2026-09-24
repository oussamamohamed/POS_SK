using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public partial class StaffManagementService : IStaffManagementService
{
    private readonly AppDbContext _dbContext;

    public StaffManagementService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<User>> GetAllStaffAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var query = _dbContext.Users.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(u => u.IsActive);
        }
        return await query.OrderBy(u => u.Name).ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<User?> GetStaffByIdAsync(Guid staffId, CancellationToken ct = default)
    {
        return await _dbContext.Users.FindAsync([staffId], ct).ConfigureAwait(false);
    }

    public async Task<User> CreateStaffMemberAsync(string name, UserRole role, string pin, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ValidatePinFormat(pin);

        var activeUsers = await _dbContext.Users.Where(u => u.IsActive).ToListAsync(ct).ConfigureAwait(false);
        foreach (var activeUser in activeUsers)
        {
            string computedHash = HashPin(pin, activeUser.PinSalt);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedHash),
                    Encoding.UTF8.GetBytes(activeUser.PinHash)))
            {
                throw new InvalidOperationException("Ce code PIN est déjà utilisé par un autre opérateur actif.");
            }
        }

        string salt = GenerateSalt();
        string pinHash = HashPin(pin, salt);

        var user = new User
        {
            Id = UuidV7.NewGuid(),
            Name = name.Trim(),
            Role = role,
            PinHash = pinHash,
            PinSalt = salt,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return user;
    }

    public async Task<User> UpdateStaffMemberAsync(Guid staffId, string name, UserRole role, bool isActive, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FindAsync([staffId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Opérateur introuvable: {staffId}");

        user.Name = name.Trim();
        user.Role = role;
        user.IsActive = isActive;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return user;
    }

    public async Task<bool> ResetStaffPinAsync(Guid staffId, string newPin, CancellationToken ct = default)
    {
        ValidatePinFormat(newPin);
        var user = await _dbContext.Users.FindAsync([staffId], ct).ConfigureAwait(false);
        if (user is null) return false;

        var activeUsers = await _dbContext.Users
            .Where(u => u.IsActive && u.Id != staffId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var activeUser in activeUsers)
        {
            string computedHash = HashPin(newPin, activeUser.PinSalt);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedHash),
                    Encoding.UTF8.GetBytes(activeUser.PinHash)))
            {
                throw new InvalidOperationException("Ce code PIN est déjà utilisé par un autre opérateur actif.");
            }
        }

        user.PinSalt = GenerateSalt();
        user.PinHash = HashPin(newPin, user.PinSalt);
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> DeactivateStaffMemberAsync(Guid staffId, CancellationToken ct = default)
    {
        var user = await _dbContext.Users.FindAsync([staffId], ct).ConfigureAwait(false);
        if (user is null) return false;

        user.IsActive = false;
        user.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static void ValidatePinFormat(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) || !PinRegex().IsMatch(pin))
        {
            throw new ArgumentException("Le code PIN doit comporter entre 4 et 6 chiffres numériques.");
        }
    }

    private static string HashPin(string rawPin, string salt)
    {
        byte[] combined = Encoding.UTF8.GetBytes(salt + rawPin);
        byte[] hash = SHA256.HashData(combined);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateSalt()
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(saltBytes).ToLowerInvariant();
    }

    [GeneratedRegex("^[0-9]{4,6}$")]
    private static partial Regex PinRegex();
}
