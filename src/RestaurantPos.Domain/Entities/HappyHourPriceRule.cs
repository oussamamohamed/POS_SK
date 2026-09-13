using System;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public enum HappyHourTargetType
{
    Product = 0,
    Category = 1
}

public enum HappyHourPricingMode
{
    FixedPrice = 0,
    PercentageDiscount = 1
}

public class HappyHourPriceRule
{
    public Guid Id { get; set; } = UuidV7.NewGuid();
    public Guid ScheduleId { get; set; }

    public HappyHourTargetType TargetType { get; set; }
    public string TargetId { get; set; } = "";
    public string TargetName { get; set; } = "";

    public HappyHourPricingMode PricingMode { get; set; }

    // Fixed price in EUR (Money) OR Percentage (0-100)
    public Money? FixedPrice { get; set; }
    public decimal? DiscountPercent { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
