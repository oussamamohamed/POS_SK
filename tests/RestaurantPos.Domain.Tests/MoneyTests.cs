using System;
using FluentAssertions;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class MoneyTests
{
    [Fact]
    public void Constructor_WithValidValues_InitializesCorrectly()
    {
        var money = new Money(1250, "EUR");

        money.AmountInCents.Should().Be(1250);
        money.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Constructor_WithLowerCaseCurrency_NormalizesToUpperCase()
    {
        var money = new Money(500, "usd");

        money.Currency.Should().Be("USD");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("EU")]
    [InlineData("EURO")]
    public void Constructor_WithInvalidCurrency_ThrowsArgumentException(string? invalidCurrency)
    {
        var act = () => new Money(100, invalidCurrency!);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("currency");
    }

    [Fact]
    public void FactoryMethods_CreateExpectedInstances()
    {
        Money.FromCents(2500, "EUR").AmountInCents.Should().Be(2500);
        Money.FromDecimal(19.99m, "EUR").AmountInCents.Should().Be(1999);
        Money.FromEuros(15.50m).AmountInCents.Should().Be(1550);
        Money.Zero().AmountInCents.Should().Be(0);
    }

    [Fact]
    public void FromDecimal_AppliesAwayFromZeroRounding()
    {
        // 0.005 rounded AwayFromZero is 0.01 (1 cent)
        Money.FromDecimal(0.005m).AmountInCents.Should().Be(1);
        Money.FromDecimal(0.004m).AmountInCents.Should().Be(0);
    }

    [Fact]
    public void Addition_WithSameCurrency_ReturnsSum()
    {
        var a = Money.FromCents(1000);
        var b = Money.FromCents(500);

        var result = a + b;

        result.AmountInCents.Should().Be(1500);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Addition_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        var a = Money.FromCents(1000, "EUR");
        var b = Money.FromCents(500, "USD");

        var act = () => a + b;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public void Subtraction_WithSameCurrency_ReturnsDifference()
    {
        var a = Money.FromCents(1000);
        var b = Money.FromCents(400);

        var result = a - b;

        result.AmountInCents.Should().Be(600);
        result.Currency.Should().Be("EUR");
    }

    [Fact]
    public void Subtraction_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        var a = Money.FromCents(1000, "EUR");
        var b = Money.FromCents(400, "USD");

        var act = () => a - b;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public void MultiplicationByInt_ScalesAmount()
    {
        var money = Money.FromCents(250);

        var result = money * 4;

        result.AmountInCents.Should().Be(1000);
    }

    [Fact]
    public void MultiplicationByDecimal_RoundsAwayFromZero()
    {
        var money = Money.FromCents(100);

        var result = money * 1.555m; // 155.5 -> 156 cents

        result.AmountInCents.Should().Be(156);
    }

    [Fact]
    public void Comparisons_WorkCorrectly()
    {
        var small = Money.FromCents(100);
        var large = Money.FromCents(200);
        var equal = Money.FromCents(100);

        (small < large).Should().BeTrue();
        (small <= equal).Should().BeTrue();
        (large > small).Should().BeTrue();
        (small >= equal).Should().BeTrue();
        (small == equal).Should().BeTrue();
        (small != large).Should().BeTrue();
    }

    [Fact]
    public void Comparison_WithDifferentCurrencies_ThrowsInvalidOperationException()
    {
        var a = Money.FromCents(100, "EUR");
        var b = Money.FromCents(100, "USD");

        var act = () => a.CompareTo(b);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*different currencies*");
    }

    [Fact]
    public void CompareToObject_HandlesNullAndInvalidTypes()
    {
        var money = Money.FromCents(100);

        money.CompareTo(null).Should().Be(1);

        var act = () => money.CompareTo("not-money");
        act.Should().Throw<ArgumentException>()
            .WithParameterName("obj");
    }

    [Fact]
    public void ToDecimal_ReturnsAmountInDecimal()
    {
        var money = Money.FromCents(1234);

        money.ToDecimal().Should().Be(12.34m);
    }

    [Fact]
    public void ToString_FormatsWithCurrency()
    {
        var money = Money.FromCents(1234, "EUR");

        var str = money.ToString();

        str.Should().Contain("12");
        str.Should().Contain("EUR");
    }
}
