using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class DeviceService : IDeviceService
{
    public const int CodeLength = 8;
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);
    private const string CodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    // Un seul serveur par restaurant : un verrou process suffit pour rendre
    // « code consommé + TerminalId attribué » atomique entre deux appairages simultanés.
    private static readonly SemaphoreSlim PairingLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly TimeProvider _time;

    public DeviceService(AppDbContext db, TimeProvider time)
    {
        _db = db;
        _time = time;
    }

    public static string Sha256Hex(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    public async Task<PairingCodeResult> CreatePairingCodeAsync(string name, DeviceRole role, Guid createdByOperatorId, CancellationToken ct = default)
    {
        var code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        var expiresAt = _time.GetUtcNow() + CodeLifetime;
        _db.DevicePairingCodes.Add(new DevicePairingCode
        {
            CodeHash = Sha256Hex(code),
            Name = name,
            Role = role,
            ExpiresAtUtc = expiresAt,
            CreatedByOperatorId = createdByOperatorId
        });
        await _db.SaveChangesAsync(ct);
        return new PairingCodeResult(code, expiresAt);
    }

    public async Task<PairedDevice?> PairAsync(string code, CancellationToken ct = default)
    {
        var normalized = Normalize(code);
        if (normalized.Length != CodeLength)
        {
            return null;
        }

        var codeHash = Sha256Hex(normalized);
        await PairingLock.WaitAsync(ct);
        try
        {
            var now = _time.GetUtcNow();
            // Filtre d'expiration en mémoire : SQLite ne traduit pas les comparaisons de DateTimeOffset.
            var pairing = await _db.DevicePairingCodes.FirstOrDefaultAsync(p => p.CodeHash == codeHash, ct);
            if (pairing is null || pairing.UsedAtUtc is not null || pairing.ExpiresAtUtc <= now)
            {
                return null;
            }

            pairing.UsedAtUtc = now;
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            var count = await _db.Devices.CountAsync(ct);
            var device = new Device
            {
                Name = pairing.Name,
                Role = pairing.Role,
                TerminalId = string.Create(CultureInfo.InvariantCulture, $"T{count + 1:D2}"),
                TokenHash = Sha256Hex(token),
                PairedAtUtc = now
            };
            _db.Devices.Add(device);
            await _db.SaveChangesAsync(ct);
            return new PairedDevice(device.Id, token, device.TerminalId, device.Name, device.Role);
        }
        finally
        {
            PairingLock.Release();
        }
    }

    public async Task<Device?> AuthenticateAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var tokenHash = Sha256Hex(token.Trim());
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.TokenHash == tokenHash, ct);
        if (device is null || device.RevokedAtUtc is not null)
        {
            return null;
        }

        device.LastSeenUtc = _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return device;
    }

    // ponytail: tri texte correct jusqu'à T99 ; au-delà (T100), trier sur un numéro entier stocké à part.
    public async Task<IReadOnlyList<Device>> ListAsync(CancellationToken ct = default) =>
        await _db.Devices.AsNoTracking().OrderBy(d => d.TerminalId).ToListAsync(ct);

    public async Task<bool> RevokeAsync(Guid deviceId, CancellationToken ct = default)
    {
        var device = await _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId, ct);
        if (device is null)
        {
            return false;
        }

        device.RevokedAtUtc ??= _time.GetUtcNow();
        await _db.SaveChangesAsync(ct);
        return true;
    }
}
