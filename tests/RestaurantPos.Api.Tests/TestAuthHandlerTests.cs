using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace RestaurantPos.Api.Tests;

public class TestAuthHandlerTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public TestAuthHandlerTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("FloorManager")]
    [InlineData("Waiter")]
    [InlineData("KitchenStaff")]
    public async Task TestAuthHandler_WithRoleHeader_AuthenticatesSuccessfully(string role)
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Operator-Name", $"Test User {role}");

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain(role);
        content.Should().Contain($"Test User {role}");
    }

    [Fact]
    public async Task TestAuthHandler_WithoutRoleHeaderOrToken_ReturnsUnauthorizedOnProtectedEndpoint()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
