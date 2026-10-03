using System;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class ChainVerificationApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public ChainVerificationApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostVerify_WithoutManagerRole_ReturnsForbiddenOrUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsync("/api/fiscal/verify", null);
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostVerify_WhenValid_ReturnsContractFormat()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        var response = await client.PostAsync("/api/fiscal/verify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.TryGetProperty("isValid", out var isValid).Should().BeTrue();
        json.TryGetProperty("checkedAtUtc", out var checkedAt).Should().BeTrue();
        json.TryGetProperty("chains", out var chains).Should().BeTrue();
        chains.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task PostVerify_WhenBreakDetected_ReturnsInvalidAndWritesJetEvent()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Corrupt or insert broken receipt with a sequence gap on a dedicated terminal
        var terminal = "BREAK_TERM_" + Guid.NewGuid().ToString("N")[..8];
        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = terminal,
            ReceiptNumber = $"{terminal}-000005",
            SequenceNumber = 5, // Gap: starts at 5 instead of 1
            TotalTtcAmount = RestaurantPos.Domain.ValueObjects.Money.FromCents(1000),
            TotalHtAmount = RestaurantPos.Domain.ValueObjects.Money.FromCents(900),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = "some_prev",
            SignatureHash = "some_sig",
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");

        var response = await client.PostAsync("/api/fiscal/verify", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("isValid").GetBoolean().Should().BeFalse();

        // Verify JET has recorded CHAIN_BREAK_DETECTED
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var jetBreak = await verifyDb.JournalEntries
            .FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.ChainBreakDetected && j.TerminalId == terminal);
        jetBreak.Should().NotBeNull();
    }
}

