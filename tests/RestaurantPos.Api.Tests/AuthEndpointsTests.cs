using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class AuthEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public AuthEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_WithValidPin_ShouldReturnJwtToken()
    {
        // Arrange
        var client = _factory.CreateClient();

        var request = new PinLoginRequest("9999"); // Admin Système

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Token.Should().NotBeNullOrWhiteSpace();
        result.Role.Should().Be("Admin");
        result.OperatorName.Should().Be("Admin Système");
    }

    [Fact]
    public async Task Login_WithInvalidPin_ShouldReturnBadRequest()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new PinLoginRequest("0000"); // Wrong PIN

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/login", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

public class LoginResultDto
{
    public bool Success { get; set; }
    public Guid OperatorId { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}
