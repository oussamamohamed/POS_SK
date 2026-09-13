using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public class HappyHourSchedule
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public required string Name { get; set; }

    // Days of week (List serialized as JSON or value-converted in EF Core)
    public List<DayOfWeek> DaysOfWeek { get; set; } = new();

    // Time windows (Local restaurant time)
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public bool IsActive { get; set; } = true;
    public bool AppliesToTakeaway { get; set; }
    public int Priority { get; set; } = 1;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    // Associated pricing rules
    public List<HappyHourPriceRule> PriceRules { get; set; } = new();
}
