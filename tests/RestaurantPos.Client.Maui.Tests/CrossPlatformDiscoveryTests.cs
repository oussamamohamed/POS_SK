using System;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Client.Maui.Services;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class CrossPlatformDiscoveryTests
{
    [Fact]
    public async Task DiscoverPeripheralsAsyncReturnsDiscoveredEscPosPrinters()
    {
        // Arrange
        var discoveryService = new CrossPlatformDiscoveryService();

        // Act
        var peripherals = await discoveryService.DiscoverPeripheralsAsync(TimeSpan.FromMilliseconds(500));

        // Assert
        peripherals.Should().NotBeNull();
        peripherals.Should().Contain(p => p.ServiceType == "_printer._tcp");
    }

    [Fact]
    public async Task DiscoverMasterServersAsyncReturnsMasterServer()
    {
        // Arrange
        var discoveryService = new CrossPlatformDiscoveryService();

        // Act
        var servers = await discoveryService.DiscoverMasterServersAsync(TimeSpan.FromMilliseconds(500));

        // Assert
        servers.Should().NotBeNull();
        servers.Should().NotBeEmpty();
        servers.Should().Contain(s => s.IsActive && !string.IsNullOrEmpty(s.ServerUrl));
    }
}
