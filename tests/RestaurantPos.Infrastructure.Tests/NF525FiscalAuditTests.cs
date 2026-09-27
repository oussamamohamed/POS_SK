using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class NF525FiscalAuditTests
{
    [Fact]
    public async Task ValidateAuditChainIntegrityAsyncWithValidReceiptsReturnsTrue()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "NF525ValidDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var auditService = new NF525FiscalAuditService(dbContext);

        string prevHash = NF525FiscalAuditService.GenesisHash;
        string terminalId = "POS01";

        for (int seq = 1; seq <= 3; seq++)
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(seq);
            long amountCents = seq * 2500;
            string taxJson = "{\"10\": 250}";

            string sig = auditService.ComputeReceiptHashSignature(
                prevHash,
                terminalId,
                seq,
                amountCents,
                now,
                taxJson
            );

            var receipt = new FiscalReceipt
            {
                TerminalId = terminalId,
                ReceiptNumber = $"{terminalId}-{seq:D6}",
                SequenceNumber = seq,
                TotalTtcAmount = Money.FromCents(amountCents),
                TotalHtAmount = Money.FromCents(amountCents - 250),
                TaxBreakdownJson = taxJson,
                PreviousSignatureHash = prevHash,
                SignatureHash = sig,
                CreatedAtUtc = now
            };

            dbContext.FiscalReceipts.Add(receipt);
            prevHash = sig;
        }

        await dbContext.SaveChangesAsync();

        // Act
        var result = await auditService.ValidateAuditChainIntegrityAsync(terminalId);

        // Assert
        result.IsChainValid.Should().BeTrue();
        result.TotalRecordsVerified.Should().Be(3);
        result.BrokenLinkReceiptNumber.Should().BeNull();
    }

    [Fact]
    public async Task ValidateAuditChainIntegrityAsyncWithTamperedReceiptDetectsBrokenLink()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "NF525TamperedDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var auditService = new NF525FiscalAuditService(dbContext);

        string prevHash = NF525FiscalAuditService.GenesisHash;
        string terminalId = "POS01";

        for (int seq = 1; seq <= 2; seq++)
        {
            var now = DateTimeOffset.UtcNow.AddMinutes(seq);
            long amountCents = 3000;
            string taxJson = "{\"10\": 300}";

            string sig = auditService.ComputeReceiptHashSignature(
                prevHash,
                terminalId,
                seq,
                amountCents,
                now,
                taxJson
            );

            var receipt = new FiscalReceipt
            {
                TerminalId = terminalId,
                ReceiptNumber = $"{terminalId}-{seq:D6}",
                SequenceNumber = seq,
                TotalTtcAmount = Money.FromCents(amountCents),
                TotalHtAmount = Money.FromCents(amountCents - 300),
                TaxBreakdownJson = taxJson,
                PreviousSignatureHash = prevHash,
                SignatureHash = sig,
                CreatedAtUtc = now
            };

            dbContext.FiscalReceipts.Add(receipt);
            prevHash = sig;
        }

        await dbContext.SaveChangesAsync();

        // Simulate tampering: manually alter amount of receipt #1
        var firstReceipt = await dbContext.FiscalReceipts.FirstAsync(r => r.SequenceNumber == 1);
        firstReceipt.TotalTtcAmount = Money.FromCents(1000); // Altered!
        await dbContext.SaveChangesAsync();

        // Act
        var result = await auditService.ValidateAuditChainIntegrityAsync(terminalId);

        // Assert
        result.IsChainValid.Should().BeFalse();
        result.BrokenLinkReceiptNumber.Should().Be("POS01-000001");
        result.ErrorDetails.Should().Contain("Signature invalide");
    }

    [Fact]
    public async Task ExecuteDailyZClosureAsyncSealsPeriodAndIncrementsPerpetualGrandTotal()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "NF525ZClosureDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var auditService = new NF525FiscalAuditService(dbContext);

        var now = DateTimeOffset.UtcNow;
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromDecimal(100.00m),
            TotalHtAmount = Money.FromDecimal(90.00m),
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG1",
            CreatedAtUtc = now
        };
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromDecimal(100.00m) });

        dbContext.FiscalReceipts.Add(r1);
        await dbContext.SaveChangesAsync();

        // Act
        var closure = await auditService.ExecuteDailyZClosureAsync("POS01", Guid.NewGuid(), "Admin");

        // Assert
        closure.Should().NotBeNull();
        closure.ClosureSequence.Should().Be(1);
        closure.TotalSalesTtcCents.Should().Be(10000); // 100.00 EUR
        closure.PerpetualGrandTotalCents.Should().Be(10000);
        closure.SignatureHash.Should().NotBeNullOrEmpty();
    }
}
