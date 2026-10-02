using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class FiscalJournalServiceTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task AppendAsync_FirstEntry_StartsAtSequenceOneAndGenesisHash()
    {
        using var db = CreateInMemoryDbContext();
        var service = new FiscalJournalService(db);

        var entry = await service.AppendAsync(
            JournalEventTypes.ServerStarted,
            "{\"version\":\"1.0.0\"}",
            terminalId: "POS_MAIN_TERM",
            operatorId: null);

        entry.ChainSequence.Should().Be(1);
        entry.PreviousHash.Should().Be(NF525FiscalAuditService.GenesisHash);
        entry.EventType.Should().Be(JournalEventTypes.ServerStarted);
        entry.EntryHash.Should().Be(FiscalHashing.ComputeJetHash(
            NF525FiscalAuditService.GenesisHash,
            1,
            JournalEventTypes.ServerStarted,
            entry.OccurredAtUtc,
            "POS_MAIN_TERM",
            null,
            "{\"version\":\"1.0.0\"}"));

        var inDb = await db.JournalEntries.FirstOrDefaultAsync(j => j.Id == entry.Id);
        inDb.Should().NotBeNull();
        inDb!.ChainSequence.Should().Be(1);
    }

    [Fact]
    public async Task AppendAsync_MultipleEntries_CreatesContinuousChain()
    {
        using var db = CreateInMemoryDbContext();
        var service = new FiscalJournalService(db);
        var opId = Guid.NewGuid();

        var entry1 = await service.AppendAsync(JournalEventTypes.LoginSucceeded, "{}", "T01", opId);
        var entry2 = await service.AppendAsync(JournalEventTypes.TaxRateChanged, "{\"rate\":20}", "T01", opId);
        var entry3 = await service.AppendAsync(JournalEventTypes.ZClosure, "{\"seq\":1}", "T01", opId);

        entry1.ChainSequence.Should().Be(1);
        entry1.PreviousHash.Should().Be(NF525FiscalAuditService.GenesisHash);

        entry2.ChainSequence.Should().Be(2);
        entry2.PreviousHash.Should().Be(entry1.EntryHash);
        entry2.EntryHash.Should().Be(FiscalHashing.ComputeJetHash(
            entry1.EntryHash,
            2,
            JournalEventTypes.TaxRateChanged,
            entry2.OccurredAtUtc,
            "T01",
            opId,
            "{\"rate\":20}"));

        entry3.ChainSequence.Should().Be(3);
        entry3.PreviousHash.Should().Be(entry2.EntryHash);
    }

    [Fact]
    public async Task AppendAsync_IgnoresLegacyEntriesWithNullChainSequence()
    {
        using var db = CreateInMemoryDbContext();
        db.JournalEntries.Add(new TransactionJournalEntry
        {
            Id = Guid.NewGuid(),
            LocalSequence = 10,
            TerminalId = "T01",
            IdempotencyKey = Guid.NewGuid().ToString(),
            EventType = "LEGACY_EVENT",
            PayloadJson = "{}",
            EntryHash = "RANDOM_HASH",
            ChainSequence = null,
            PreviousHash = null
        });
        await db.SaveChangesAsync();

        var service = new FiscalJournalService(db);

        var firstChained = await service.AppendAsync(JournalEventTypes.ServerStarted, "{}");

        firstChained.ChainSequence.Should().Be(1);
        firstChained.PreviousHash.Should().Be(NF525FiscalAuditService.GenesisHash);
    }
}
