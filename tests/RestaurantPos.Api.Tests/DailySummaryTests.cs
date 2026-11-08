using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class DailySummaryTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DailySummaryTests(PosApiApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task SummaryTotals_EqualTheZ_TipsEqualTheTakings_AndNothingFiscalIsWritten()
    {
        var operatorId = Guid.NewGuid();
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        admin.DefaultRequestHeaders.Add("X-Test-Operator-Id", operatorId.ToString());
        var device = await DeviceTestHelper.PairAsync(_factory, admin, "Caisse résumé");
        var term = device.TerminalId;

        (await admin.GetAsync($"/api/dashboard/daily-summary?terminalId={term}")).StatusCode.Should().Be(HttpStatusCode.NotFound); // pas encore de Z

        var session = await (await admin.PostAsJsonAsync("/api/cash/sessions", new { openingFloatCents = 5_000 })).Content.ReadFromJsonAsync<JsonElement>(); // ouverte avant les ventes
        var orderIds = new List<Guid>();
        foreach (var (cents, method) in new[] { (1_000L, PaymentMethod.Cash), (2_450L, PaymentMethod.Cash), (3_000L, PaymentMethod.CreditCard) })
        {
            var id = Guid.NewGuid();
            orderIds.Add(id);
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order { Id = id, TableNumber = "R" + orderIds.Count, Status = OrderStatus.Open, OperatorId = operatorId, Items = { new OrderItem { OrderId = id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = RestaurantPos.Domain.ValueObjects.Money.FromCents(cents), TaxRatePercent = 10m } } });
            await db.SaveChangesAsync();
            (await admin.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(id, null, cents, [new SyncTenderDto(method, cents, cents)], null))).EnsureSuccessStatusCode();
        }
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var first = await db.Orders.FirstAsync(o => o.Id == orderIds[0]);
            first.TipAmount = RestaurantPos.Domain.ValueObjects.Money.FromCents(250);
            await db.SaveChangesAsync();
        }

        // Comptage de caisse de la période, puis Z.
        var count = await admin.PostAsJsonAsync($"/api/cash/sessions/{session.GetProperty("id").GetGuid()}/count", new { countedCents = 8_400 });
        count.EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest(term, Guid.NewGuid(), "Gérant"))).StatusCode.Should().Be(HttpStatusCode.OK);

        int receiptsBefore, closuresBefore;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            receiptsBefore = db.FiscalReceipts.Count();
            closuresBefore = db.DailyFiscalClosures.Count();
        }

        var summary = await admin.GetFromJsonAsync<JsonElement>($"/api/dashboard/daily-summary?terminalId={term}");
        var z = await admin.GetFromJsonAsync<JsonElement>($"/api/fiscal/latest-closure?terminalId={term}");

        if (Environment.GetEnvironmentVariable("WRITE_DAILY_SUMMARY_FIXTURE") == "1") // capture réelle de la réponse, décodée par l'iPad (`daily_summary.json`)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
            File.WriteAllText(Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "daily_summary.json"), summary.GetRawText() + "\n");
        }

        summary.GetProperty("totalSalesTtcCents").GetInt64().Should().Be((long)Math.Round(z.GetProperty("totalSalesTtc").GetDecimal() * 100m));
        summary.GetProperty("totalSalesHtCents").GetInt64().Should().Be((long)Math.Round(z.GetProperty("totalSalesHt").GetDecimal() * 100m));
        summary.GetProperty("receiptCount").GetInt32().Should().Be(z.GetProperty("receiptCount").GetInt32());
        summary.GetProperty("closureSequence").GetInt64().Should().Be(z.GetProperty("closureSequence").GetInt64());
        summary.GetProperty("totalSalesTtcCents").GetInt64().Should().Be(6_450);
        summary.GetProperty("paymentTotalsCents").GetProperty("Cash").GetInt64().Should().Be(3_450);
        summary.GetProperty("paymentTotalsCents").GetProperty("CreditCard").GetInt64().Should().Be(3_000);
        summary.GetProperty("averageTicketCents").GetInt64().Should().Be(2_150);
        summary.GetProperty("cash").GetProperty("varianceCents").GetInt64().Should().Be(8_400 - (5_000 + 3_450));
        z.GetProperty("cashVariance").GetInt64().Should().Be(-50); // le Z porte aussi le comptage (champs non signés)

        var tips = summary.GetProperty("tipsByServer").EnumerateArray().Sum(t => t.GetProperty("tipsCents").GetInt64());
        var takings = await admin.GetFromJsonAsync<JsonElement>($"/api/dashboard/takings?from={DateTimeOffset.UtcNow.AddHours(-2):O}&to={DateTimeOffset.UtcNow.AddHours(1):O}".Replace("+", "%2B"));
        tips.Should().Be((long)Math.Round(takings.GetProperty("tipsTotalTtc").GetDecimal() * 100m));

        using var verify = _factory.Services.CreateScope();
        var after = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        after.FiscalReceipts.Count().Should().Be(receiptsBefore);
        after.DailyFiscalClosures.Count().Should().Be(closuresBefore);
    }

    [Fact]
    public async Task Summary_IsManagerOnly_AndNeedsNoSendChannel()
    {
        var waiter = _factory.CreateClient();
        waiter.DefaultRequestHeaders.Add("X-Test-Role", "Waiter");
        (await waiter.GetAsync("/api/dashboard/daily-summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
