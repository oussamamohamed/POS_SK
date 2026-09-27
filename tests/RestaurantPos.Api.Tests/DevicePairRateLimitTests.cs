using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Tests;

public class DevicePairRateLimitTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DevicePairRateLimitTests(PosApiApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Pair_AfterFiveWrongCodes_Returns429()
    {
        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            (await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        var locked = await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"));

        locked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
