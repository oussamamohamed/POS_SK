using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public class TerminalLayoutProfile
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string ProfileName { get; set; }
    public List<string> OrderedCategoryIds { get; set; } = [];
    public List<Guid> QuickKeyProductIds { get; set; } = [];
    public int GridColumnCount { get; set; } = 4;
    public string DefaultLandingView { get; set; } = "SalesTerminal";
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
