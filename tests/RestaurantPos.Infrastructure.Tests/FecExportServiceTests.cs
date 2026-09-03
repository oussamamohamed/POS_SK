using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class FecExportServiceTests
{
    [Fact]
    public async Task GenerateFecAsync_ProducesValid18FieldHeaderAndNomenclature()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "FecDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var fecService = new FecExportService(dbContext);

        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var siren = "123456789";

        // Act
        var result = await fecService.GenerateFecAsync(new FecExportRequest(from, to, siren));

        // Assert
        result.FileName.Should().Be("123456789FEC20261231.txt");

        using var reader = new StringReader(Encoding.UTF8.GetString(result.FileBytes));
        var headerLine = await reader.ReadLineAsync();

        headerLine.Should().NotBeNull();
        var headers = headerLine!.Split('\t');
        headers.Length.Should().Be(18);
        headers[0].Should().Be("JournalCode");
        headers[1].Should().Be("JournalLib");
        headers[2].Should().Be("EcritureNum");
        headers[3].Should().Be("EcritureDate");
        headers[4].Should().Be("CompteNum");
        headers[5].Should().Be("CompteLib");
        headers[8].Should().Be("PieceRef");
        headers[11].Should().Be("Debit");
        headers[12].Should().Be("Credit");
        headers[17].Should().Be("Idevise");
    }

    [Fact]
    public async Task GenerateFecAsync_BalancesDebitAndCreditExactly()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "FecDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "T-20260903-0001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(3850), // 38.50 €
            TotalHtAmount = Money.FromCents(3500),  // 35.00 €
            TaxBreakdownJson = "{\"10.0\": 350}",
            CreatedAtUtc = now
        };

        receipt.Tenders.Add(new PaymentTender
        {
            FiscalReceiptId = receipt.Id,
            Method = PaymentMethod.CreditCard,
            Amount = Money.FromCents(2000), // 20.00 €
            Tendered = Money.FromCents(2000)
        });

        receipt.Tenders.Add(new PaymentTender
        {
            FiscalReceiptId = receipt.Id,
            Method = PaymentMethod.Cash,
            Amount = Money.FromCents(1850), // 18.50 €
            Tendered = Money.FromCents(2000),
            ChangeGiven = Money.FromCents(150)
        });

        dbContext.FiscalReceipts.Add(receipt);
        await dbContext.SaveChangesAsync();

        var fecService = new FecExportService(dbContext);

        // Act
        var result = await fecService.GenerateFecAsync(new FecExportRequest(
            now.AddDays(-1),
            now.AddDays(1),
            "987654321"
        ));

        // Assert
        result.TotalDebit.Should().Be(38.50m);
        result.TotalCredit.Should().Be(38.50m);
        result.TotalDebit.Should().Be(result.TotalCredit);

        using var reader = new StringReader(Encoding.UTF8.GetString(result.FileBytes));
        var header = await reader.ReadLineAsync();

        var lines = new List<string[]>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lines.Add(line.Split('\t'));
        }

        // 1 HT (706100), 1 TVA (445710), 2 Tenders (512000, 530000) = 4 lines
        lines.Count.Should().Be(4);

        // Verify HT line (Credit = 35.00, Debit = 0.00)
        var htLine = lines.First(l => l[4] == "706100");
        htLine[12].Should().Be("35.00");
        htLine[11].Should().Be("0.00");

        // Verify VAT line (Credit = 3.50, Debit = 0.00)
        var vatLine = lines.First(l => l[4] == "445710");
        vatLine[12].Should().Be("3.50");
        vatLine[11].Should().Be("0.00");

        // Verify CreditCard line (Debit = 20.00, Credit = 0.00)
        var cbLine = lines.First(l => l[4] == "512000");
        cbLine[11].Should().Be("20.00");
        cbLine[12].Should().Be("0.00");

        // Verify Cash line (Debit = 18.50, Credit = 0.00)
        var cashLine = lines.First(l => l[4] == "530000");
        cashLine[11].Should().Be("18.50");
        cashLine[12].Should().Be("0.00");
    }

    [Fact]
    public async Task GenerateFecAsync_InvertsDebitsAndCreditsForVoidedReceipt()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "FecDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var now = new DateTimeOffset(2026, 9, 3, 14, 0, 0, TimeSpan.Zero);

        var voidReceipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "AVOIR-20260903-0001",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(1100), // 11.00 €
            TotalHtAmount = Money.FromCents(1000),  // 10.00 €
            TaxBreakdownJson = "{\"10.0\": 100}",
            CreatedAtUtc = now,
            IsVoid = true
        };

        voidReceipt.Tenders.Add(new PaymentTender
        {
            FiscalReceiptId = voidReceipt.Id,
            Method = PaymentMethod.CreditCard,
            Amount = Money.FromCents(1100),
            Tendered = Money.FromCents(1100)
        });

        dbContext.FiscalReceipts.Add(voidReceipt);
        await dbContext.SaveChangesAsync();

        var fecService = new FecExportService(dbContext);

        // Act
        var result = await fecService.GenerateFecAsync(new FecExportRequest(
            now.AddDays(-1),
            now.AddDays(1),
            "112233445"
        ));

        // Assert
        result.TotalDebit.Should().Be(11.00m);
        result.TotalCredit.Should().Be(11.00m);

        using var reader = new StringReader(Encoding.UTF8.GetString(result.FileBytes));
        await reader.ReadLineAsync(); // Skip header

        var lines = new List<string[]>();
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            lines.Add(line.Split('\t'));
        }

        // For void: HT is debited (10.00), VAT is debited (1.00), Bank is credited (11.00)
        var htLine = lines.First(l => l[4] == "706100");
        htLine[11].Should().Be("10.00"); // Debit
        htLine[12].Should().Be("0.00");  // Credit

        var vatLine = lines.First(l => l[4] == "445710");
        vatLine[11].Should().Be("1.00"); // Debit
        vatLine[12].Should().Be("0.00"); // Credit

        var cbLine = lines.First(l => l[4] == "512000");
        cbLine[11].Should().Be("0.00");  // Debit
        cbLine[12].Should().Be("11.00"); // Credit
    }
}
