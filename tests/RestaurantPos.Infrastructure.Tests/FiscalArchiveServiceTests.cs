using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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

public class FiscalArchiveServiceTests : IDisposable
{
    private readonly string _tempDirectory;

    public FiscalArchiveServiceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "pos_test_archives_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }
        catch
        {
            // Ignore cleanup failure
        }
        GC.SuppressFinalize(this);
    }

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    private static FiscalPeriodClosure SeedPeriodClosure(
        AppDbContext db,
        string terminalId = "POS_MAIN_TERM",
        string periodKey = "2026-08",
        FiscalPeriodType periodType = FiscalPeriodType.Monthly)
    {
        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        var closure = new FiscalPeriodClosure
        {
            TerminalId = terminalId,
            PeriodType = periodType,
            PeriodKey = periodKey,
            ClosureSequence = 1,
            PeriodStartUtc = start,
            PeriodEndUtc = end,
            TotalTtcAmount = Money.FromCents(10000),
            TotalHtAmount = Money.FromCents(8500),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 10000,
            DailyClosureCount = 2,
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG_PERIOD_1",
            SealedByUserId = Guid.NewGuid(),
            SealedByUserName = "Manager"
        };

        db.PeriodClosures.Add(closure);

        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = terminalId,
            ReceiptNumber = "REC-001",
            OrderId = Guid.NewGuid(),
            SequenceNumber = 1,
            CreatedAtUtc = start.AddDays(5),
            TotalTtcAmount = Money.FromCents(5000),
            TotalHtAmount = Money.FromCents(4500),
            TaxBreakdownJson = "{}",
            SignatureHash = "SIG_REC_1",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash
        });

        db.DailyFiscalClosures.Add(new DailyFiscalClosure
        {
            TerminalId = terminalId,
            ClosureSequence = 1,
            PeriodStartUtc = start.AddDays(5),
            PeriodEndUtc = start.AddDays(5).AddHours(12),
            TotalSalesTtc = Money.FromCents(5000),
            TotalSalesHt = Money.FromCents(4500),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 5000,
            SignatureHash = "SIG_Z_1"
        });

        db.SaveChanges();
        return closure;
    }

    [Fact]
    public async Task CreateArchive_GeneratesZipWith5FilesAndValidManifest()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        var closure = SeedPeriodClosure(db);
        var userId = Guid.NewGuid();

        var result = await archiveService.CreateArchiveAsync(closure.Id, userId);

        result.Should().NotBeNull();
        result.PeriodClosureId.Should().Be(closure.Id);
        result.PeriodKey.Should().Be("2026-08");
        result.PeriodType.Should().Be(FiscalPeriodType.Monthly);
        result.ArchiveSequence.Should().Be(1);
        result.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
        result.SignatureHash.Should().NotBeNullOrWhiteSpace();
        result.FileSha256.Should().HaveLength(64);
        result.FileSizeBytes.Should().BeGreaterThan(0);

        var filePath = Path.Combine(_tempDirectory, result.FileName);
        File.Exists(filePath).Should().BeTrue();

        // Verify ZIP contents: exactly 5 files
        using (var zip = ZipFile.OpenRead(filePath))
        {
            zip.Entries.Should().HaveCount(5);
            zip.GetEntry("receipts.json").Should().NotBeNull();
            zip.GetEntry("closures.json").Should().NotBeNull();
            zip.GetEntry("period-closures.json").Should().NotBeNull();
            zip.GetEntry("journal.json").Should().NotBeNull();
            var manifestEntry = zip.GetEntry("manifest.json");
            manifestEntry.Should().NotBeNull();

            using var s = manifestEntry!.Open();
            using var doc = JsonDocument.Parse(s);
            var root = doc.RootElement;
            root.GetProperty("archiveId").GetGuid().Should().Be(result.Id);
            root.GetProperty("periodClosureId").GetGuid().Should().Be(closure.Id);
            root.GetProperty("periodKey").GetString().Should().Be("2026-08");
            root.GetProperty("counts").GetProperty("receipts").GetInt32().Should().Be(1);
            root.GetProperty("counts").GetProperty("closures").GetInt32().Should().Be(1);

            var files = root.GetProperty("files");
            files.GetProperty("receipts.json").GetString().Should().HaveLength(64);
            files.GetProperty("closures.json").GetString().Should().HaveLength(64);
            files.GetProperty("period-closures.json").GetString().Should().HaveLength(64);
            files.GetProperty("journal.json").GetString().Should().HaveLength(64);
        }

        // JET entry created in same transaction
        var jet = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.ArchiveCreated);
        jet.Should().NotBeNull();
        jet!.PayloadJson.Should().Contain(result.Id.ToString());
    }

    [Fact]
    public async Task CreateArchive_ThrowsArchiveExistsException_WhenCalledTwiceForSameClosure()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        var closure = SeedPeriodClosure(db);
        var userId = Guid.NewGuid();

        await archiveService.CreateArchiveAsync(closure.Id, userId);

        var act = () => archiveService.CreateArchiveAsync(closure.Id, userId);
        var ex = await Assert.ThrowsAsync<ArchiveExistsException>(act);
        ex.Code.Should().Be("archive_exists");
    }

    [Fact]
    public async Task CreateArchive_ChainsSequentially_AndVerifyAllChainsPasses()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);
        var verificationService = new FiscalChainVerificationService(db, journal);

        var closure1 = SeedPeriodClosure(db, "POS_MAIN_TERM", "2026-08");

        var closure2 = new FiscalPeriodClosure
        {
            TerminalId = "POS_MAIN_TERM",
            PeriodType = FiscalPeriodType.Monthly,
            PeriodKey = "2026-09",
            ClosureSequence = 2,
            PeriodStartUtc = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
            PeriodEndUtc = new DateTimeOffset(2026, 9, 30, 23, 59, 59, TimeSpan.Zero),
            TotalTtcAmount = Money.FromCents(20000),
            TotalHtAmount = Money.FromCents(17000),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 30000,
            DailyClosureCount = 5,
            PreviousSignatureHash = closure1.SignatureHash,
            SignatureHash = "SIG_PERIOD_2",
            SealedByUserId = Guid.NewGuid(),
            SealedByUserName = "Manager"
        };
        db.PeriodClosures.Add(closure2);
        await db.SaveChangesAsync();

        var userId = Guid.NewGuid();
        var a1 = await archiveService.CreateArchiveAsync(closure1.Id, userId);
        var a2 = await archiveService.CreateArchiveAsync(closure2.Id, userId);

        a1.ArchiveSequence.Should().Be(1);
        a1.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);

        a2.ArchiveSequence.Should().Be(2);
        a2.PreviousSignatureHash.Should().Be(a1.SignatureHash);

        var check = await verificationService.VerifyAllChainsAsync();
        var archiveChain = check.Chains.FirstOrDefault(c => c.Chain == "archives");
        archiveChain.Should().NotBeNull();
        archiveChain!.IsValid.Should().BeTrue();
        archiveChain.CheckedCount.Should().Be(2);
        archiveChain.Break.Should().BeNull();
    }

    [Fact]
    public async Task VerifyArchive_ReturnsValid_WhenArchiveIsUntampered()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        var closure = SeedPeriodClosure(db);
        var created = await archiveService.CreateArchiveAsync(closure.Id, Guid.NewGuid());

        var fileResult = await archiveService.GetArchiveFileAsync(created.Id);
        fileResult.Should().NotBeNull();

        using var stream = fileResult!.Value.Stream;
        var verifyResult = await archiveService.VerifyArchiveAsync(stream, created.FileName);

        verifyResult.IsValid.Should().BeTrue();
        verifyResult.ArchiveId.Should().Be(created.Id);
        verifyResult.Reason.Should().BeNull();

        // Check that GetArchiveFileAsync logged JET ArchiveExported
        var jet = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.ArchiveExported);
        jet.Should().NotBeNull();
        jet!.PayloadJson.Should().Contain(created.Id.ToString());
    }

    [Fact]
    public async Task VerifyArchive_ReturnsHashMismatch_WhenAByteIsModified()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        var closure = SeedPeriodClosure(db);
        var created = await archiveService.CreateArchiveAsync(closure.Id, Guid.NewGuid());

        var filePath = Path.Combine(_tempDirectory, created.FileName);
        byte[] bytes = await File.ReadAllBytesAsync(filePath);

        // Tamper with byte at offset 100 as in quickstart.md
        bytes[100] = (byte)(bytes[100] ^ 0xFF);

        using var tamperedStream = new MemoryStream(bytes);
        var verifyResult = await archiveService.VerifyArchiveAsync(tamperedStream, created.FileName);

        verifyResult.IsValid.Should().BeFalse();
        verifyResult.ArchiveId.Should().Be(created.Id);
        verifyResult.Reason.Should().Be("hash_mismatch");
    }

    [Fact]
    public async Task VerifyArchive_ReturnsUnknownArchive_WhenZipIsUnrecognized()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("random.txt");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write("hello world");
        }
        ms.Position = 0;

        var verifyResult = await archiveService.VerifyArchiveAsync(ms, "random.zip");

        verifyResult.IsValid.Should().BeFalse();
        verifyResult.ArchiveId.Should().BeNull();
        verifyResult.Reason.Should().Be("unknown_archive");
    }

    [Fact]
    public async Task VerifyArchive_ReturnsChainBreak_WhenSignatureHashInDbIsCorrupted()
    {
        using var db = CreateInMemoryDbContext();
        var journal = new FiscalJournalService(db);
        var archiveService = new FiscalArchiveService(db, journal, archivesDirectory: _tempDirectory);

        var closure = SeedPeriodClosure(db);
        var created = await archiveService.CreateArchiveAsync(closure.Id, Guid.NewGuid());

        // Corrupt signature hash in DB directly
        var archiveInDb = await db.FiscalArchives.FirstAsync(a => a.Id == created.Id);
        archiveInDb.SignatureHash = "CORRUPTED_SIGNATURE_HASH";
        await db.SaveChangesAsync();

        var filePath = Path.Combine(_tempDirectory, created.FileName);
        using var stream = File.OpenRead(filePath);
        var verifyResult = await archiveService.VerifyArchiveAsync(stream, created.FileName);

        verifyResult.IsValid.Should().BeFalse();
        verifyResult.ArchiveId.Should().Be(created.Id);
        verifyResult.Reason.Should().Be("chain_break");
    }
}
