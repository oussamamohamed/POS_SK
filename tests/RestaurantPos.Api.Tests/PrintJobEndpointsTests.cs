using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class PrintJobEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    public PrintJobEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> ClientAsync(string pin)
    {
        var client = _factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    private static readonly string ValidDocumentJson = TicketDocumentJson.Serialize(new TicketDocument("fr", false, [new TicketText("x", TicketAlign.Start)]));

    private async Task<(Guid PrinterId, Guid JobId)> SeedJobAsync(PrintJobStatus status, PrintJobKind kind = PrintJobKind.PickupVoucher)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = new PrinterConfiguration { Name = "P-" + Guid.NewGuid().ToString("N")[..6], IpAddress = "10.0.0.9" };
        db.PrinterConfigurations.Add(printer);
        var now = DateTimeOffset.UtcNow;
        var job = new PrintJob { PrinterId = printer.Id, Kind = kind, OpenCashDrawer = true, DocumentJson = ValidDocumentJson, Status = status, Attempts = 7, CreatedAtUtc = now, NextAttemptAtUtc = now, DeadlineAtUtc = now.AddMinutes(-1), LastError = "refused" };
        db.PrintJobs.Add(job);
        await db.SaveChangesAsync();
        return (printer.Id, job.Id);
    }

    private async Task<PrintJob> JobAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking().SingleAsync(j => j.Id == id);
    }

    [Fact]
    public async Task Retry_FailedJob_BecomesPendingWithNewDeadline()
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(PrintJobStatus.Failed);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var job = await JobAsync(jobId);
        job.Status.Should().Be(PrintJobStatus.Pending);
        job.Attempts.Should().Be(0);
        job.DeadlineAtUtc.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Retry_PendingJob_Returns409()
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(PrintJobStatus.Pending);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(PrintJobKind.PickupVoucher)]
    [InlineData(PrintJobKind.KitchenTicket)]
    public async Task Retry_SentNonFiscalJob_Returns409(PrintJobKind kind)
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(PrintJobStatus.Sent, kind);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Retry_SentJobWithUnreadableDocument_Returns409AndCreatesNoDuplicate()
    {
        var manager = await ClientAsync("1234");
        var (printerId, jobId) = await SeedJobAsync(PrintJobStatus.Sent, PrintJobKind.Receipt);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PrintJobs.SingleAsync(j => j.Id == jobId)).DocumentJson = "not json";
            await db.SaveChangesAsync();
        }
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var check = _factory.Services.CreateScope();
        (await check.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.CountAsync(j => j.PrinterId == printerId)).Should().Be(1);
    }

    [Fact]
    public async Task Retry_SentReceipt_CreatesDuplicateWithoutOpeningDrawer()
    {
        var manager = await ClientAsync("1234");
        var (printerId, jobId) = await SeedJobAsync(PrintJobStatus.Sent, PrintJobKind.Receipt);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var duplicate = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking()
            .SingleAsync(j => j.PrinterId == printerId && j.DuplicateOfDocumentId == jobId);
        duplicate.OpenCashDrawer.Should().BeFalse();
    }

    [Theory]
    [InlineData(PrintJobStatus.Pending, HttpStatusCode.OK)]
    [InlineData(PrintJobStatus.Failed, HttpStatusCode.OK)]
    [InlineData(PrintJobStatus.Sent, HttpStatusCode.Conflict)]
    public async Task Cancel_RespectsStatus(PrintJobStatus status, HttpStatusCode expected)
    {
        var manager = await ClientAsync("1234");
        var (_, jobId) = await SeedJobAsync(status);
        (await manager.PostAsync($"/api/print-jobs/{jobId}/cancel", null)).StatusCode.Should().Be(expected);
        if (expected == HttpStatusCode.OK) (await JobAsync(jobId)).Status.Should().Be(PrintJobStatus.Cancelled);
    }

    [Fact]
    public async Task UnknownJob_Returns404()
    {
        var manager = await ClientAsync("1234");
        (await manager.PostAsync($"/api/print-jobs/{Guid.NewGuid()}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Jobs_DefaultsToPendingAndFailed()
    {
        var manager = await ClientAsync("1234");
        var (printerId, failedId) = await SeedJobAsync(PrintJobStatus.Failed);
        var body = await manager.GetFromJsonAsync<JsonElement>($"/api/printers/{printerId}/jobs");
        body.EnumerateArray().Select(j => j.GetProperty("id").GetGuid()).Should().Equal(failedId);
        body[0].GetProperty("status").GetString().Should().Be("Failed");
        (await manager.GetFromJsonAsync<JsonElement>($"/api/printers/{printerId}/jobs?status=Sent")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Status_ListsPrintersWithCounts_ForAnyAuthenticatedUser()
    {
        var waiter = await ClientAsync("2468");
        var (printerId, _) = await SeedJobAsync(PrintJobStatus.Failed);
        var body = await waiter.GetFromJsonAsync<JsonElement>("/api/printers/status");
        var row = body.EnumerateArray().Single(p => p.GetProperty("printerId").GetGuid() == printerId);
        row.GetProperty("failedCount").GetInt32().Should().Be(1);
        row.GetProperty("isOnline").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Waiter_CannotRetryOrListJobs()
    {
        var waiter = await ClientAsync("2468");
        var (printerId, jobId) = await SeedJobAsync(PrintJobStatus.Failed);
        (await waiter.PostAsync($"/api/print-jobs/{jobId}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync($"/api/printers/{printerId}/jobs")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
