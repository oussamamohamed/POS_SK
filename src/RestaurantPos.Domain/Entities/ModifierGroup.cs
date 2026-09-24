using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public class ModifierOption
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public Money ExtraPrice { get; set; } = Money.Zero();
}

public class ModifierGroup
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public List<ModifierOption> Options { get; init; } = [];
}
