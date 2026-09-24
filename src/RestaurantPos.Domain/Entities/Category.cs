using System;

namespace RestaurantPos.Domain.Entities;

public class Category
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? IconName { get; set; }
    public string? ColorHex { get; set; }
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
