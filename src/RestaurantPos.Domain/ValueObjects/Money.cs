using System;

namespace RestaurantPos.Domain.ValueObjects;

public readonly record struct Money : IComparable<Money>, IComparable
{
    public long AmountInCents { get; }
    public string Currency { get; }

    public Money(long amountInCents, string currency = "EUR")
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
        {
            throw new ArgumentException("Currency must be a valid 3-letter ISO code.", nameof(currency));
        }

        AmountInCents = amountInCents;
        Currency = currency.ToUpperInvariant();
    }

    public static Money FromCents(long amountInCents, string currency = "EUR") => new(amountInCents, currency);

    public static Money FromDecimal(decimal amount, string currency = "EUR")
    {
        return new Money((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero), currency);
    }

    public static Money FromEuros(decimal amount) => FromDecimal(amount, "EUR");

    public decimal ToDecimal() => AmountInCents / 100m;

    public static Money Zero(string currency = "EUR") => new(0, currency);

    public static Money operator +(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountInCents + b.AmountInCents, a.Currency);
    }

    public static Money operator -(Money a, Money b)
    {
        EnsureSameCurrency(a, b);
        return new Money(a.AmountInCents - b.AmountInCents, a.Currency);
    }

    public static Money operator *(Money a, int quantity)
    {
        return new Money(a.AmountInCents * quantity, a.Currency);
    }

    public static Money operator *(Money a, decimal factor)
    {
        return new Money((long)Math.Round(a.AmountInCents * factor, MidpointRounding.AwayFromZero), a.Currency);
    }

    public static bool operator <(Money a, Money b) => a.CompareTo(b) < 0;
    public static bool operator <=(Money a, Money b) => a.CompareTo(b) <= 0;
    public static bool operator >(Money a, Money b) => a.CompareTo(b) > 0;
    public static bool operator >=(Money a, Money b) => a.CompareTo(b) >= 0;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return AmountInCents.CompareTo(other.AmountInCents);
    }

    public int CompareTo(object? obj)
    {
        if (obj is null) return 1;
        if (obj is not Money other)
        {
            throw new ArgumentException("Object must be of type Money.", nameof(obj));
        }
        return CompareTo(other);
    }

    private static void EnsureSameCurrency(Money a, Money b)
    {
        if (a.Currency != b.Currency)
        {
            throw new InvalidOperationException($"Cannot operate on money with different currencies: '{a.Currency}' vs '{b.Currency}'.");
        }
    }

    public override string ToString() => $"{ToDecimal():N2} {Currency}";
}
