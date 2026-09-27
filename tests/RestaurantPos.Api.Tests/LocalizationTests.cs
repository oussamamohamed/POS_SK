using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class LocalizationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public LocalizationTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<string> PairWithBadCodeAsync(string? acceptLanguage)
    {
        // Le rate limiter de pairing est un singleton partagé par tous les cas du Theory
        // (même PosApiApplicationFactory) : le réinitialiser évite qu'un 429 masque le 400 attendu.
        _factory.Services.GetRequiredService<IPinRateLimiterService>().ResetAttempts("pair:unknown-client");
        var client = _factory.CreateClient();
        if (acceptLanguage is not null) client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        var response = await client.PostAsJsonAsync("/api/devices/pair", new PairRequest("ZZZZZZZZ"));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("message").GetString()!;
    }

    [Theory]
    [InlineData(null, "Invalid or expired code")]
    [InlineData("en", "Invalid or expired code")]
    [InlineData("de", "Invalid or expired code")]
    [InlineData("fr", "Code invalide ou expiré")]
    [InlineData("fr-CA", "Code invalide ou expiré")]
    [InlineData("ar-MA", "رمز غير صالح أو منتهي الصلاحية")]
    [InlineData("ar;q=0.9,en;q=0.8", "رمز غير صالح أو منتهي الصلاحية")]
    public async Task PairingError_IsTranslatedFromAcceptLanguage(string? header, string expected) =>
        (await PairWithBadCodeAsync(header)).Should().Be(expected);

    [Fact]
    public async Task NoFrenchMessage_WhenEnglishRequested()
    {
        _factory.Services.GetRequiredService<IPinRateLimiterService>().ResetAttempts("unknown-client");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "en");
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("0000"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorMessage").GetString().Should().Be("Invalid PIN or credentials");
    }

    [Fact]
    public async Task AuthError_InFrench_WhenFrenchRequested()
    {
        _factory.Services.GetRequiredService<IPinRateLimiterService>().ResetAttempts("unknown-client");
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Accept-Language", "fr");
        var response = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("0000"));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("errorMessage").GetString().Should().Be("Code PIN ou identifiants incorrects");
    }

    [Fact]
    public void AcceptLanguage_Ar_DoesNotChangeCurrentCulture()
    {
        var options = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value;
        options.SupportedCultures!.Select(c => c.Name).Should().Equal("en");
        options.SupportedUICultures!.Select(c => c.Name).Should().Equal("en", "fr", "ar");
    }
}
