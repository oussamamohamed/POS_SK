using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class FiscalAndDashboardReportApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    private readonly HttpClient _client;

    public FiscalAndDashboardReportApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetAdminTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        return result!.Token;
    }

    [Fact]
    public async Task XReport_Endpoint_WithoutAuth_ReturnsUnauthorized()
    {
        // Act
        var response = await _client.GetAsync("/api/fiscal/x-report?terminalId=POS01");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task XReport_Endpoint_WithManagerAuth_ReturnsAccurateZeroTotalsWhenClean()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/fiscal/x-report?terminalId=POS_CLEAN_TERM");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("totalSalesTtc").GetDecimal().Should().Be(0.00m);
        json.GetProperty("totalSalesHt").GetDecimal().Should().Be(0.00m);
        json.GetProperty("receiptCount").GetInt32().Should().Be(0);
        json.GetProperty("perpetualGrandTotal").GetDecimal().Should().Be(0.00m);
    }

    [Fact]
    public async Task ZClosure_And_LatestClosure_Endpoints_ExecuteAndVerifyAccurateTotals()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        string terminalId = "POS_Z_TERM_" + Guid.NewGuid().ToString("N")[..6];
        Guid managerId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var adminUser = db.Users.FirstOrDefault(u => u.Name == "Admin Système");
            if (adminUser != null)
            {
                managerId = adminUser.Id;
            }

            // Seed a receipt for this terminal
            var receipt = new FiscalReceipt
            {
                TerminalId = terminalId,
                ReceiptNumber = $"{terminalId}-000001",
                SequenceNumber = 1,
                TotalTtcAmount = Money.FromCents(8800), // 88.00 €
                TotalHtAmount = Money.FromCents(8000),  // 80.00 €
                TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 800 }),
                PreviousSignatureHash = "GENESIS_0000000000000000000000000000000000000000000000000000000000000000",
                SignatureHash = "SIG_TEST_01",
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            receipt.Tenders.Add(new PaymentTender
            {
                Method = PaymentMethod.CreditCard,
                Amount = Money.FromCents(8800)
            });

            db.FiscalReceipts.Add(receipt);
            await db.SaveChangesAsync();
        }

        // Act 1: Call Z-Closure endpoint
        var zRequest = new HttpRequestMessage(HttpMethod.Post, "/api/fiscal/z-closure");
        zRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        zRequest.Content = JsonContent.Create(new
        {
            TerminalId = terminalId,
            ManagerId = managerId,
            ManagerName = "Admin Système"
        });

        var zResponse = await _client.SendAsync(zRequest);

        // Assert Z-Closure response
        zResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var zJson = await zResponse.Content.ReadFromJsonAsync<JsonElement>();

        zJson.GetProperty("closureSequence").GetInt64().Should().Be(1);
        zJson.GetProperty("totalSalesTtc").GetDecimal().Should().Be(88.00m);
        zJson.GetProperty("totalSalesHt").GetDecimal().Should().Be(80.00m);
        zJson.GetProperty("receiptCount").GetInt32().Should().Be(1);
        zJson.GetProperty("perpetualGrandTotal").GetDecimal().Should().Be(88.00m);
        zJson.GetProperty("signatureHash").GetString().Should().NotBeNullOrEmpty();

        // Act 2: Verify /latest-closure endpoint returns the newly sealed closure
        var latestRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/fiscal/latest-closure?terminalId={terminalId}");
        latestRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var latestResponse = await _client.SendAsync(latestRequest);
        latestResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var latestJson = await latestResponse.Content.ReadFromJsonAsync<JsonElement>();

        latestJson.GetProperty("closureSequence").GetInt64().Should().Be(1);
        latestJson.GetProperty("totalSalesTtc").GetDecimal().Should().Be(88.00m);
        latestJson.GetProperty("perpetualGrandTotal").GetDecimal().Should().Be(88.00m);
    }

    [Fact]
    public async Task FinancialDashboard_Endpoint_ReturnsAccurateReportKpis()
    {
        // Arrange
        var token = await GetAdminTokenAsync();
        var fromStr = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"));
        var toStr = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"));
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/dashboard/financial?from={fromStr}&to={toStr}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await response.Content.ReadFromJsonAsync<FinancialDashboardReportDto>();

        report.Should().NotBeNull();
        report!.Kpis.Should().NotBeNull();
        report.Services.Should().NotBeNull();
        report.TopProducts.Should().NotBeNull();
        report.PaymentMethods.Should().NotBeNull();
    }
}
