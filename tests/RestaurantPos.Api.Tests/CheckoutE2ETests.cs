using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class CheckoutE2ETests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutE2ETests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetAuthTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        return result!.Token;
    }

    [Fact]
    public async Task VoidReceipt_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Guid receiptId = Guid.NewGuid();
        Guid operatorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operatorUser = db.Users.FirstOrDefault(u => u.Name == "Admin Système");
            if (operatorUser != null)
            {
                operatorId = operatorUser.Id;
            }

            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = receiptId,
                ReceiptNumber = "REC-001",
                OrderId = Guid.NewGuid(),
                TerminalId = "TERM-1",
                TotalTtcAmount = Money.FromCents(1500, "EUR"),
                SignatureHash = "signature-test",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var request = new VoidReceiptRequest
        {
            TerminalId = "TERM-1",
            OperatorId = operatorId
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var originalReceipt = await db.FiscalReceipts.FindAsync(receiptId);
            originalReceipt.Should().NotBeNull();
            originalReceipt!.IsVoid.Should().BeTrue();

            var voidReceipt = db.FiscalReceipts.FirstOrDefault(r => r.VoidedReceiptId == receiptId);
            voidReceipt.Should().NotBeNull();
            voidReceipt!.TotalTtcAmount.ToDecimal().Should().Be(-15m); // Negative amount for void
        }
    }
}
