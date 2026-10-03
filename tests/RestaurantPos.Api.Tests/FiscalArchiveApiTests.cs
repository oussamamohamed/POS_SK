using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class FiscalArchiveApiTests
{
    private static async Task<FiscalPeriodClosure> SeedPeriodClosureAsync(PosApiApplicationFactory factory, string periodKey = "2026-08")
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var start = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 8, 31, 23, 59, 59, TimeSpan.Zero);

        var closure = new FiscalPeriodClosure
        {
            TerminalId = "POS_MAIN_TERM",
            PeriodType = FiscalPeriodType.Monthly,
            PeriodKey = periodKey,
            ClosureSequence = 1,
            PeriodStartUtc = start,
            PeriodEndUtc = end,
            TotalTtcAmount = Money.FromCents(10000),
            TotalHtAmount = Money.FromCents(8500),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 10000,
            DailyClosureCount = 1,
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG_PERIOD_TEST",
            SealedByUserId = Guid.NewGuid(),
            SealedByUserName = "Manager"
        };

        db.PeriodClosures.Add(closure);

        db.FiscalReceipts.Add(new FiscalReceipt
        {
            TerminalId = "POS_MAIN_TERM",
            ReceiptNumber = "REC-001",
            OrderId = Guid.NewGuid(),
            SequenceNumber = 1,
            CreatedAtUtc = start.AddDays(2),
            TotalTtcAmount = Money.FromCents(10000),
            TotalHtAmount = Money.FromCents(8500),
            TaxBreakdownJson = "{}",
            SignatureHash = "SIG_REC_1",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash
        });

        db.DailyFiscalClosures.Add(new DailyFiscalClosure
        {
            TerminalId = "POS_MAIN_TERM",
            ClosureSequence = 1,
            PeriodStartUtc = start.AddDays(2),
            PeriodEndUtc = start.AddDays(2).AddHours(10),
            TotalSalesTtc = Money.FromCents(10000),
            TotalSalesHt = Money.FromCents(8500),
            TaxesSummaryJson = "{}",
            TenderTotalsJson = "{}",
            PerpetualGrandTotalCents = 10000,
            SignatureHash = "SIG_Z_1"
        });

        await db.SaveChangesAsync();
        return closure;
    }

    [Fact]
    public async Task CreateArchive_ReturnsOk_WithMetadata_And409OnDuplicate()
    {
        using var factory = new PosApiApplicationFactory();
        var closure = await SeedPeriodClosureAsync(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // 1. Create Archive
        var createResp = await client.PostAsJsonAsync("/api/fiscal/archives", new { periodClosureId = closure.Id });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var archiveJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        archiveJson.GetProperty("id").GetGuid().Should().NotBeEmpty();
        archiveJson.GetProperty("fileName").GetString().Should().Contain("2026-08");
        archiveJson.GetProperty("fileSha256").GetString().Should().HaveLength(64);
        archiveJson.GetProperty("fileSizeBytes").GetInt64().Should().BeGreaterThan(0);
        archiveJson.GetProperty("signatureHash").GetString().Should().NotBeNullOrWhiteSpace();

        // 2. Duplicate Archive request -> 409 { code: "archive_exists" }
        var dupResp = await client.PostAsJsonAsync("/api/fiscal/archives", new { periodClosureId = closure.Id });
        dupResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var dupJson = await dupResp.Content.ReadFromJsonAsync<JsonElement>();
        dupJson.GetProperty("code").GetString().Should().Be("archive_exists");
    }

    [Fact]
    public async Task DownloadArchive_ReturnsZipFile_AndLogsJetArchiveExported()
    {
        using var factory = new PosApiApplicationFactory();
        var closure = await SeedPeriodClosureAsync(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var createResp = await client.PostAsJsonAsync("/api/fiscal/archives", new { periodClosureId = closure.Id });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var archiveJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var archiveId = archiveJson.GetProperty("id").GetGuid();

        // Download archive file
        var fileResp = await client.GetAsync($"/api/fiscal/archives/{archiveId}/file");
        fileResp.StatusCode.Should().Be(HttpStatusCode.OK);
        fileResp.Content.Headers.ContentType?.MediaType.Should().Be("application/zip");

        var bytes = await fileResp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(0);

        // Verify it is a valid zip with 5 files
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            zip.Entries.Should().HaveCount(5);
        }

        // Verify JET event logged
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jet = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == JournalEventTypes.ArchiveExported);
            jet.Should().NotBeNull();
            jet!.PayloadJson.Should().Contain(archiveId.ToString());
        }
    }

    [Fact]
    public async Task VerifyArchive_Multipart_HandlesValid_Tampered_AndUnknown()
    {
        using var factory = new PosApiApplicationFactory();
        var closure = await SeedPeriodClosureAsync(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // Create archive
        var createResp = await client.PostAsJsonAsync("/api/fiscal/archives", new { periodClosureId = closure.Id });
        var archiveJson = await createResp.Content.ReadFromJsonAsync<JsonElement>();
        var archiveId = archiveJson.GetProperty("id").GetGuid();
        var fileName = archiveJson.GetProperty("fileName").GetString()!;

        // Download archive bytes
        var fileResp = await client.GetAsync($"/api/fiscal/archives/{archiveId}/file");
        var originalBytes = await fileResp.Content.ReadAsByteArrayAsync();

        // 1. Verify unmodified -> valid
        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(originalBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(fileContent, "file", fileName);

            var verifyResp = await client.PostAsync("/api/fiscal/archives/verify", content);
            verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
            res.GetProperty("isValid").GetBoolean().Should().BeTrue();
            res.GetProperty("archiveId").GetGuid().Should().Be(archiveId);
        }

        // 2. Modify one byte -> hash_mismatch
        var tamperedBytes = (byte[])originalBytes.Clone();
        tamperedBytes[100] = (byte)(tamperedBytes[100] ^ 0xFF);

        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(tamperedBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(fileContent, "file", fileName);

            var verifyResp = await client.PostAsync("/api/fiscal/archives/verify", content);
            verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
            res.GetProperty("isValid").GetBoolean().Should().BeFalse();
            res.GetProperty("archiveId").GetGuid().Should().Be(archiveId);
            res.GetProperty("reason").GetString().Should().Be("hash_mismatch");
        }

        // 3. Completely random zip -> unknown_archive
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("test.txt");
            using var s = entry.Open();
            using var w = new StreamWriter(s);
            w.Write("random content");
        }
        var randomBytes = ms.ToArray();

        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(randomBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            content.Add(fileContent, "file", "unknown.zip");

            var verifyResp = await client.PostAsync("/api/fiscal/archives/verify", content);
            verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var res = await verifyResp.Content.ReadFromJsonAsync<JsonElement>();
            res.GetProperty("isValid").GetBoolean().Should().BeFalse();
            res.GetProperty("reason").GetString().Should().Be("unknown_archive");
        }
    }

    [Fact]
    public async Task GetArchives_ReturnsListOfArchives()
    {
        using var factory = new PosApiApplicationFactory();
        var closure = await SeedPeriodClosureAsync(factory);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        await client.PostAsJsonAsync("/api/fiscal/archives", new { periodClosureId = closure.Id });

        var listResp = await client.GetAsync("/api/fiscal/archives");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var archives = await listResp.Content.ReadFromJsonAsync<JsonElement>();
        archives.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
    }
}
