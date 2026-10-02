using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class DuplicatePrintTests
{
    private static async Task SeedPrinterAsync(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!await db.PrinterConfigurations.AnyAsync(p => p.IsActive))
        {
            db.PrinterConfigurations.Add(new PrinterConfiguration
            {
                Id = Guid.NewGuid(),
                Name = "Test Thermal Printer",
                IpAddress = "127.0.0.1",
                IsActive = true,
                AssignedStationIds = [PreparationStations.Receipt],
                PaperWidthMm = 80
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task ReprintReceipt_IncrementsDuplicateNumber_AndWritesJournalEntry()
    {
        using var factory = new PosApiApplicationFactory();
        await SeedPrinterAsync(factory);

        Guid receiptId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { TableNumber = "T1", Status = OrderStatus.Paid };
            db.Orders.Add(order);

            var receipt = new FiscalReceipt
            {
                TerminalId = "POS_MAIN_TERM",
                ReceiptNumber = "POS_MAIN_TERM-000001",
                SequenceNumber = 1,
                OrderId = order.Id,
                TotalTtcAmount = Money.FromCents(2500),
                TotalHtAmount = Money.FromCents(2273),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.FiscalReceipts.Add(receipt);
            await db.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // 1ère réimpression -> duplicateNumber = 1
        var resp1 = await client.PostAsync($"/api/checkout/receipts/{receiptId}/reprint", null);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        res1.GetProperty("printQueued").GetBoolean().Should().BeTrue();
        res1.GetProperty("duplicateNumber").GetInt32().Should().Be(1);

        // 2ème réimpression -> duplicateNumber = 2
        var resp2 = await client.PostAsync($"/api/checkout/receipts/{receiptId}/reprint", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        res2.GetProperty("printQueued").GetBoolean().Should().BeTrue();
        res2.GetProperty("duplicateNumber").GetInt32().Should().Be(2);

        // Vérifier base de données et journal
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var printJobs = await db.PrintJobs
                .Where(j => j.DuplicateOfDocumentId == receiptId)
                .OrderBy(j => j.DuplicateNumber)
                .ToListAsync();

            printJobs.Should().HaveCount(2);
            printJobs[0].DuplicateNumber.Should().Be(1);
            printJobs[1].DuplicateNumber.Should().Be(2);

            var journalEntries = await db.JournalEntries
                .Where(e => e.EventType == JournalEventTypes.DuplicatePrinted)
                .ToListAsync();

            journalEntries.Should().HaveCount(2);
            journalEntries.Should().OnlyContain(e => e.PayloadJson.Contains(receiptId.ToString()));
            journalEntries.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":1"));
            journalEntries.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":2"));
        }
    }

    [Fact]
    public async Task ReprintReceipt_WithoutPrinter_NumberingStaysContinuous()
    {
        using var factory = new PosApiApplicationFactory();

        Guid receiptId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { TableNumber = "T1", Status = OrderStatus.Paid };
            await db.PrinterConfigurations.ForEachAsync(p => p.IsActive = false);
            db.Orders.Add(order);
            var receipt = new FiscalReceipt
            {
                TerminalId = "POS_MAIN_TERM",
                ReceiptNumber = "POS_MAIN_TERM-000001",
                SequenceNumber = 1,
                OrderId = order.Id,
                TotalTtcAmount = Money.FromCents(2500),
                TotalHtAmount = Money.FromCents(2273),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.FiscalReceipts.Add(receipt);
            await db.SaveChangesAsync();
            receiptId = receipt.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        for (var expected = 1; expected <= 3; expected++)
        {
            var resp = await client.PostAsync($"/api/checkout/receipts/{receiptId}/reprint", null);
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            json.GetProperty("printQueued").GetBoolean().Should().BeFalse();
            json.GetProperty("duplicateNumber").GetInt32().Should().Be(expected);
        }
    }

    [Fact]
    public async Task ReprintReceipt_NumberingIsPerDocument()
    {
        using var factory = new PosApiApplicationFactory();
        await SeedPrinterAsync(factory);

        Guid receipt1Id, receipt2Id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var r1 = new FiscalReceipt
            {
                TerminalId = "POS_MAIN_TERM",
                ReceiptNumber = "POS_MAIN_TERM-000001",
                SequenceNumber = 1,
                TotalTtcAmount = Money.FromCents(1000),
                TotalHtAmount = Money.FromCents(909),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            var r2 = new FiscalReceipt
            {
                TerminalId = "POS_MAIN_TERM",
                ReceiptNumber = "POS_MAIN_TERM-000002",
                SequenceNumber = 2,
                TotalTtcAmount = Money.FromCents(2000),
                TotalHtAmount = Money.FromCents(1818),
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
            db.FiscalReceipts.AddRange(r1, r2);
            await db.SaveChangesAsync();
            receipt1Id = r1.Id;
            receipt2Id = r2.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // Reprint Receipt 1 -> duplicate 1
        var r1Rep1 = await (await client.PostAsync($"/api/checkout/receipts/{receipt1Id}/reprint", null)).Content.ReadFromJsonAsync<JsonElement>();
        r1Rep1.GetProperty("duplicateNumber").GetInt32().Should().Be(1);

        // Reprint Receipt 2 -> duplicate 1 (starts at 1 for this document)
        var r2Rep1 = await (await client.PostAsync($"/api/checkout/receipts/{receipt2Id}/reprint", null)).Content.ReadFromJsonAsync<JsonElement>();
        r2Rep1.GetProperty("duplicateNumber").GetInt32().Should().Be(1);

        // Reprint Receipt 1 -> duplicate 2
        var r1Rep2 = await (await client.PostAsync($"/api/checkout/receipts/{receipt1Id}/reprint", null)).Content.ReadFromJsonAsync<JsonElement>();
        r1Rep2.GetProperty("duplicateNumber").GetInt32().Should().Be(2);

        // Reprint Receipt 2 -> duplicate 2
        var r2Rep2 = await (await client.PostAsync($"/api/checkout/receipts/{receipt2Id}/reprint", null)).Content.ReadFromJsonAsync<JsonElement>();
        r2Rep2.GetProperty("duplicateNumber").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task ReprintReceipt_ByReceiptNumber_Succeeds()
    {
        using var factory = new PosApiApplicationFactory();
        await SeedPrinterAsync(factory);

        const string receiptNum = "POS_MAIN_TERM-000042";
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.FiscalReceipts.Add(new FiscalReceipt
            {
                TerminalId = "POS_MAIN_TERM",
                ReceiptNumber = receiptNum,
                SequenceNumber = 42,
                TotalTtcAmount = Money.FromCents(1500),
                TotalHtAmount = Money.FromCents(1364),
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var resp = await client.PostAsync($"/api/checkout/receipts/{receiptNum}/reprint", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("printQueued").GetBoolean().Should().BeTrue();
        json.GetProperty("duplicateNumber").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task ReprintReceipt_UnknownReceipt_Returns404()
    {
        using var factory = new PosApiApplicationFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var resp = await client.PostAsync($"/api/checkout/receipts/{Guid.NewGuid()}/reprint", null);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task LatestClosurePrint_ReturnsDuplicateNumber_AndIncrements_AndLogsJet()
    {
        using var factory = new PosApiApplicationFactory();
        await SeedPrinterAsync(factory);

        Guid closureId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var closure = new DailyFiscalClosure
            {
                TerminalId = "POS_MAIN_TERM",
                ClosureSequence = 5,
                PeriodStartUtc = DateTimeOffset.UtcNow.AddHours(-10),
                PeriodEndUtc = DateTimeOffset.UtcNow,
                TotalSalesTtc = Money.FromCents(50000),
                TotalSalesHt = Money.FromCents(45455),
                PerpetualGrandTotalCents = 120000,
                SignatureHash = "DUMMY_CLOSURE_HASH_123",
                SealedByUserName = "Manager Test",
                SealedByUserId = Guid.NewGuid()
            };
            db.DailyFiscalClosures.Add(closure);
            await db.SaveChangesAsync();
            closureId = closure.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // 1er print latest-closure -> duplicateNumber = 1
        var resp1 = await client.PostAsync("/api/fiscal/latest-closure/print?terminalId=POS_MAIN_TERM", null);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        json1.GetProperty("printQueued").GetBoolean().Should().BeTrue();
        json1.GetProperty("duplicateNumber").GetInt32().Should().Be(1);

        // 2ème print latest-closure -> duplicateNumber = 2
        var resp2 = await client.PostAsync("/api/fiscal/latest-closure/print?terminalId=POS_MAIN_TERM", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var json2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        json2.GetProperty("printQueued").GetBoolean().Should().BeTrue();
        json2.GetProperty("duplicateNumber").GetInt32().Should().Be(2);

        // Vérifier journal
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jet = await db.JournalEntries
                .Where(e => e.EventType == JournalEventTypes.DuplicatePrinted && e.PayloadJson.Contains(closureId.ToString()))
                .ToListAsync();

            jet.Should().HaveCount(2);
            jet.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":1"));
            jet.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":2"));
        }
    }

    [Fact]
    public async Task RetryPrintJob_WhenFailed_RetriesOriginal_WithoutDuplicateOrJet()
    {
        using var factory = new PosApiApplicationFactory();

        Guid jobId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var job = new PrintJob
            {
                PrinterId = Guid.NewGuid(),
                Kind = PrintJobKind.Receipt,
                DocumentJson = "{}",
                Status = PrintJobStatus.Failed,
                Attempts = 3,
                LastError = "Paper jam",
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-10),
                NextAttemptAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                DeadlineAtUtc = DateTimeOffset.UtcNow.AddMinutes(20)
            };
            db.PrintJobs.Add(job);
            await db.SaveChangesAsync();
            jobId = job.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        var resp = await client.PostAsync($"/api/print-jobs/{jobId}/retry", null);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await resp.Content.ReadFromJsonAsync<PrintJobDto>();

        dto.Should().NotBeNull();
        dto!.Id.Should().Be(jobId);
        dto.Status.Should().Be("Pending");
        dto.Attempts.Should().Be(0);
        dto.DuplicateNumber.Should().BeNull();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jetCount = await db.JournalEntries
                .CountAsync(e => e.EventType == JournalEventTypes.DuplicatePrinted);
            jetCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task RetryPrintJob_WhenSent_CreatesDuplicateJob_AndLogsJet()
    {
        using var factory = new PosApiApplicationFactory();
        await SeedPrinterAsync(factory);

        Guid originalJobId;
        Guid docId = Guid.NewGuid();
        Guid printerId;

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            printerId = (await db.PrinterConfigurations.FirstAsync()).Id;

            var dummyDoc = new TicketDocument("fr", false, [
                new TicketText("Ticket Original", TicketAlign.Center)
            ]);

            var job = new PrintJob
            {
                PrinterId = printerId,
                Kind = PrintJobKind.Receipt,
                DocumentJson = TicketDocumentJson.Serialize(dummyDoc),
                Status = PrintJobStatus.Sent,
                Attempts = 1,
                SentAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
                DuplicateOfDocumentId = docId,
                DuplicateNumber = null,
                CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
                NextAttemptAtUtc = DateTimeOffset.UtcNow,
                DeadlineAtUtc = DateTimeOffset.UtcNow.AddMinutes(25)
            };
            db.PrintJobs.Add(job);
            await db.SaveChangesAsync();
            originalJobId = job.Id;
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");

        // 1er retry sur le job Sent -> crée duplicata n°1
        var resp1 = await client.PostAsync($"/api/print-jobs/{originalJobId}/retry", null);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto1 = await resp1.Content.ReadFromJsonAsync<PrintJobDto>();

        dto1.Should().NotBeNull();
        dto1!.Id.Should().NotBe(originalJobId);
        dto1.Status.Should().Be("Pending");
        dto1.DuplicateNumber.Should().Be(1);
        dto1.DuplicateOfDocumentId.Should().Be(docId);

        // 2ème retry sur le même job original Sent -> crée duplicata n°2
        var resp2 = await client.PostAsync($"/api/print-jobs/{originalJobId}/retry", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto2 = await resp2.Content.ReadFromJsonAsync<PrintJobDto>();

        dto2.Should().NotBeNull();
        dto2!.Id.Should().NotBe(originalJobId);
        dto2.Id.Should().NotBe(dto1.Id);
        dto2.Status.Should().Be("Pending");
        dto2.DuplicateNumber.Should().Be(2);
        dto2.DuplicateOfDocumentId.Should().Be(docId);

        // Vérifier journal
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var jet = await db.JournalEntries
                .Where(e => e.EventType == JournalEventTypes.DuplicatePrinted && e.PayloadJson.Contains(docId.ToString()))
                .ToListAsync();

            jet.Should().HaveCount(2);
            jet.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":1"));
            jet.Should().Contain(e => e.PayloadJson.Contains("\"duplicateNumber\":2"));
        }
    }
}
