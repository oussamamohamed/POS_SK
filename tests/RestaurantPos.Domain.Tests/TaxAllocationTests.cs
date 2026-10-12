using FluentAssertions;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class TaxAllocationTests
{
    [Fact]
    public void Allocate_WhenTotalEqualsTheSum_ReturnsTheWeightsUnchanged()
    {
        long[] weights = [1000, 2000, 5];
        TaxAllocation.Allocate(3005, weights).Should().Equal(weights);
    }

    [Fact]
    public void Allocate_WhenTotalExceedsTheSum_IsCappedAtTheSum()
    {
        TaxAllocation.Allocate(9999, [100, 200]).Should().Equal(100, 200);
    }

    [Theory]
    [InlineData(3, new long[] { 1, 1, 1, 1, 1 }, new long[] { 1, 1, 1, 0, 0 })] // restes égaux : l'ordre des groupes départage
    [InlineData(2, new long[] { 1, 1, 1 }, new long[] { 1, 1, 0 })]
    [InlineData(100, new long[] { 100, 100, 100 }, new long[] { 34, 33, 33 })]
    [InlineData(2700, new long[] { 1000, 2000 }, new long[] { 900, 1800 })]
    [InlineData(1, new long[] { 3, 3 }, new long[] { 1, 0 })]
    [InlineData(5, new long[] { 0, 10, 0 }, new long[] { 0, 5, 0 })]
    public void Allocate_UsesTheLargestRemainderMethod(long total, long[] weights, long[] expected)
    {
        TaxAllocation.Allocate(total, weights).Should().Equal(expected);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Allocate_WithNothingToSpread_ReturnsZeros(long total)
    {
        TaxAllocation.Allocate(total, [10, 20]).Should().Equal(0, 0);
    }

    [Fact]
    public void Allocate_WithZeroWeightsOrNoGroups_ReturnsZerosOrEmpty()
    {
        TaxAllocation.Allocate(50, [0, 0]).Should().Equal(0, 0);
        TaxAllocation.Allocate(50, []).Should().BeEmpty();
    }

    [Fact]
    public void Allocate_IsDeterministicAndKeepsEveryInvariant_OnManyRandomCases()
    {
        var random = new Random(20261011);
        for (int i = 0; i < 5000; i++)
        {
            int count = random.Next(1, 7);
            var weights = Enumerable.Range(0, count).Select(_ => (long)random.Next(0, 100_000)).ToArray();
            long sum = weights.Sum();
            long total = sum == 0 ? 0 : random.NextInt64(0, sum + 1);

            var first = TaxAllocation.Allocate(total, weights);
            var second = TaxAllocation.Allocate(total, weights);

            string context = $"case {i}: total={total} weights=[{string.Join(",", weights)}]";
            second.Should().Equal(first, context);
            first.Sum().Should().Be(Math.Min(total, sum), context);
            for (int g = 0; g < count; g++)
            {
                first[g].Should().BeInRange(0, weights[g], context);
                // plus fort reste : jamais plus d'un centime d'écart avec la part exacte
                double exact = sum == 0 ? 0 : (double)total * weights[g] / sum;
                Math.Abs(first[g] - exact).Should().BeLessThan(1.0 + 1e-9, context);
            }
        }
    }

    [Fact]
    public void Allocate_NegativeWeights_AreTreatedAsZero()
    {
        TaxAllocation.Allocate(2, [-1, -1, 5]).Should().Equal(0, 0, 2);
        TaxAllocation.Allocate(9, [-1, -1, 5]).Should().Equal(0, 0, 5);
    }
}
