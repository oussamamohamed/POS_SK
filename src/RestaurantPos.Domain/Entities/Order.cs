using System;
using System.Collections.Generic;
using System.Linq;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Enums;
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
    public OrderDestination Destination { get; set; } = OrderDestination.Takeaway;
    public string? PickupNumber { get; set; }
    public string? PickupBuzzer { get; set; }
    public DateTimeOffset? PickupScheduledAtUtc { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<OrderItem> Items { get; init; } = [];

    // Order-Level Discount & Tips
    public DiscountType? GlobalDiscountType { get; set; }
    public decimal GlobalDiscountValue { get; set; }
    public string? GlobalDiscountReason { get; set; }
    public Money TipAmount { get; set; } = Money.Zero();

    public Money TotalTtc => CalculateTotalTtc();
    public Money TotalHt => new(CalculateTaxBreakdown().Sum(t => t.TaxableBaseCents));

    /// <summary>Somme des TTC de lignes, avant remise de commande.</summary>
    public Money Subtotal => new(Items.Sum(i => i.CalculateTotalTtc().AmountInCents));

    /// <summary>Montant de la remise de commande réellement appliquée (`Subtotal − TotalTtc`).</summary>
    public Money DiscountAmount => new(Subtotal.AmountInCents - CalculateTotalTtc().AmountInCents);

    public decimal GetEffectiveTaxRate(OrderItem item)
    {
        if (Destination == OrderDestination.Takeaway && item.TaxRateTakeawayPercent.HasValue)
        {
            return item.TaxRateTakeawayPercent.Value;
        }
        return item.TaxRatePercent;
    }

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

    /// <summary>
    /// Ventilation fiscale de la commande : un groupe par taux effectif (ordre croissant), la remise de commande (pourcentage ou montant
    /// fixe) étant répartie sur les groupes au prorata de leur TTC (plus fort reste). Σ (base + TVA) = <see cref="TotalTtc"/>.
    /// </summary>
    public List<TaxBreakdownItem> CalculateTaxBreakdown()
    {
        var groups = Items
            .GroupBy(GetEffectiveTaxRate)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Ttc: g.Sum(x => x.CalculateTotalTtc().AmountInCents)))
            .ToList();
        long[] shares = TaxAllocation.Allocate(CalculateTotalTtc().AmountInCents, groups.Select(g => g.Ttc).ToList());
        return groups.Select((g, i) => TaxBreakdownItem.Calculate(g.Rate, shares[i])).ToList();
    }
}

public class OrderItem : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public required string ProductName { get; set; }

    private int _quantity = 1;
    public int Quantity
    {
        get => _quantity;
        set
        {
            if (_quantity != value)
            {
                _quantity = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTtc));
                OnPropertyChanged(nameof(TaxAmount));
            }
        }
    }

    private Money _unitPrice;
    public Money UnitPrice
    {
        get => _unitPrice;
        set
        {
            if (_unitPrice != value)
            {
                _unitPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TotalTtc));
                OnPropertyChanged(nameof(TaxAmount));
            }
        }
    }

    public decimal TaxRatePercent { get; set; } = 10.0m;
    public decimal? TaxRateTakeawayPercent { get; set; }
    public bool IsFoodVoucherEligible { get; set; } = true;
    public string? PreparationStationId { get; set; }
    public bool IsDispatched { get; set; }
    public List<string> SelectedModifiers { get; init; } = [];
    public Money ModifiersPriceExtra { get; set; } = Money.Zero();
    public string? KitchenComment { get; set; }

    // Course & Discounts
    public CourseType Course { get; set; } = CourseType.Direct;
    public decimal DiscountPercent { get; set; }
    public bool IsComp { get; set; }
    public string? CompReason { get; set; }

    // Happy Hour Traceability (NF525 compliant)
    public bool IsHappyHourApplied { get; set; }
    public Money? OriginalUnitPrice { get; set; }
    public Guid? AppliedHappyHourScheduleId { get; set; }
    public DateTimeOffset OrderedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public Money TotalTtc => CalculateTotalTtc();
    public Money TaxAmount => Money.FromCents(TaxBreakdownItem.Calculate(TaxRatePercent, CalculateTotalTtc().AmountInCents).TaxAmountCents);

    public Money CalculateTotalTtc()
    {
        if (IsComp) return Money.Zero();
        Money effectiveUnitPrice = UnitPrice + ModifiersPriceExtra;
        if (DiscountPercent > 0)
        {
            long baseCents = (effectiveUnitPrice * Quantity).AmountInCents;
            long discountedCents = (long)Math.Round(baseCents * (1.0m - (DiscountPercent / 100.0m)), MidpointRounding.AwayFromZero);
            return Money.FromCents(Math.Max(0, discountedCents));
        }
        return effectiveUnitPrice * Quantity;
    }
}
