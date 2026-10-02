using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PeriodClosureApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public PeriodClosureApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PostPeriodClosure_WithoutAuth_ReturnsForbiddenOrUnauthorized()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = "POS_MAIN_TERM",
            periodType = "monthly",
            periodKey = "2026-09"
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostPeriodClosure_WithInvalidPeriodType_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = "POS_MAIN_TERM",
            periodType = "invalid_type",
            periodKey = "2026-09"
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("monthly", "2025-13")]
    [InlineData("monthly", "abc")]
    [InlineData("annual", "0")]
    [InlineData("annual", "99999")]
    public async Task PostPeriodClosure_WithInvalidPeriodKey_ReturnsBadRequest(string periodType, string periodKey)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = "POS_MAIN_TERM",
            periodType,
            periodKey
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostPeriodClosure_WhenPeriodNotEnded_Returns409Conflict()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // Period 2099-12 has not ended
        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = "POS_MAIN_TERM",
            periodType = "monthly",
            periodKey = "2099-12"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("period_not_ended");
    }

    [Fact]
    public async Task PostPeriodClosure_WhenMissingDailyClosures_Returns409WithDays()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var terminalId = "TEST_TERM_" + Guid.NewGuid().ToString("N")[..8];

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Add receipt in January 2020 (past month) without any Z closure
            db.FiscalReceipts.Add(new FiscalReceipt
            {
                TerminalId = terminalId,
                ReceiptNumber = $"{terminalId}-000001",
                SequenceNumber = 1,
                TotalTtcAmount = Money.FromCents(1500),
                TotalHtAmount = Money.FromCents(1364),
                CreatedAtUtc = new DateTimeOffset(2020, 1, 15, 12, 0, 0, TimeSpan.Zero),
                TaxBreakdownJson = "{}",
                SignatureHash = "SIG"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = terminalId,
            periodType = "monthly",
            periodKey = "2020-01"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("missing_daily_closures");
        json.TryGetProperty("days", out var days).Should().BeTrue();
        days.ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task PostPeriodClosure_WhenValid_Returns200AndCanBeFetchedViaGet()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var terminalId = "TEST_TERM_" + Guid.NewGuid().ToString("N")[..8];

        // Close empty past month: February 2020
        var response = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = terminalId,
            periodType = "monthly",
            periodKey = "2020-02"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("terminalId").GetString().Should().Be(terminalId);
        json.GetProperty("periodType").GetString().Should().Be("monthly");
        json.GetProperty("periodKey").GetString().Should().Be("2020-02");
        json.GetProperty("closureSequence").GetInt64().Should().Be(1);
        json.TryGetProperty("printQueued", out _).Should().BeTrue();

        // Second call for the same period should return 409 period_already_closed
        var duplicate = await client.PostAsJsonAsync("/api/fiscal/period-closures", new
        {
            terminalId = terminalId,
            periodType = "monthly",
            periodKey = "2020-02"
        });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupJson = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        dupJson.GetProperty("code").GetString().Should().Be("period_already_closed");

        // GET /api/fiscal/period-closures?terminalId=...
        var getResponse = await client.GetAsync($"/api/fiscal/period-closures?terminalId={terminalId}&periodType=monthly");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await getResponse.Content.ReadFromJsonAsync<List<JsonElement>>();
        list.Should().NotBeNull();
        list!.Should().ContainSingle();
        list[0].GetProperty("periodKey").GetString().Should().Be("2020-02");
    }
}
