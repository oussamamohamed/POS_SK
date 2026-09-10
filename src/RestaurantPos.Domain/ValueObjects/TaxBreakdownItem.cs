namespace RestaurantPos.Domain.ValueObjects;

public readonly record struct TaxBreakdownItem
{
    public decimal TaxRatePercent { get; init; }
    public long TaxableBaseCents { get; init; }
    public long TaxAmountCents { get; init; }

    public TaxBreakdownItem(decimal taxRatePercent, long taxableBaseCents, long taxAmountCents)
    {
        TaxRatePercent = taxRatePercent;
        TaxableBaseCents = taxableBaseCents;
        TaxAmountCents = taxAmountCents;
    }

    public static TaxBreakdownItem Calculate(decimal taxRatePercent, long totalTtcCents)
    {
        // HT = TTC / (1 + Rate/100)
        decimal divisor = 1m + (taxRatePercent / 100m);
        long baseHtCents = (long)Math.Round(totalTtcCents / divisor, MidpointRounding.AwayFromZero);
        long taxCents = totalTtcCents - baseHtCents;
        return new TaxBreakdownItem(taxRatePercent, baseHtCents, taxCents);
    }
}
