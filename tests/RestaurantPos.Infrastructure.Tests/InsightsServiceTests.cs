using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class InsightsServiceTests
{
    // Mercredi 4 novembre 2026, 15 h UTC ; même jour de la semaine précédente : mercredi 28 octobre.
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Insights_" + Guid.NewGuid().ToString("N")).Options);

    private static InsightsService Service(AppDbContext db) =>
        new(db, new FinancialDashboardService(db), new NF525FiscalAuditService(db), new CashSessionService(db), new AlertService(db));

    /// <summary>Une vente à l'heure donnée (reçu + commande payée), éventuellement à emporter.</summary>
    private static void Sale(AppDbContext db, DateTimeOffset at, long cents, bool takeaway = false)
    {
        var order = new Order { Status = OrderStatus.Paid, TableNumber = "1", CreatedAtUtc = at, Destination = takeaway ? OrderDestination.Takeaway : OrderDestination.EatIn };
        order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10m });
        db.Orders.Add(order);
        db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = order.Id, TotalTtcAmount = Money.FromCents(cents), CreatedAtUtc = at });
        db.SaveChanges();
    }

    private static void History(AppDbContext db, int days, long cents)
    {
        for (var d = 1; d <= days; d++) Sale(db, Now.AddDays(-d).AddHours(-3), cents);
    }

    [Fact]
    public async Task AverageTicketDown20Percent_RaisesTheCardWithThePercentage()
    {
        using var db = NewDb();
        History(db, 5, 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddDays(-7).AddHours(-4 + i * 0.1), 3_900); // référence : mercredi dernier avant 15 h
        for (var i = 0; i < 10; i++) Sale(db, Now.AddHours(-4 + i * 0.1), 3_120);

        var result = await Service(db).GetInsightsAsync(0, Now);

        result.InsufficientData.Should().BeFalse();
        var card = result.Cards.Single(c => c.Metric == "avgTicket");
        card.CurrentCents.Should().Be(3_120);
        card.ReferenceCents.Should().Be(3_900);
        card.DeltaPercent.Should().Be(-20.0m);
    }

    private static readonly System.Text.Json.JsonSerializerOptions Web = new(System.Text.Json.JsonSerializerDefaults.Web);

    /// <summary>Corps JSON de `/api/dashboard/insights` (sérialisation Web de l'API) : l'iPad le décode (`insights.json`). WRITE_INSIGHTS_FIXTURE=1 pour régénérer.</summary>
    [Fact]
    public async Task TheInsightsResponse_MatchesTheFixtureDecodedByTheIpad()
    {
        using var db = NewDb();
        History(db, 5, 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddDays(-7).AddHours(-4 + i * 0.1), 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddHours(-4 + i * 0.1), 3_120);
        var json = System.Text.Json.JsonSerializer.Serialize(await Service(db).GetInsightsAsync(0, Now), Web) + "\n";

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
        var path = Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "insights.json");
        if (Environment.GetEnvironmentVariable("WRITE_INSIGHTS_FIXTURE") == "1") File.WriteAllText(path, json);
        File.ReadAllText(path).Should().Be(json, "régénérer avec WRITE_INSIGHTS_FIXTURE=1");
    }

    [Fact]
    public async Task AGapBelowTheThreshold_RaisesNoCard()
    {
        using var db = NewDb();
        History(db, 5, 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddDays(-7).AddHours(-4 + i * 0.1), 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddHours(-4 + i * 0.1), 3_700); // −5,1 % : sous les 15 %

        (await Service(db).GetInsightsAsync(0, Now)).Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task LessThanThreeDaysOfData_MeansInsufficientDataAndNoCard()
    {
        using var db = NewDb();
        Sale(db, Now.AddDays(-7).AddHours(-4), 3_900);
        Sale(db, Now.AddHours(-4), 500);

        var result = await Service(db).GetInsightsAsync(0, Now);

        result.InsufficientData.Should().BeTrue();
        result.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task ADayWithoutSales_RaisesNoCard()
    {
        using var db = NewDb();
        History(db, 6, 3_900);

        var result = await Service(db).GetInsightsAsync(0, Now);

        result.InsufficientData.Should().BeFalse();
        result.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task TakeawayShare_IsReportedInBasisPoints()
    {
        using var db = NewDb();
        History(db, 5, 3_000);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddDays(-7).AddHours(-4 + i * 0.1), 3_000, takeaway: i < 2);   // 20 % à emporter
        for (var i = 0; i < 10; i++) Sale(db, Now.AddHours(-4 + i * 0.1), 3_000, takeaway: i < 5);               // 50 %
        var card = (await Service(db).GetInsightsAsync(0, Now)).Cards.Single(c => c.Metric == "takeawayShare");

        card.CurrentCents.Should().Be(5_000);
        card.ReferenceCents.Should().Be(2_000);
        card.DeltaPercent.Should().Be(150.0m);
    }

    [Fact]
    public async Task TheTrendThreshold_IsConfigurable()
    {
        using var db = NewDb();
        History(db, 5, 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddDays(-7).AddHours(-4 + i * 0.1), 3_900);
        for (var i = 0; i < 10; i++) Sale(db, Now.AddHours(-4 + i * 0.1), 3_120); // −20 %
        var settings = new AlertService(db);

        await settings.UpdateSettingsAsync(new AlertSettings(15, true));
        (await Service(db).GetInsightsAsync(0, Now)).Cards.Should().NotBeEmpty();
        await settings.UpdateSettingsAsync(new AlertSettings(25, true));
        (await Service(db).GetInsightsAsync(0, Now)).Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task TheLocalDayFollowsTheOffset_SoASaleAtMidnightUtcBelongsToTheLocalDay()
    {
        using var db = NewDb();
        History(db, 5, 3_900);
        // En UTC+2, il est 17 h le mercredi ; une vente à 22 h UTC mardi (00 h locale mercredi) compte dans « aujourd'hui ».
        Sale(db, new DateTimeOffset(2026, 11, 3, 22, 30, 0, TimeSpan.Zero), 1_000);
        Sale(db, new DateTimeOffset(2026, 10, 27, 22, 30, 0, TimeSpan.Zero), 2_000);

        var result = await Service(db).GetInsightsAsync(120, Now);

        result.Cards.Should().Contain(c => c.Metric == "cumulativeTtc" && c.CurrentCents == 1_000 && c.ReferenceCents == 2_000);
    }

    [Fact]
    public void DeltaPercent_RoundsHalfAwayFromZero_AndIsZeroWithoutReference()
    {
        IInsightsService.DeltaPercent(1_000, 3_000).Should().Be(-66.7m);
        IInsightsService.DeltaPercent(1_005, 1_000).Should().Be(0.5m);
        IInsightsService.DeltaPercent(5, 0).Should().Be(0m);
    }
}
