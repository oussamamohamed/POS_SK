using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Domain.Entities;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class SyncEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public SyncEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetNetworkInfo_ReturnsServerInfo()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/network/info");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetString().Should().Be("Online");
        body.GetProperty("serverName").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("port").GetInt32().Should().Be(5000);
    }

    [Fact]
    public async Task SyncBatchAndStatus_StoresMessagesIdempotentlyAndReportsCount()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");
        var msgId1 = Guid.NewGuid();
        var msgId2 = Guid.NewGuid();

        var batchReq = new SyncBatchRequest(
        [
            new SyncMessageItemDto(msgId1, "T01", "OrderCreated", "KEY1", "{}", DateTimeOffset.UtcNow),
            new SyncMessageItemDto(msgId2, "T01", "OrderUpdated", "KEY2", "{}", DateTimeOffset.UtcNow)
        ]);

        var batchResp = await client.PostAsJsonAsync("/api/sync/batch", batchReq);
        batchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var batchBody = await batchResp.Content.ReadFromJsonAsync<JsonElement>();
        batchBody.GetProperty("success").GetBoolean().Should().BeTrue();
        batchBody.GetProperty("processedMessagesCount").GetInt32().Should().Be(2);

        // Resending same batch should be idempotent (0 newly processed)
        var replayResp = await client.PostAsJsonAsync("/api/sync/batch", batchReq);
        replayResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var replayBody = await replayResp.Content.ReadFromJsonAsync<JsonElement>();
        replayBody.GetProperty("processedMessagesCount").GetInt32().Should().Be(0);

        // GET /api/sync/status
        var statusResp = await client.GetAsync("/api/sync/status");
        statusResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusBody = await statusResp.Content.ReadFromJsonAsync<JsonElement>();
        statusBody.GetProperty("status").GetString().Should().Be("Synchronized");
        statusBody.GetProperty("totalMessages").GetInt32().Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task SyncFiscalXReport_ReturnsValidSummary()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var response = await client.GetAsync("/api/sync/fiscal/x-report?terminalId=T01");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("terminalId").GetString().Should().Be("T01");
        body.TryGetProperty("totalSalesTtc", out _).Should().BeTrue();
        body.TryGetProperty("vatBreakdown", out _).Should().BeTrue();
    }

    [Fact]
    public async Task SyncFiscalXReport_NonManager_ReturnsForbidden()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");

        var response = await client.GetAsync("/api/sync/fiscal/x-report?terminalId=T01");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SyncBatch_Anonymous_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        var batchReq = new SyncBatchRequest([]);

        var response = await client.PostAsJsonAsync("/api/sync/batch", batchReq);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncReceipt_WithoutPairedDevice_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");

        var syncReq = new SyncReceiptDto(
            Guid.NewGuid(),
            "Comptoir",
            1000,
            [new SyncTenderDto(PaymentMethod.Cash, 1000, 1000)],
            "T01"
        );

        var response = await client.PostAsJsonAsync("/api/sync/receipt", syncReq);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncReceipt_WithPairedDevice_ProcessesPaymentAndReturnsReceiptNumber()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "iPad Sync Test");

        var syncReq = new SyncReceiptDto(
            Guid.NewGuid(),
            "Comptoir",
            1500,
            [new SyncTenderDto(PaymentMethod.Cash, 1500, 2000)],
            paired.TerminalId
        );

        var response = await client.PostAsJsonAsync("/api/sync/receipt", syncReq);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("receiptNumber").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("fiscalSignature").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("totalPaid").GetDecimal().Should().Be(15.00m);
        body.GetProperty("changeGiven").GetDecimal().Should().Be(5.00m);
    }
}
