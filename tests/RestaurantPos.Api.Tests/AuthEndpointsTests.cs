using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using RestaurantPos.Application.DTOs;

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

    [Fact]
    public async Task GetMe_WithValidToken_ShouldReturnOperatorProfile()
    {
        // Arrange
        var client = _factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResultDto>();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult!.Token);

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("Admin");
    }

    [Fact]
    public async Task GetMe_WithoutToken_ShouldReturnUnauthorized()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/auth/me");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task VoidReceipt_WithWaiterRole_ShouldReturnForbidden()
    {
        // Arrange - using test header X-Test-Role: Waiter
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");

        var voidReq = new VoidReceiptRequest("TERM-1", Guid.NewGuid());

        // Act
        var response = await client.PostAsJsonAsync($"/api/checkout/void/{Guid.NewGuid()}", voidReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VoidReceipt_WithFloorManagerRole_ShouldNotBeForbidden()
    {
        // Arrange - using test header X-Test-Role: FloorManager
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var voidReq = new VoidReceiptRequest("TERM-1", Guid.NewGuid());

        // Act
        var response = await client.PostAsJsonAsync($"/api/checkout/void/{Guid.NewGuid()}", voidReq);

        // Assert - will be BadRequest (receipt not found) but NOT 401 or 403
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SupervisorOverride_WithValidManagerPin_ShouldSucceed()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter"); // Acting as logged-in waiter

        var overrideReq = new SupervisorOverrideRequest("1234", "VOID_RECEIPT"); // 1234 is Alexandre Dupont (FloorManager)

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/override", overrideReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"authorized\":true");
        content.Should().Contain("FloorManager");
    }

    [Fact]
    public async Task SupervisorOverride_WithWaiterPin_ShouldBeForbidden()
    {
        // Arrange
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");

        var overrideReq = new SupervisorOverrideRequest("2468", "VOID_RECEIPT"); // 2468 is Sophie Martin (Waiter)

        // Act
        var response = await client.PostAsJsonAsync("/api/auth/override", overrideReq);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
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
