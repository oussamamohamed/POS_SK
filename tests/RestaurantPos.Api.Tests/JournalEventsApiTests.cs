using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class JournalEventsApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly PosApiApplicationFactory _factory;

    public JournalEventsApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Login_SuccessAndFailure_WriteChainedJetEntries()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (!await db.Users.AnyAsync(u => u.Name == "Audit Test Manager"))
            {
                var salt = "testsalt123456";
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(salt + "9988"))).ToLowerInvariant();
                db.Users.Add(new User
                {
                    Id = Guid.NewGuid(),
                    Name = "Audit Test Manager",
                    PinSalt = salt,
                    PinHash = hash,
                    Role = UserRole.FloorManager,
                    IsActive = true
                });
                await db.SaveChangesAsync();
            }
        }

        // Failed login
        var failRes = await _client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("0000"));
        failRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Successful login
        var successRes = await _client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9988"));
        successRes.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entries = await db.JournalEntries
                .Where(j => j.EventType == JournalEventTypes.LoginFailed || j.EventType == JournalEventTypes.LoginSucceeded)
                .OrderBy(j => j.ChainSequence)
                .ToListAsync();

            entries.Should().Contain(e => e.EventType == JournalEventTypes.LoginFailed);
            entries.Should().Contain(e => e.EventType == JournalEventTypes.LoginSucceeded);
            entries.All(e => e.ChainSequence.HasValue).Should().BeTrue();
        }
    }

    [Fact]
    public async Task DevicePairAndRevoke_WriteChainedJetEntries()
    {
        string code;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deviceService = scope.ServiceProvider.GetRequiredService<RestaurantPos.Application.Common.Interfaces.IDeviceService>();
            var pairCode = await deviceService.CreatePairingCodeAsync("Tablette Audit", DeviceRole.Caisse, Guid.NewGuid());
            code = pairCode.Code;
        }

        // Pair device
        var pairRes = await _client.PostAsJsonAsync("/api/devices/pair", new PairRequest(code));
        pairRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paired = await pairRes.Content.ReadFromJsonAsync<PairResponse>();
        paired.Should().NotBeNull();

        // Revoke device as Admin
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        var revokeRes = await adminClient.PostAsync($"/api/devices/{paired!.DeviceId}/revoke", null);
        revokeRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var pairEntry = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.DevicePaired);
            var revokeEntry = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.DeviceRevoked);

            pairEntry.Should().NotBeNull();
            pairEntry!.ChainSequence.Should().NotBeNull();

            revokeEntry.Should().NotBeNull();
            revokeEntry!.ChainSequence.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Journal_GetPaginated_ReturnsEntries()
    {
        var adminClient = _factory.CreateClient();
        adminClient.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        var res = await adminClient.GetAsync("/api/fiscal/journal?pageSize=10");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var doc = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonDocument>();
        doc.Should().NotBeNull();
        doc!.RootElement.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
    }

    [Fact]
    public async Task UpdateProduct_WhenTaxRateChanges_WritesJournalEntry()
    {
        Guid prodId;
        using (var scope = _factory.Services.CreateScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<RestaurantPos.Application.Common.Interfaces.IBackOfficeCatalogService>();
            var cat = await catalog.CreateCategoryAsync("Test Cat", null, 1, null);
            var prod = await catalog.CreateProductAsync("Test TVA Item", cat.Id, 10.0m, 10.0m, null, null, 1, false, null);
            prodId = prod.Id;

            // Change tax rate from 10.0m to 20.0m
            await catalog.UpdateProductAsync(prodId, prod.Name, cat.Id, 10.0m, 20.0m, null, null, 1, true, true, false, null);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db.JournalEntries
                .OrderByDescending(j => j.OccurredAtUtc)
                .FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.TaxRateChanged);

            entry.Should().NotBeNull();
            entry!.ChainSequence.Should().NotBeNull();
            entry.PayloadJson.Should().Contain("10");
            entry.PayloadJson.Should().Contain("20");
        }
    }
}
