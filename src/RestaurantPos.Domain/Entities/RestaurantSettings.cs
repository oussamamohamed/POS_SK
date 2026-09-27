using System;

namespace RestaurantPos.Domain.Entities;

/// <summary>Réglages du restaurant (ligne unique, Id = 1).</summary>
public class RestaurantSettings
{
    public const int SingletonId = 1;
    public int Id { get; init; } = SingletonId;
    public required string ReceiptLanguage { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
