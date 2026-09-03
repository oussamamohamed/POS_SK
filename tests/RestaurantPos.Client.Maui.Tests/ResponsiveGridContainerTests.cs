using FluentAssertions;
using RestaurantPos.Client.Maui.Controls;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class ResponsiveGridContainerTests
{
    [Theory]
    [InlineData(600, 2)]
    [InlineData(800, 3)]
    [InlineData(1024, 4)]
    [InlineData(1366, 5)]
    [InlineData(1920, 6)]
    public void CalculateColumnCountAdaptsGridToDisplayWidth(double width, int expectedColumns)
    {
        // Act
        int columns = ResponsiveLayoutEngine.CalculateColumnCount(width);

        // Assert
        columns.Should().Be(expectedColumns);
    }

    [Fact]
    public void CalculateTileDimensionAlwaysPreservesMinimumTouchTarget()
    {
        // Arrange
        double smallWidth = 320;
        int columns = 4;

        // Act
        double dimension = ResponsiveLayoutEngine.CalculateTileDimension(smallWidth, columns);

        // Assert
        dimension.Should().BeGreaterThanOrEqualTo(ResponsiveLayoutEngine.MinimumTouchTargetSizePt);
    }
}
