using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>Début de période d'un Z : fin du dernier Z (de tout type), sinon premier reçu du poste, sinon la date du Z.</summary>
public class ZPeriodStartTests
{
    private const string Terminal = "T01";

    private static (NF525FiscalAuditService Fiscal, AppDbContext Db) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("ZStart_" + Guid.NewGuid().ToString("N")).Options);
        return (new NF525FiscalAuditService(db, new FiscalJournalService(db)), db);
    }

    private static void AddReceipt(AppDbContext db, long seq, DateTimeOffset at, long ttcCents)
    {
        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = Terminal,
            ReceiptNumber = $"{Terminal}-{seq:000000}",
            SequenceNumber = seq,
            CreatedAtUtc = at,
            TotalTtcAmount = Money.FromCents(ttcCents),
            TotalHtAmount = Money.FromCents(ttcCents),
            TaxBreakdownJson = "{}",
        });
    }

    [Fact]
    public async Task FirstZ_StartsAtEarliestReceipt_NotMinValue()
    {
        var (fiscal, db) = Create();
        var first = new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        AddReceipt(db, 2, first.AddHours(2), 500);
        AddReceipt(db, 1, first, 1000);
        await db.SaveChangesAsync();

        var z = await fiscal.ExecuteDailyZClosureAsync(Terminal, Guid.NewGuid(), "Gérant");

        z.PeriodStartUtc.Should().Be(first);
        z.TotalSalesTtcCents.Should().Be(1500);
        z.ReceiptCount.Should().Be(2);
    }

    [Fact]
    public async Task FirstZ_WithoutReceipts_StartsAtItsOwnDate()
    {
        var (fiscal, _) = Create();

        var z = await fiscal.ExecuteDailyZClosureAsync(Terminal, Guid.NewGuid(), "Gérant");

        z.PeriodStartUtc.Should().Be(z.ClosedAtUtc);
        z.TotalSalesTtcCents.Should().Be(0);
    }

    [Fact]
    public async Task Z_StartsAtEndOfLastClosure_EvenWhenItIsEmpty()
    {
        var (fiscal, db) = Create();
        var z1End = new DateTimeOffset(2026, 1, 31, 22, 0, 0, TimeSpan.Zero);
        var z2End = new DateTimeOffset(2026, 2, 1, 22, 0, 0, TimeSpan.Zero);
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = Terminal, ClosureSequence = 1, PeriodStartUtc = z1End.AddHours(-10), PeriodEndUtc = z1End, TotalSalesTtc = Money.FromCents(900), PerpetualGrandTotalCents = 900, SignatureHash = "h1", PreviousSignatureHash = "g" });
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = Terminal, ClosureSequence = 2, PeriodStartUtc = z1End, PeriodEndUtc = z2End, TotalSalesTtc = Money.FromCents(0), PerpetualGrandTotalCents = 900, SignatureHash = "h2", PreviousSignatureHash = "h1" });
        AddReceipt(db, 1, new DateTimeOffset(2026, 2, 2, 10, 0, 0, TimeSpan.Zero), 700);
        AddReceipt(db, 2, new DateTimeOffset(2026, 2, 2, 11, 0, 0, TimeSpan.Zero), 300);
        await db.SaveChangesAsync();

        var z3 = await fiscal.ExecuteDailyZClosureAsync(Terminal, Guid.NewGuid(), "Gérant");

        z3.PeriodStartUtc.Should().Be(z2End);
        z3.TotalSalesTtcCents.Should().Be(1000);
        z3.ReceiptCount.Should().Be(2);
    }

    [Fact]
    public async Task MonthlyClosure_CountsTheFirstEverZ()
    {
        var (fiscal, db) = Create();
        AddReceipt(db, 1, new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), 1000);
        AddReceipt(db, 2, new DateTimeOffset(2026, 9, 10, 13, 0, 0, TimeSpan.Zero), 500);
        await db.SaveChangesAsync();
        await fiscal.ExecuteDailyZClosureAsync(Terminal, Guid.NewGuid(), "Gérant");

        var monthly = await fiscal.ExecutePeriodClosureAsync(
            Terminal, FiscalPeriodType.Monthly, "2026-09", Guid.NewGuid(), "Gérant",
            utcNow: new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero));

        monthly.DailyClosureCount.Should().Be(1);
        monthly.TotalTtcCents.Should().Be(1500);
    }
}
