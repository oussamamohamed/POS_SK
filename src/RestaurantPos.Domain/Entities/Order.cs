using System;
using System.Collections.Generic;
using System.Linq;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Domain.Entities;

public enum OrderStatus
{
    Open = 0,
    SentToKitchen = 1,
    BillRequested = 2,
    Paid = 3,
    Cancelled = 4
}

public class Order
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public string TableNumber { get; set; } = "Comptoir";
    public Guid OperatorId { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<OrderItem> Items { get; init; } = [];

    // Order-Level Discount & Tips
    public DiscountType? GlobalDiscountType { get; set; }
    public decimal GlobalDiscountValue { get; set; }
    public string? GlobalDiscountReason { get; set; }
    public Money TipAmount { get; set; } = Money.Zero();

    public Money TotalTtc => CalculateTotalTtc();
    public Money TotalHt => new(CalculateTaxBreakdown().Sum(t => t.TaxableBaseCents));

    public Money CalculateTotalTtc()
    {
        long itemsTotalCents = Items.Sum(i => i.CalculateTotalTtc().AmountInCents);
        if (GlobalDiscountType == DiscountType.Percentage && GlobalDiscountValue > 0)
        {
            long discountedCents = (long)Math.Round(itemsTotalCents * (1.0m - (GlobalDiscountValue / 100.0m)), MidpointRounding.AwayFromZero);
            return Money.FromCents(Math.Max(0, discountedCents));
        }
        if (GlobalDiscountType == DiscountType.FixedAmount && GlobalDiscountValue > 0)
        {
            long discountCents = (long)Math.Round(GlobalDiscountValue * 100m, MidpointRounding.AwayFromZero);
            return Money.FromCents(Math.Max(0, itemsTotalCents - discountCents));
        }
        return new Money(itemsTotalCents);
    }

    public List<TaxBreakdownItem> CalculateTaxBreakdown()
    {
        return Items
            .GroupBy(i => i.TaxRatePercent)
            .Select(g =>
            {
                long groupTtcCents = g.Sum(x => x.CalculateTotalTtc().AmountInCents);
                if (GlobalDiscountType == DiscountType.Percentage && GlobalDiscountValue > 0)
                {
                    groupTtcCents = (long)Math.Round(groupTtcCents * (1.0m - (GlobalDiscountValue / 100.0m)), MidpointRounding.AwayFromZero);
                }
                return TaxBreakdownItem.Calculate(g.Key, Math.Max(0, groupTtcCents));
            })
            .ToList();
    }
}

public class OrderItem
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }
    public int Quantity { get; set; } = 1;
    public Money UnitPrice { get; set; }
    public decimal TaxRatePercent { get; set; } = 10.0m;
    public string? PreparationStationId { get; set; }
    public bool IsDispatched { get; set; }
    public List<string> SelectedModifiers { get; init; } = [];
    public string? KitchenComment { get; set; }

    // Course & Discounts
    public CourseType Course { get; set; } = CourseType.Direct;
    public decimal DiscountPercent { get; set; }
    public bool IsComp { get; set; }
    public string? CompReason { get; set; }

    public Money TotalTtc => CalculateTotalTtc();
    public Money TaxAmount => Money.FromCents(TaxBreakdownItem.Calculate(TaxRatePercent, CalculateTotalTtc().AmountInCents).TaxAmountCents);

    public Money CalculateTotalTtc()
    {
        if (IsComp) return Money.Zero();
        if (DiscountPercent > 0)
        {
            long baseCents = (UnitPrice * Quantity).AmountInCents;
            long discountedCents = (long)Math.Round(baseCents * (1.0m - (DiscountPercent / 100.0m)), MidpointRounding.AwayFromZero);
            return Money.FromCents(Math.Max(0, discountedCents));
        }
        return UnitPrice * Quantity;
    }
}
