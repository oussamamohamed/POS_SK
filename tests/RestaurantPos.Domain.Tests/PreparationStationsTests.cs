using FluentAssertions;
using RestaurantPos.Domain.Common;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class PreparationStationsTests
{
    [Theory]
    [InlineData("BAR", "COLD", "DESSERT", "BAR")]
    [InlineData(null, "COLD", "DESSERT", "COLD")]
    [InlineData("", " ", "DESSERT", "DESSERT")]
    [InlineData(null, null, null, "HOT_KITCHEN")]
    public void Resolve_FollowsItemProductCategoryDefault(string? item, string? product, string? category, string expected) =>
        PreparationStations.Resolve(item, product, category).Should().Be(expected);

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("GRILL", true)]
    [InlineData("RECEIPT", false)]
    [InlineData("STATION-HOT", false)]
    public void IsKitchenStationOrEmpty(string? id, bool expected) =>
        PreparationStations.IsKitchenStationOrEmpty(id).Should().Be(expected);
}
