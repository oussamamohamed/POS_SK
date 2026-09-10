using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public class Product
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public required string CategoryId { get; set; }
    public string? Description { get; set; }
    public Money Price { get; set; }
    public decimal TaxRatePercent { get; set; } = 10.0m;
    public string? ColorHex { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public bool IsQuickKey { get; set; }
    public string? PreparationStationId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<string> Modifiers { get; init; } = [];
}
