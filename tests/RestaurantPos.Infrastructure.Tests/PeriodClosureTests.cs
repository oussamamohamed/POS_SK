using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PeriodClosureTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task MonthlyClosure_SumsDailyClosures_ExactToTheCent()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var auditService = new NF525FiscalAuditService(db, journal);

        var terminalId = "POS_MAIN_TERM";
        var managerId = Guid.NewGuid();
        var managerName = "Gérant";

        // Create 3 Z closures in September 2026 (local times)
        var z1Date = new DateTimeOffset(2026, 9, 10, 22, 0, 0, TimeSpan.FromHours(2));
        var z2Date = new DateTimeOffset(2026, 9, 15, 22, 0, 0, TimeSpan.FromHours(2));
        var z3Date = new DateTimeOffset(2026, 9, 30, 22, 0, 0, TimeSpan.FromHours(2));

        var vatZ1 = new Dictionary<string, long> { ["10.00"] = 100, ["20.00"] = 200 };
        var tendersZ1 = new Dictionary<string, long> { ["Cash"] = 1200, ["CreditCard"] = 1500 };

        var vatZ2 = new Dictionary<string, long> { ["10.00"] = 150, ["20.00"] = 300 };
        var tendersZ2 = new Dictionary<string, long> { ["Cash"] = 2000, ["CreditCard"] = 2500 };

        var vatZ3 = new Dictionary<string, long> { ["10.00"] = 50, ["20.00"] = 100 };
        var tendersZ3 = new Dictionary<string, long> { ["Cash"] = 800, ["CreditCard"] = 1000 };

        db.DailyFiscalClosures.AddRange(
            new DailyFiscalClosure
            {
                TerminalId = terminalId,
                ClosureSequence = 1,
                PeriodStartUtc = z1Date.AddHours(-12),
                PeriodEndUtc = z1Date,
                TotalSalesTtc = Money.FromCents(2700),
                TotalSalesHt = Money.FromCents(2400),
                TaxesSummaryJson = JsonSerializer.Serialize(vatZ1),
                TenderTotalsJson = JsonSerializer.Serialize(tendersZ1),
                PerpetualGrandTotalCents = 2700,
                SignatureHash = "SIG_Z1"
            },
            new DailyFiscalClosure
            {
                TerminalId = terminalId,
                ClosureSequence = 2,
                PeriodStartUtc = z2Date.AddHours(-12),
                PeriodEndUtc = z2Date,
                TotalSalesTtc = Money.FromCents(4500),
                TotalSalesHt = Money.FromCents(4050),
                TaxesSummaryJson = JsonSerializer.Serialize(vatZ2),
                TenderTotalsJson = JsonSerializer.Serialize(tendersZ2),
                PerpetualGrandTotalCents = 7200,
                SignatureHash = "SIG_Z2"
            },
            new DailyFiscalClosure
            {
                TerminalId = terminalId,
                ClosureSequence = 3,
                PeriodStartUtc = z3Date.AddHours(-12),
                PeriodEndUtc = z3Date,
                TotalSalesTtc = Money.FromCents(1800),
                TotalSalesHt = Money.FromCents(1650),
                TaxesSummaryJson = JsonSerializer.Serialize(vatZ3),
                TenderTotalsJson = JsonSerializer.Serialize(tendersZ3),
                PerpetualGrandTotalCents = 9000,
                SignatureHash = "SIG_Z3"
            }
        );
        await db.SaveChangesAsync();

        // Close on 2026-10-01 (month ended)
        var closeTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var monthly = await auditService.ExecutePeriodClosureAsync(
            terminalId,
            FiscalPeriodType.Monthly,
            "2026-09",
            managerId,
            managerName,
            utcNow: closeTime
        );

        monthly.TotalTtcCents.Should().Be(2700 + 4500 + 1800); // 9000 cents
        monthly.TotalHtCents.Should().Be(2400 + 4050 + 1650);   // 8100 cents
        monthly.DailyClosureCount.Should().Be(3);
        monthly.PerpetualGrandTotalCents.Should().Be(9000); // Taken from last Z

        var vats = JsonSerializer.Deserialize<Dictionary<string, long>>(monthly.TaxesSummaryJson);
        vats!["10.00"].Should().Be(300);
        vats["20.00"].Should().Be(600);

        var tenders = JsonSerializer.Deserialize<Dictionary<string, long>>(monthly.TenderTotalsJson);
        tenders!["Cash"].Should().Be(4000);
        tenders["CreditCard"].Should().Be(5000);
    }

    [Fact]
    public async Task MonthlyClosure_WhenPeriodNotEnded_ThrowsPeriodNotEnded()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);

        // Attempting to close September 2026 on September 25, 2026
        var midMonth = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

        var act = async () => await auditService.ExecutePeriodClosureAsync(
            "POS_MAIN_TERM",
            FiscalPeriodType.Monthly,
            "2026-09",
            Guid.NewGuid(),
            "Manager",
            utcNow: midMonth
        );

        var ex = await act.Should().ThrowAsync<PeriodClosureException>();
        ex.Which.Code.Should().Be("period_not_ended");
    }

    [Fact]
    public async Task MonthlyClosure_WhenAlreadyClosed_ThrowsPeriodAlreadyClosed()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);

        var closeTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

        // First closure
        await auditService.ExecutePeriodClosureAsync(
            "POS_MAIN_TERM",
            FiscalPeriodType.Monthly,
            "2026-09",
            Guid.NewGuid(),
            "Manager",
            utcNow: closeTime
        );

        // Second closure attempt
        var act = async () => await auditService.ExecutePeriodClosureAsync(
            "POS_MAIN_TERM",
            FiscalPeriodType.Monthly,
            "2026-09",
            Guid.NewGuid(),
            "Manager",
            utcNow: closeTime
        );

        var ex = await act.Should().ThrowAsync<PeriodClosureException>();
        ex.Which.Code.Should().Be("period_already_closed");
    }

    [Fact]
    public async Task MonthlyClosure_ZRunAfterMonthEnd_BelongsToTheMonthItCloses()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db, new FiscalJournalService(db));
        var terminalId = "POS_MAIN_TERM";

        // Reçu du 30/09, Z lancé le 02/10 : la période du Z commence en septembre, il clôture septembre.
        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = terminalId,
            ReceiptNumber = "POS_MAIN_TERM-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1200),
            TotalHtAmount = Money.FromCents(1091),
            CreatedAtUtc = new DateTimeOffset(2026, 9, 30, 14, 0, 0, TimeSpan.Zero),
            TaxBreakdownJson = "{}",
            SignatureHash = "SIG1"
        });
        db.DailyFiscalClosures.Add(new DailyFiscalClosure
        {
            TerminalId = terminalId,
            ClosureSequence = 1,
            PeriodStartUtc = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero),
            PeriodEndUtc = new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero),
            TotalSalesTtc = Money.FromCents(1200),
            TotalSalesHt = Money.FromCents(1091),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 1200,
            SignatureHash = "SIG_Z1"
        });
        await db.SaveChangesAsync();

        var monthly = await auditService.ExecutePeriodClosureAsync(
            terminalId,
            FiscalPeriodType.Monthly,
            "2026-09",
            Guid.NewGuid(),
            "Gérant",
            utcNow: new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero));

        monthly.DailyClosureCount.Should().Be(1);
        monthly.TotalTtcCents.Should().Be(1200);
    }

    [Fact]
    public async Task MonthlyClosure_WithReceiptWithoutDailyClosure_ThrowsMissingDailyClosures()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);

        var terminalId = "POS_MAIN_TERM";

        // Add a receipt on 2026-09-12
        var receiptDate = new DateTimeOffset(2026, 9, 12, 14, 30, 0, TimeSpan.FromHours(2));
        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = terminalId,
            ReceiptNumber = "POS_MAIN_TERM-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1200),
            TotalHtAmount = Money.FromCents(1091),
            CreatedAtUtc = receiptDate,
            TaxBreakdownJson = "{}",
            SignatureHash = "SIG1"
        });
        await db.SaveChangesAsync();

        // Attempting to close September on October 1st without any Z closure covering Sep 12
        var closeTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.FromHours(2));
        var act = async () => await auditService.ExecutePeriodClosureAsync(
            terminalId,
            FiscalPeriodType.Monthly,
            "2026-09",
            Guid.NewGuid(),
            "Manager",
            utcNow: closeTime
        );

        var ex = await act.Should().ThrowAsync<PeriodClosureException>();
        ex.Which.Code.Should().Be("missing_daily_closures");
        ex.Which.Days.Should().Contain("2026-09-12");
    }

    [Fact]
    public async Task AnnualClosure_WithMissingMonthlyClosures_ThrowsMissingMonthlyClosures()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);

        var terminalId = "POS_MAIN_TERM";

        // Default fiscal year: Jan 1 to Dec 31
        // Create only 10 monthly closures for 2026 (missing 2026-03 and 2026-11)
        for (int m = 1; m <= 12; m++)
        {
            if (m == 3 || m == 11) continue;
            var monthKey = $"2026-{m:D2}";
            var closeMonthTime = new DateTimeOffset(2026, m, 28, 23, 0, 0, TimeSpan.Zero).AddMonths(1);
            await auditService.ExecutePeriodClosureAsync(
                terminalId,
                FiscalPeriodType.Monthly,
                monthKey,
                Guid.NewGuid(),
                "Manager",
                utcNow: closeMonthTime
            );
        }

        // Attempt annual closure for 2026 on 2027-01-02
        var closeYearTime = new DateTimeOffset(2027, 1, 2, 10, 0, 0, TimeSpan.Zero);
        var act = async () => await auditService.ExecutePeriodClosureAsync(
            terminalId,
            FiscalPeriodType.Annual,
            "2026",
            Guid.NewGuid(),
            "Manager",
            utcNow: closeYearTime
        );

        var ex = await act.Should().ThrowAsync<PeriodClosureException>();
        ex.Which.Code.Should().Be("missing_monthly_closures");
        ex.Which.Months.Should().BeEquivalentTo(["2026-03", "2026-11"]);
    }

    [Fact]
    public async Task AnnualClosure_ShiftedFiscalYear_StartsAprilFirst()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);

        var terminalId = "POS_MAIN_TERM";

        // Configure shifted fiscal year starting April 1
        db.RestaurantSettings.Add(new RestaurantSettings
        {
            ReceiptLanguage = "fr",
            KitchenTicketLanguage = "fr",
            CompanyName = "Bistro",
            FiscalYearStartMonth = 4,
            FiscalYearStartDay = 1
        });
        await db.SaveChangesAsync();

        // For fiscal year 2026, months are 2026-04 through 2027-03
        var expectedMonths = new[]
        {
            "2026-04", "2026-05", "2026-06", "2026-07", "2026-08", "2026-09",
            "2026-10", "2026-11", "2026-12", "2027-01", "2027-02", "2027-03"
        };

        // Create all 12 monthly closures
        for (int i = 0; i < 12; i++)
        {
            var start = new DateTime(2026, 4, 1).AddMonths(i);
            var monthKey = start.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            var closeMonthTime = new DateTimeOffset(start.AddMonths(1), TimeSpan.Zero).AddDays(1);

            await auditService.ExecutePeriodClosureAsync(
                terminalId,
                FiscalPeriodType.Monthly,
                monthKey,
                Guid.NewGuid(),
                "Manager",
                utcNow: closeMonthTime
            );
        }

        // Close annual fiscal year 2026 on 2027-04-02
        var closeYearTime = new DateTimeOffset(2027, 4, 2, 10, 0, 0, TimeSpan.Zero);
        var annual = await auditService.ExecutePeriodClosureAsync(
            terminalId,
            FiscalPeriodType.Annual,
            "2026",
            Guid.NewGuid(),
            "Manager",
            utcNow: closeYearTime
        );

        annual.PeriodKey.Should().Be("2026");
        annual.PeriodType.Should().Be(FiscalPeriodType.Annual);
        annual.ClosureSequence.Should().Be(1);
        annual.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
        annual.SignatureHash.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task PeriodClosures_ChainSeparately_PerTerminalAndType()
    {
        using var db = CreateInMemoryDbContext();
        var auditService = new NF525FiscalAuditService(db);
        var verifier = new FiscalChainVerificationService(db);

        var t1 = "TERM_1";
        var t2 = "TERM_2";

        // Create Monthly closures on T1
        var c1 = await auditService.ExecutePeriodClosureAsync(
            t1, FiscalPeriodType.Monthly, "2026-01", Guid.NewGuid(), "M1",
            utcNow: new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));
        var c2 = await auditService.ExecutePeriodClosureAsync(
            t1, FiscalPeriodType.Monthly, "2026-02", Guid.NewGuid(), "M1",
            utcNow: new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero));

        c1.ClosureSequence.Should().Be(1);
        c1.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);

        c2.ClosureSequence.Should().Be(2);
        c2.PreviousSignatureHash.Should().Be(c1.SignatureHash);

        // Create Monthly closure on T2 (independent sequence and chain)
        var cT2 = await auditService.ExecutePeriodClosureAsync(
            t2, FiscalPeriodType.Monthly, "2026-01", Guid.NewGuid(), "M2",
            utcNow: new DateTimeOffset(2026, 2, 2, 0, 0, 0, TimeSpan.Zero));

        cT2.ClosureSequence.Should().Be(1);
        cT2.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);

        // Verify chains
        var result = await verifier.VerifyAllChainsAsync();
        result.IsValid.Should().BeTrue();

        var pChains = result.Chains.Where(c => c.Chain == "period_closures").ToList();
        pChains.Should().HaveCount(2); // One for TERM_1, one for TERM_2
        pChains.Should().OnlyContain(c => c.IsValid);
    }
}
