using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class SupervisorPinGuardTests : IClassFixture<PosApiApplicationFactory>
{
    private const string LimiterKey = "supervisor:unknown-client";
    private readonly PosApiApplicationFactory _factory;

    public SupervisorPinGuardTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        _factory.Services.GetRequiredService<IPinRateLimiterService>().ResetAttempts(LimiterKey);
    }

    private HttpClient Client(string role = "Waiter")
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private static Task<HttpResponseMessage> HeldVoid(HttpClient c, string pin) =>
        c.PostAsJsonAsync($"/api/orders/counter/held/{Guid.NewGuid()}/void", new VoidHeldOrderRequest(pin, "test", "POS_A"));

    [Fact]
    public async Task Held_void_wrong_pin_burst_gets_429()
    {
        var client = Client();
        for (var i = 0; i < 5; i++)
        {
            (await HeldVoid(client, "0000")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await HeldVoid(client, "9999")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Held_void_unknown_hold()
    {
        var client = Client();
        var limiter = _factory.Services.GetRequiredService<IPinRateLimiterService>();

        (await HeldVoid(client, "0000")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await HeldVoid(client, "9999")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Le bon PIN remet le compteur à zéro : 5 faux sont à nouveau nécessaires pour verrouiller.
        for (var i = 0; i < 4; i++)
        {
            await HeldVoid(client, "0000");
        }

        limiter.IsLocked(LimiterKey).Should().BeFalse();
        await HeldVoid(client, "0000");
        limiter.IsLocked(LimiterKey).Should().BeTrue();
    }

    [Fact]
    public async Task Happy_hour_override_requires_auth()
    {
        var client = _factory.CreateClient();

        (await client.PostAsJsonAsync("/api/happy-hour/override/activate", new ActivateOverrideRequest("T01", "1234", 30, "x")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync("/api/happy-hour/override/stop", new StopOverrideRequest("T01", "1234", "x")))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Shared_key_across_routes()
    {
        var client = Client();
        for (var i = 0; i < 3; i++)
        {
            await HeldVoid(client, "0000");
        }

        for (var i = 0; i < 2; i++)
        {
            (await client.PostAsJsonAsync("/api/auth/override", new SupervisorOverrideRequest("0000", "VOID_RECEIPT")))
                .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        (await client.PostAsJsonAsync("/api/auth/override", new SupervisorOverrideRequest("1234", "VOID_RECEIPT")))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.PostAsJsonAsync("/api/happy-hour/override/stop", new StopOverrideRequest("T01", "1234", "x")))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Waiter_pin_is_403_and_counts()
    {
        var client = Client();
        var limiter = _factory.Services.GetRequiredService<IPinRateLimiterService>();

        for (var i = 0; i < 5; i++)
        {
            (await HeldVoid(client, "2468")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        limiter.IsLocked(LimiterKey).Should().BeTrue();
    }

    [Fact]
    public async Task Empty_pin_is_400_and_not_counted()
    {
        var client = Client();

        (await HeldVoid(client, " ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        _factory.Services.GetRequiredService<IPinRateLimiterService>().GetRemainingLockout(LimiterKey).Should().Be(TimeSpan.Zero);
    }
}
