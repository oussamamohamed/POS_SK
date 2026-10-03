using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class FiscalImmutabilityInterceptorTests
{
    private static AppDbContext CreateDbContextWithInterceptor()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new FiscalImmutabilityInterceptor())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task Modify_FiscalReceipt_ThrowsInvalidOperationException()
    {
        using var db = CreateDbContextWithInterceptor();
        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1000),
            TotalHtAmount = Money.FromCents(900),
            TaxBreakdownJson = "{}"
        };
        db.FiscalReceipts.Add(receipt);
        await db.SaveChangesAsync();

        // Attempt mutation
        receipt.TotalTtcAmount = Money.FromCents(9999);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task Delete_FiscalReceipt_ThrowsInvalidOperationException()
    {
        using var db = CreateDbContextWithInterceptor();
        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(1000),
            TotalHtAmount = Money.FromCents(900),
            TaxBreakdownJson = "{}"
        };
        db.FiscalReceipts.Add(receipt);
        await db.SaveChangesAsync();

        db.FiscalReceipts.Remove(receipt);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task ModifyOrDelete_PaymentTender_ThrowsInvalidOperationException()
    {
        using var db = CreateDbContextWithInterceptor();
        var tender = new PaymentTender
        {
            FiscalReceiptId = Guid.NewGuid(),
            Method = PaymentMethod.CreditCard,
            Amount = Money.FromCents(1500),
            Tendered = Money.FromCents(1500)
        };
        db.PaymentTenders.Add(tender);
        await db.SaveChangesAsync();

        tender.Amount = Money.FromCents(2000);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task ModifyOrDelete_DailyFiscalClosure_ThrowsInvalidOperationException()
    {
        using var db = CreateDbContextWithInterceptor();
        var closure = new DailyFiscalClosure
        {
            TerminalId = "POS01",
            ClosureSequence = 1,
            PeriodStartUtc = DateTimeOffset.UtcNow.AddDays(-1),
            PeriodEndUtc = DateTimeOffset.UtcNow,
            TotalSalesTtc = Money.FromCents(5000),
            TotalSalesHt = Money.FromCents(4500),
            SignatureHash = "sig",
            PreviousSignatureHash = "prev"
        };
        db.DailyFiscalClosures.Add(closure);
        await db.SaveChangesAsync();

        closure.TotalSalesTtc = Money.FromCents(9000);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task Modify_ChainedJournalEntry_ThrowsInvalidOperationException()
    {
        using var db = CreateDbContextWithInterceptor();
        var entry = new TransactionJournalEntry
        {
            EventType = JournalEventTypes.ServerStarted,
            PayloadJson = "{}",
            EntryHash = "hash1",
            IdempotencyKey = "key1",
            ChainSequence = 1,
            PreviousHash = "genesis"
        };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();

        entry.PayloadJson = "{\"tampered\": true}";
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*immutable*");
    }

    [Fact]
    public async Task Modify_LegacyJournalEntry_AllowedWithoutException()
    {
        using var db = CreateDbContextWithInterceptor();
        var legacyEntry = new TransactionJournalEntry
        {
            EventType = "LEGACY_EVENT",
            PayloadJson = "{}",
            EntryHash = "hash_legacy",
            IdempotencyKey = "key_legacy",
            ChainSequence = null, // Legacy entry
            PreviousHash = null
        };
        db.JournalEntries.Add(legacyEntry);
        await db.SaveChangesAsync();

        legacyEntry.PayloadJson = "{\"updated\": true}";
        var act = () => db.SaveChangesAsync();

        await act.Should().NotThrowAsync();
    }
}
