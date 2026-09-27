using FluentAssertions;
using RestaurantPos.Api.Services;

namespace RestaurantPos.Api.Tests;

public class BonjourAdvertiserServiceTests
{
    [Theory]
    [InlineData("http://0.0.0.0:5080", (ushort)5080)]
    [InlineData("http://[::]:5081", (ushort)5081)]
    [InlineData("http://+:5082", (ushort)5082)]
    [InlineData("http://*:5083", (ushort)5083)]
    [InlineData("http://localhost:5084", (ushort)5084)]
    public void PortFrom_ReadsKestrelAddress(string address, ushort expected)
    {
        BonjourAdvertiserService.PortFrom([address]).Should().Be(expected);
    }

    [Fact]
    public void PortFrom_PrefersHttpOverHttps_AndReturnsNullWhenEmpty()
    {
        BonjourAdvertiserService.PortFrom(["https://0.0.0.0:7001", "http://0.0.0.0:5080"]).Should().Be((ushort)5080);
        BonjourAdvertiserService.PortFrom([]).Should().BeNull();
    }
}
