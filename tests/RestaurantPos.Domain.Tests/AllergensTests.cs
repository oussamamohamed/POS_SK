using FluentAssertions;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class AllergensTests
{
    [Fact]
    public void Order_IsFrozen()
    {
        // Le masque est stocké en base et partagé avec le web et l'iPad : l'ordre ne change jamais.
        Allergens.Names.Should().Equal(
            "gluten", "crustaceans", "eggs", "fish", "peanuts", "soy", "milk",
            "nuts", "celery", "mustard", "sesame", "sulphites", "lupin", "molluscs");
    }

    [Fact]
    public void Contains_UsesBitPosition()
    {
        var mask = (1 << 0) | (1 << 6); // gluten + lait
        Allergens.Contains(mask, "gluten").Should().BeTrue();
        Allergens.Contains(mask, "milk").Should().BeTrue();
        Allergens.Contains(mask, "eggs").Should().BeFalse();
        Allergens.Contains(mask, "inconnu").Should().BeFalse();
    }

    [Fact]
    public void Union_CombinesMasks() => Allergens.Union(0b01, 0b10).Should().Be(0b11);

    [Theory]
    [InlineData(0, true)]
    [InlineData(16383, true)]
    [InlineData(16384, false)]
    [InlineData(-1, false)]
    public void IsValidMask_RejectsOutOfRange(int mask, bool expected) =>
        Allergens.IsValidMask(mask).Should().Be(expected);
}
