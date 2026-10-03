using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ChainVerificationTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task VerifyChainsAsync_WhenAllChainsIntact_ReturnsValid()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var verifier = new FiscalChainVerificationService(db);

        // Add 2 JET entries
        await journal.AppendAsync(JournalEventTypes.ServerStarted, "{}", "POS_MAIN_TERM");
        await journal.AppendAsync(JournalEventTypes.ServerStopped, "{}", "POS_MAIN_TERM");

        // Add 2 Receipts
        var now = DateTimeOffset.UtcNow;
        var r1Hash = FiscalHashing.ComputeReceiptHash(NF525FiscalAuditService.GenesisHash, "POS_MAIN_TERM", 1, 1500, now, "{}");
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "POS_MAIN_TERM-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1500),
            TotalHtAmount = Money.FromCents(1364),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = r1Hash,
            CreatedAtUtc = now
        };
        var r2Hash = FiscalHashing.ComputeReceiptHash(r1Hash, "POS_MAIN_TERM", 2, 2000, now.AddMinutes(1), "{}");
        var r2 = new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "POS_MAIN_TERM-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(2000),
            TotalHtAmount = Money.FromCents(1818),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = r1Hash,
            SignatureHash = r2Hash,
            CreatedAtUtc = now.AddMinutes(1)
        };
        db.FiscalReceipts.AddRange(r1, r2);
        await db.SaveChangesAsync();

        var result = await verifier.VerifyAllChainsAsync();

        result.IsValid.Should().BeTrue();
        result.Chains.Should().Contain(c => c.Chain == "receipts" && c.TerminalId == "POS_MAIN_TERM" && c.CheckedCount == 2 && c.IsValid);
        result.Chains.Should().Contain(c => c.Chain == "journal" && c.CheckedCount == 2 && c.IsValid);
    }

    [Fact]
    public async Task VerifyChainsAsync_ReceiptSignatureTampered_DetectsBreak()
    {
        using var db = CreateInMemoryDbContext();
        var verifier = new FiscalChainVerificationService(db);

        var now = DateTimeOffset.UtcNow;
        var r1Hash = FiscalHashing.ComputeReceiptHash(NF525FiscalAuditService.GenesisHash, "POS_MAIN_TERM", 1, 1500, now, "{}");
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "POS_MAIN_TERM-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1500),
            TotalHtAmount = Money.FromCents(1364),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "tampered_hash_signature",
            CreatedAtUtc = now
        };
        db.FiscalReceipts.Add(r1);
        await db.SaveChangesAsync();

        var result = await verifier.VerifyAllChainsAsync();

        result.IsValid.Should().BeFalse();
        var chain = result.Chains.First(c => c.Chain == "receipts" && c.TerminalId == "POS_MAIN_TERM");
        chain.IsValid.Should().BeFalse();
        chain.Break.Should().NotBeNull();
        chain.Break!.Kind.Should().Be("signature_mismatch");
        chain.Break.SequenceNumber.Should().Be(1);
    }

    [Fact]
    public async Task VerifyChainsAsync_SequenceGap_DetectsBreak()
    {
        using var db = CreateInMemoryDbContext();
        var verifier = new FiscalChainVerificationService(db);

        var now = DateTimeOffset.UtcNow;
        var r1Hash = FiscalHashing.ComputeReceiptHash(NF525FiscalAuditService.GenesisHash, "POS_MAIN_TERM", 1, 1500, now, "{}");
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "POS_MAIN_TERM-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(1500),
            TotalHtAmount = Money.FromCents(1364),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = r1Hash,
            CreatedAtUtc = now
        };
        // Gap: sequence 3 instead of 2
        var r3Hash = FiscalHashing.ComputeReceiptHash(r1Hash, "POS_MAIN_TERM", 3, 2000, now.AddMinutes(1), "{}");
        var r3 = new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "POS_MAIN_TERM-000003",
            SequenceNumber = 3,
            TotalTtcAmount = Money.FromCents(2000),
            TotalHtAmount = Money.FromCents(1818),
            TaxBreakdownJson = "{}",
            PreviousSignatureHash = r1Hash,
            SignatureHash = r3Hash,
            CreatedAtUtc = now.AddMinutes(1)
        };
        db.FiscalReceipts.AddRange(r1, r3);
        await db.SaveChangesAsync();

        var result = await verifier.VerifyAllChainsAsync();

        result.IsValid.Should().BeFalse();
        var chain = result.Chains.First(c => c.Chain == "receipts" && c.TerminalId == "POS_MAIN_TERM");
        chain.IsValid.Should().BeFalse();
        chain.Break.Should().NotBeNull();
        chain.Break!.Kind.Should().Be("sequence_gap");
        chain.Break.SequenceNumber.Should().Be(3);
    }

    [Fact]
    public async Task VerifyChainsAsync_LegacyJournalEntries_CountedSeparately()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var verifier = new FiscalChainVerificationService(db);

        // Add 1 legacy entry (ChainSequence is null)
        db.JournalEntries.Add(new TransactionJournalEntry
        {
            EventType = "LEGACY",
            PayloadJson = "{}",
            EntryHash = "legacy_hash",
            IdempotencyKey = "k1",
            ChainSequence = null
        });
        await db.SaveChangesAsync();

        // Add 1 chained entry
        await journal.AppendAsync(JournalEventTypes.ServerStarted, "{}", null);

        var result = await verifier.VerifyAllChainsAsync();

        var jetChain = result.Chains.First(c => c.Chain == "journal");
        jetChain.CheckedCount.Should().Be(1);
        jetChain.LegacyCount.Should().Be(1);
        jetChain.IsValid.Should().BeTrue();
    }

    [Fact]
    [Trait("Category", "Slow")]
    public async Task VerifyChains_Performance_100kReceipts_Under30Seconds()
    {
        using var db = CreateInMemoryDbContext();
        var verifier = new FiscalChainVerificationService(db);

        var receipts = new List<FiscalReceipt>(100_000);
        var prevHash = NF525FiscalAuditService.GenesisHash;
        var now = DateTimeOffset.UtcNow;
        var terminal = "T_PERF";

        for (int i = 1; i <= 100_000; i++)
        {
            var hash = FiscalHashing.ComputeReceiptHash(prevHash, terminal, i, 1000, now, "{}");
            receipts.Add(new FiscalReceipt
            {
                TerminalId = terminal,
                ReceiptNumber = $"{terminal}-{i:D6}",
                SequenceNumber = i,
                TotalTtcAmount = Money.FromCents(1000),
                TotalHtAmount = Money.FromCents(900),
                TaxBreakdownJson = "{}",
                PreviousSignatureHash = prevHash,
                SignatureHash = hash,
                CreatedAtUtc = now
            });
            prevHash = hash;
        }

        db.FiscalReceipts.AddRange(receipts);
        await db.SaveChangesAsync();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await verifier.VerifyAllChainsAsync();
        sw.Stop();

        result.IsValid.Should().BeTrue();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
    }
}
