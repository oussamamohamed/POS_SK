using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Domain.Entities;

/// <summary>Poste appairé (iPad ou caisse web). Son TerminalId porte sa propre chaîne NF525.</summary>
public class Device
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public DeviceRole Role { get; set; }
    public required string TerminalId { get; init; }
    public required string TokenHash { get; init; }
    public DateTimeOffset PairedAtUtc { get; init; }
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? RevokedAtUtc { get; set; }
    /// <summary>Imprimante des tickets client de ce poste (null = imprimante du poste RECEIPT).</summary>
    public Guid? ReceiptPrinterId { get; set; }
}

/// <summary>Code d'appairage à usage unique généré depuis le back-office.</summary>
public class DevicePairingCode
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string CodeHash { get; init; }
    public required string Name { get; init; }
    public DeviceRole Role { get; init; }
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public DateTimeOffset? UsedAtUtc { get; set; }
    public Guid CreatedByOperatorId { get; init; }
}
