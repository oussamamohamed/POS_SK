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
            VoidedReceiptId = Guid.NewGuid()
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
    // ---------- Défauts de production du FEC (journée simulée) ----------

    private sealed record Row(string Num, string Account, long Debit, long Credit);

    private static Task<List<Row>> FecRowsAsync(
        long ttc, long ht, string taxJson, bool isVoid, params (PaymentMethod Method, long Amount)[] tenders) =>
        FecRowsAsync(ttc, ht, taxJson, null, isVoid, tenders);

    private static async Task<List<Row>> FecRowsAsync(
        long ttc, long ht, string taxJson, string? htJson, bool isVoid, params (PaymentMethod Method, long Amount)[] tenders)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "FecDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        using var dbContext = new AppDbContext(options);
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        long sign = isVoid ? -1 : 1;

        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = isVoid ? "POS01-VOID-000002" : "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(sign * ttc),
            TotalHtAmount = Money.FromCents(sign * ht),
            TaxBreakdownJson = taxJson,
            HtBreakdownJson = htJson,
            CreatedAtUtc = now,
            VoidedReceiptId = isVoid ? Guid.NewGuid() : null
        };
        foreach (var (method, amount) in tenders)
        {
            receipt.Tenders.Add(new PaymentTender
            {
                FiscalReceiptId = receipt.Id,
                Method = method,
                Amount = Money.FromCents(sign * amount),
                Tendered = Money.FromCents(sign * amount)
            });
        }

        dbContext.FiscalReceipts.Add(receipt);
        await dbContext.SaveChangesAsync();

        var result = await new FecExportService(dbContext).GenerateFecAsync(
            new FecExportRequest(now.AddDays(-1), now.AddDays(1), "123456789"));

        return Encoding.UTF8.GetString(result.FileBytes)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1)
            .Select(l => l.Split('\t'))
            .Select(c => new Row(c[2], c[4], Cents(c[11]), Cents(c[12])))
            .ToList();
    }

    private static long Cents(string amount) => (long)Math.Round(decimal.Parse(amount, CultureInfo.InvariantCulture) * 100m);

    private static void ShouldBalancePerEntry(List<Row> rows) =>
        rows.GroupBy(r => r.Num).Should().OnlyContain(g => g.Sum(r => r.Debit) == g.Sum(r => r.Credit));

    [Fact]
    public async Task VoidReceiptAtOneRate_WritesNegativeHtAsDebit()
    {
        var rows = await FecRowsAsync(1100, 1000, "{\"10.0\": -100}", true, (PaymentMethod.CreditCard, 1100));

        rows.Single(r => r.Account == "706100").Debit.Should().Be(1000);
        rows.Single(r => r.Account == "445710").Debit.Should().Be(100);
        rows.Single(r => r.Account == "512000").Credit.Should().Be(1100);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task VoidReceiptAtSeveralRates_KeepsEachHt()
    {
        // 10 % : TTC 11,00 (HT 10,00 + TVA 1,00) ; 20 % : TTC 12,00 (HT 10,00 + TVA 2,00)
        var rows = await FecRowsAsync(2300, 2000, "{\"10.0\": -100, \"20.0\": -200}", true, (PaymentMethod.Cash, 2300));

        rows.Single(r => r.Account == "706100").Debit.Should().Be(1000);
        rows.Single(r => r.Account == "706200").Debit.Should().Be(1000);
        rows.Single(r => r.Account == "445710").Debit.Should().Be(100);
        rows.Single(r => r.Account == "445720").Debit.Should().Be(200);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task ZeroRateGroup_KeepsItsHtOnItsOwnAccount()
    {
        // 0 % : TTC 5,00 (HT 5,00) ; 20 % : TTC 12,00 (HT 10,00 + TVA 2,00)
        var rows = await FecRowsAsync(1700, 1500, "{\"0\": 0, \"20\": 200}", false, (PaymentMethod.CreditCard, 1700));

        rows.Single(r => r.Account == "706000").Credit.Should().Be(500);
        rows.Single(r => r.Account == "706200").Credit.Should().Be(1000);
        rows.Single(r => r.Account == "445720").Credit.Should().Be(200);
        ShouldBalancePerEntry(rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TipIsCreditedToTipsAccount_SoEntryBalances(bool isVoid)
    {
        // TTC 11,00 ; règlement 13,00 : 2,00 de pourboire
        var rows = await FecRowsAsync(1100, 1000, "{\"10.0\": " + (isVoid ? "-100" : "100") + "}", isVoid, (PaymentMethod.CreditCard, 1300));

        var tip = rows.Single(r => r.Account == "468600");
        (isVoid ? tip.Debit : tip.Credit).Should().Be(200);
        (rows.Single(r => r.Account == "706100").Debit + rows.Single(r => r.Account == "706100").Credit).Should().Be(1000);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task ReducedRate21_RoundingStaysOnOneLine_AndHtTotalIsExact()
    {
        // 2,1 % : TTC 10,00 -> HT 9,79 + TVA 0,21 ; 10 % : TTC 11,00 -> HT 10,00 + TVA 1,00
        var rows = await FecRowsAsync(2100, 1979, "{\"2.1\": 21, \"10\": 100}", false, (PaymentMethod.Cash, 2100));

        rows.Where(r => r.Account.StartsWith("706", StringComparison.Ordinal)).Sum(r => r.Credit).Should().Be(1979);
        rows.Single(r => r.Account == "706100").Credit.Should().Be(1000);
        rows.Single(r => r.Account == "706000").Credit.Should().Be(979);
        rows.Single(r => r.Account == "445700").Credit.Should().Be(21);
        ShouldBalancePerEntry(rows);
    }

    // ---------- HT par taux persisté (HtBreakdownJson) ----------

    // 2,1 % : TTC 10,00 -> HT 9,79 + TVA 0,21 ; 5,5 % : TTC 10,00 -> HT 9,48 + TVA 0,52. Sans HT persisté, la déduction depuis la TVA
    // attribue 9,45 au taux 5,5 % (valeur cohérente avec 0,52 la plus proche de TVA / taux) et 9,82 au 2,1 %.
    private const string TwoLowRatesVat = "{\"2.1\": 21, \"5.5\": 52}";

    [Fact]
    public async Task HtBreakdownJson_GivesTheExactHtPerRateAccount()
    {
        var rows = await FecRowsAsync(2000, 1927, TwoLowRatesVat, "{\"2.1\":979,\"5.5\":948}", false, (PaymentMethod.CreditCard, 2000));

        rows.Single(r => r.Account == "706000").Credit.Should().Be(979);
        rows.Single(r => r.Account == "706550").Credit.Should().Be(948);
        rows.Single(r => r.Account == "445700").Credit.Should().Be(21);
        rows.Single(r => r.Account == "445755").Credit.Should().Be(52);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task HtBreakdownJson_OnAVoidReceipt_IsDebitedPerRate()
    {
        var rows = await FecRowsAsync(2000, 1927, "{\"2.1\": -21, \"5.5\": -52}", "{\"2.1\":-979,\"5.5\":-948}", true, (PaymentMethod.CreditCard, 2000));

        rows.Single(r => r.Account == "706000").Debit.Should().Be(979);
        rows.Single(r => r.Account == "706550").Debit.Should().Be(948);
        rows.Single(r => r.Account == "445755").Debit.Should().Be(52);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task HtBreakdownJson_KeepsAZeroRateGroupExact()
    {
        var rows = await FecRowsAsync(1700, 1500, "{\"0\": 0, \"20\": 200}", "{\"0\":500,\"20\":1000}", false, (PaymentMethod.CreditCard, 1700));

        rows.Single(r => r.Account == "706000").Credit.Should().Be(500);
        rows.Single(r => r.Account == "706200").Credit.Should().Be(1000);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task ReceiptWithoutHtBreakdownJson_KeepsTheLegacyDeduction()
    {
        var rows = await FecRowsAsync(2000, 1927, TwoLowRatesVat, false, (PaymentMethod.CreditCard, 2000));

        rows.Where(r => r.Account.StartsWith("706", StringComparison.Ordinal)).Sum(r => r.Credit).Should().Be(1927);
        rows.Single(r => r.Account == "706550").Credit.Should().Be(945);
        rows.Single(r => r.Account == "706000").Credit.Should().Be(982);
        ShouldBalancePerEntry(rows);
    }

    [Fact]
    public async Task UnreadableHtBreakdownJson_FallsBackToTheLegacyDeduction()
    {
        var rows = await FecRowsAsync(2000, 1927, TwoLowRatesVat, "pas du json", false, (PaymentMethod.CreditCard, 2000));

        rows.Single(r => r.Account == "706550").Credit.Should().Be(945);
        rows.Single(r => r.Account == "706000").Credit.Should().Be(982);
    }

    [Theory]
    [InlineData("{\"2.1\":1500,\"5.5\":427}")] // somme conservée, ventilation hors plage faisable
    [InlineData("{\"2.1\":979,\"5.5\":900}")] // somme fausse
    [InlineData("{\"2.1\":979,\"10\":948}")] // clés différentes de la TVA signée
    public async Task IncoherentHtBreakdownJson_FallsBackToTheLegacyDeduction(string htJson)
    {
        var rows = await FecRowsAsync(2000, 1927, TwoLowRatesVat, htJson, false, (PaymentMethod.CreditCard, 2000));

        rows.Where(r => r.Account.StartsWith("706", StringComparison.Ordinal)).Sum(r => r.Credit).Should().Be(1927);
        rows.Single(r => r.Account == "706550").Credit.Should().Be(945);
        rows.Single(r => r.Account == "706000").Credit.Should().Be(982);
        ShouldBalancePerEntry(rows);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReceiptWithoutTenders_SettlesToCashWithAbsoluteAmount(bool isVoid)
    {
        var rows = await FecRowsAsync(1100, 1000, "{\"10.0\": " + (isVoid ? "-100" : "100") + "}", isVoid);

        var cash = rows.Single(r => r.Account == "530000");
        (isVoid ? cash.Credit : cash.Debit).Should().Be(1100);
        (isVoid ? cash.Debit : cash.Credit).Should().Be(0);
        ShouldBalancePerEntry(rows);
    }
}
