using FluentAssertions;
using RestaurantPos.Client.Maui.Services;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class EscPosByteGeneratorTests
{
    [Fact]
    public async Task KickDrawerAsyncWithUnreachableHostReturnsFalseGracefullyWithoutException()
    {
        // Arrange
        var client = new NetworkPrinterClient();

        // Act
        bool result = await client.KickDrawerAsync("127.0.0.1", 9999);

        // Assert
        result.Should().BeFalse();
    }
}
