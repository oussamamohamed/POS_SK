using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public class ProductModifierOption
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid GroupId { get; set; }
    public required string Name { get; set; }
    public Money ExtraPrice { get; set; } = Money.Zero();
    public bool IsDefault { get; set; }
    public int DisplayOrder { get; set; }
}

public class ProductModifierGroup
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid ProductId { get; set; }
    public required string GroupName { get; set; }
    public int MinSelections { get; set; }
    public int MaxSelections { get; set; } = 1;
    public int DisplayOrder { get; set; }
    public List<ProductModifierOption> Options { get; init; } = [];

    public bool IsMandatory => MinSelections > 0;
    public bool IsSingleChoice => MaxSelections == 1;
}
