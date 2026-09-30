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
using Xunit;

namespace RestaurantPos.Api.Tests;

/// <summary>Impression des rapports X/Z : file d'attente et routes de réimpression.</summary>
public class ReportPrintApiTests
{
    private static async Task<HttpClient> ClientAsync(PosApiApplicationFactory factory, string pin)
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest(pin))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return client;
    }

    /// <summary>Garantit une imprimante RECEIPT active (le seed peut ne pas tourner en Testing).</summary>
    private static void EnsurePrinters(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!db.PrinterConfigurations.Any(p => p.IsActive && p.AssignedStationIds.Contains("RECEIPT")))
            db.PrinterConfigurations.Add(new PrinterConfiguration { Name = "Caisse", IpAddress = "10.0.0.1", AssignedStationIds = ["RECEIPT"] });
        db.SaveChanges();
    }

    private static List<PrintJob> Jobs(PosApiApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AppDbContext>().PrintJobs.AsNoTracking().ToList();
    }

    [Fact]
    public async Task ZClosure_Success_QueuesReport_AndReportsPrintQueued()
    {
        using var factory = new PosApiApplicationFactory();
        var client = await ClientAsync(factory, "1234");
        EnsurePrinters(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var open = db.Orders.Where(o => o.Status == OrderStatus.Open && o.Items.Count > 0).ToList();
            db.Orders.RemoveRange(open);
            await db.SaveChangesAsync();
        }

        var res = await client.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest("POS_MAIN_TERM", Guid.NewGuid(), "Gérant"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printQueued").GetBoolean().Should().BeTrue();
        Jobs(factory).Should().ContainSingle().Which.Kind.Should().Be(PrintJobKind.Report);
    }

    [Fact]
    public async Task XReportPrint_QueuesReport()
    {
        using var factory = new PosApiApplicationFactory();
        var client = await ClientAsync(factory, "1234");
        EnsurePrinters(factory);

        var res = await client.PostAsync("/api/fiscal/x-report/print?terminalId=POS_MAIN_TERM", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printQueued").GetBoolean().Should().BeTrue();
        Jobs(factory).Should().ContainSingle().Which.Kind.Should().Be(PrintJobKind.Report);
    }

    [Fact]
    public async Task LatestClosurePrint_WithoutClosure_Returns404_WithClosure_Queues()
    {
        using var factory = new PosApiApplicationFactory();
        var client = await ClientAsync(factory, "1234");
        EnsurePrinters(factory);

        (await client.PostAsync("/api/fiscal/latest-closure/print?terminalId=POS_MAIN_TERM", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var open = db.Orders.Where(o => o.Status == OrderStatus.Open && o.Items.Count > 0).ToList();
            db.Orders.RemoveRange(open);
            await db.SaveChangesAsync();
        }
        (await client.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest("POS_MAIN_TERM", Guid.NewGuid(), "Gérant"))).EnsureSuccessStatusCode();

        var res = await client.PostAsync("/api/fiscal/latest-closure/print?terminalId=POS_MAIN_TERM", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("printQueued").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ReportPrint_WaiterForbidden()
    {
        using var factory = new PosApiApplicationFactory();
        var waiter = await ClientAsync(factory, "2468");

        (await waiter.PostAsync("/api/fiscal/x-report/print?terminalId=POS_MAIN_TERM", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsync("/api/fiscal/latest-closure/print?terminalId=POS_MAIN_TERM", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
