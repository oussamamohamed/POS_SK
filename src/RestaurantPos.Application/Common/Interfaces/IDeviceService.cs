using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Application.Common.Interfaces;

public record PairingCodeResult(string Code, DateTimeOffset ExpiresAtUtc);

public record PairedDevice(Guid DeviceId, string Token, string TerminalId, string Name, DeviceRole Role);

public interface IDeviceService
{
    Task<PairingCodeResult> CreatePairingCodeAsync(string name, DeviceRole role, Guid createdByOperatorId, CancellationToken ct = default);

    /// <returns><c>null</c> si le code est inconnu, expiré ou déjà utilisé.</returns>
    Task<PairedDevice?> PairAsync(string code, CancellationToken ct = default);

    /// <returns><c>null</c> si le jeton est absent, inconnu ou révoqué. Met à jour la dernière activité.</returns>
    Task<Device?> AuthenticateAsync(string? token, CancellationToken ct = default);

    Task<IReadOnlyList<Device>> ListAsync(CancellationToken ct = default);

    Task<bool> RevokeAsync(Guid deviceId, CancellationToken ct = default);

    /// <returns>false si l'appareil ou l'imprimante n'existe pas.</returns>
    Task<bool> SetReceiptPrinterAsync(Guid deviceId, Guid? printerId, CancellationToken ct = default);
}
