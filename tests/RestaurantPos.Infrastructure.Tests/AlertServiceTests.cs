using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class AlertServiceTests
{
    private static readonly Guid Waiter = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("AlertDb_" + Guid.NewGuid().ToString("N")).Options);

    private static (Order Order, FiscalReceipt Receipt) Sale(AppDbContext db, Guid op, bool credit = false, long cents = 1_000, DiscountType? discount = null, decimal discountValue = 0, bool compLine = false, string terminal = "T01")
    {
        var order = new Order { OperatorId = op, Status = OrderStatus.Paid, TableNumber = "1", GlobalDiscountType = discount, GlobalDiscountValue = discountValue };
        order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10m, IsComp = compLine });
        var receipt = new FiscalReceipt { TerminalId = terminal, ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = order.Id, TotalTtcAmount = Money.FromCents(credit ? -cents : cents), VoidedReceiptId = credit ? Guid.NewGuid() : null };
        db.Orders.Add(order);
        db.FiscalReceipts.Add(receipt);
        db.SaveChanges();
        return (order, receipt);
    }

    [Fact]
    public async Task Voids_RaiseOneAlertWhenTheThresholdIsExceeded_NamingCountAmountAndReceipts_WithoutDuplicates()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.VoidsCount, 3, true, null);
        var receipts = Enumerable.Range(0, 3).Select(_ => Sale(db, Waiter, credit: true, cents: 1_500).Receipt).ToList();

        (await svc.EvaluateAsync()).Should().Be(0); // 3 pour un seuil de 3 : pas dépassé
        receipts.Add(Sale(db, Waiter, credit: true, cents: 1_500).Receipt);
        Sale(db, Other, credit: true); // un autre serveur sous le seuil

        (await svc.EvaluateAsync()).Should().Be(1);
        (await svc.EvaluateAsync()).Should().Be(0); // relancer l'évaluation : aucun doublon
        var alert = (await svc.ListAsync("open", null, null)).Single();
        alert.RuleType.Should().Be("voids_count");
        alert.OperatorId.Should().Be(Waiter);
        alert.PeriodKey.Should().Be("T01:0");
        var detail = JsonDocument.Parse(alert.DetailJson).RootElement;
        detail.GetProperty("count").GetInt32().Should().Be(4);
        detail.GetProperty("totalCents").GetInt64().Should().Be(6_000);
        detail.GetProperty("receiptIds").EnumerateArray().Select(e => e.GetGuid()).Should().BeEquivalentTo(receipts.Select(r => r.Id));
        svc.TakeChanged().Should().BeTrue();
        svc.TakeChanged().Should().BeFalse();
    }

    [Fact]
    public async Task AnOpenAlert_FollowsTheCurrentCount_AResolvedOneStaysFrozen()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.VoidsCount, 3, true, null);
        for (var i = 0; i < 4; i++) Sale(db, Waiter, credit: true);
        (await svc.EvaluateAsync()).Should().Be(1);
        svc.TakeChanged().Should().BeTrue();

        for (var i = 0; i < 8; i++) Sale(db, Waiter, credit: true);
        (await svc.EvaluateAsync()).Should().Be(0); // mise à jour, pas une nouvelle alerte
        svc.TakeChanged().Should().BeTrue();        // mais l'écran doit se rafraîchir
        var open = (await svc.ListAsync("open", null, null)).Single();
        JsonDocument.Parse(open.DetailJson).RootElement.GetProperty("count").GetInt32().Should().Be(12);

        await svc.ResolveAsync(open.Id, "Vu avec le serveur", Waiter);
        for (var i = 0; i < 3; i++) Sale(db, Waiter, credit: true);
        await svc.EvaluateAsync();
        var resolved = (await svc.ListAsync("resolved", null, null)).Single();
        JsonDocument.Parse(resolved.DetailJson).RootElement.GetProperty("count").GetInt32().Should().Be(12); // figée
        (await svc.ListAsync("open", null, null)).Should().BeEmpty();
    }

    [Fact]
    public async Task ALineDiscount_CountsAsADiscount_ButACompLineDoesNot()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.DiscountPercent, 30, true, null);
        var (lineDiscounted, _) = Sale(db, Waiter, cents: 1_000);
        lineDiscounted.Items.Single().DiscountPercent = 50m;
        var (comp, _) = Sale(db, Other, cents: 1_000, compLine: true);
        comp.Items.Single().DiscountPercent = 90m; // une ligne offerte n'est pas une remise
        db.SaveChanges();

        (await svc.EvaluateAsync()).Should().Be(1);
        (await svc.ListAsync("open", null, null)).Single().OperatorId.Should().Be(Waiter);
    }

    [Fact]
    public async Task DisabledRules_RaiseNothing()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        Enumerable.Range(0, 10).ToList().ForEach(_ => Sale(db, Waiter, credit: true));

        (await svc.EvaluateAsync()).Should().Be(0);
        (await svc.RulesAsync()).Should().OnlyContain(r => !r.Enabled);
    }

    [Fact]
    public async Task AZ_StartsANewPeriod_SoTheSameOperatorCanAlertAgain()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.VoidsCount, 1, true, null);
        Enumerable.Range(0, 2).ToList().ForEach(_ => Sale(db, Waiter, credit: true));
        await svc.EvaluateAsync();

        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = "T01", ClosureSequence = 1, PeriodStartUtc = DateTimeOffset.UtcNow.AddHours(-8), PeriodEndUtc = DateTimeOffset.UtcNow.AddSeconds(-1), SignatureHash = "h", PreviousSignatureHash = "g" });
        db.SaveChanges();
        await Task.Delay(20);
        Enumerable.Range(0, 2).ToList().ForEach(_ => Sale(db, Waiter, credit: true));

        (await svc.EvaluateAsync()).Should().Be(1);
        (await svc.ListAsync(null, null, null)).Select(a => a.PeriodKey).Should().BeEquivalentTo(["T01:0", "T01:1"]);
    }

    [Fact]
    public async Task Discounts_AboveThePercent_MentionTheOrderAndTheAuthor_FixedAmountsAreConverted_CompsAreSummed()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.DiscountPercent, 30, true, null);
        await svc.SetRuleAsync(AlertRuleTypes.CompsTotal, 5_000, true, null);
        var big = Sale(db, Waiter, discount: DiscountType.Percentage, discountValue: 40m).Order;
        Sale(db, Other, discount: DiscountType.Percentage, discountValue: 10m);
        Sale(db, Other, cents: 3_000, compLine: true);
        Sale(db, Other, cents: 3_000, compLine: true);

        (await svc.EvaluateAsync()).Should().Be(2);
        var alerts = await svc.ListAsync("open", null, null);
        var discount = alerts.Single(a => a.RuleType == "discount_percent");
        discount.OperatorId.Should().Be(Waiter);
        JsonDocument.Parse(discount.DetailJson).RootElement.GetProperty("orderIds").EnumerateArray().Single().GetGuid().Should().Be(big.Id);
        var comps = alerts.Single(a => a.RuleType == "comps_total");
        comps.OperatorId.Should().Be(Other);
        JsonDocument.Parse(comps.DetailJson).RootElement.GetProperty("totalCents").GetInt64().Should().Be(6_000);
    }

    [Fact]
    public async Task Resolving_NeedsANote_KeepsTheAlertInHistory_AndALaterEvaluationDoesNotReopenIt()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.VoidsCount, 0, true, null);
        Sale(db, Waiter, credit: true);
        await svc.EvaluateAsync();
        var alert = (await svc.ListAsync("open", null, null)).Single();

        await FluentActions.Awaiting(() => svc.ResolveAsync(alert.Id, "  ", Waiter)).Should().ThrowAsync<AlertException>().Where(e => e.Code == "note_required");
        var resolved = await svc.ResolveAsync(alert.Id, "Erreur de saisie corrigée", Other);

        resolved!.State.Should().Be("resolved");
        resolved.ResolutionNote.Should().Be("Erreur de saisie corrigée");
        (await svc.EvaluateAsync()).Should().Be(0);
        (await svc.ListAsync("open", null, null)).Should().BeEmpty();
        (await svc.ListAsync("resolved", null, null)).Should().ContainSingle();
        (await svc.ResolveAsync(Guid.NewGuid(), "x", Waiter)).Should().BeNull();
    }

    [Fact]
    public async Task CashVariance_AboveTheThreshold_RaisesAnAlert_ForTheSessionOpener()
    {
        using var db = NewDb();
        var svc = new AlertService(db);
        await svc.SetRuleAsync(AlertRuleTypes.CashVariance, 1_000, true, null);
        var cash = new CashSessionService(db, svc);
        var session = await cash.OpenAsync("T01", Waiter, 10_000);

        var result = await cash.CountAsync(session.Id, 8_500, null, "Rendu erroné", Waiter);

        result!.AlertCreated.Should().BeTrue();
        var alert = (await svc.ListAsync("open", null, null)).Single();
        alert.RuleType.Should().Be("cash_variance");
        alert.OperatorId.Should().Be(Waiter);
        JsonDocument.Parse(alert.DetailJson).RootElement.GetProperty("varianceCents").GetInt64().Should().Be(-1_500);
    }

    [Fact]
    public async Task RulesAndSettings_AreValidated()
    {
        using var db = NewDb();
        var svc = new AlertService(db);

        (await svc.SetRuleAsync("inconnue", 1, true, null)).Should().BeNull();
        await FluentActions.Awaiting(() => svc.SetRuleAsync(AlertRuleTypes.VoidsCount, -1, true, null)).Should().ThrowAsync<AlertException>();
        (await svc.GetSettingsAsync()).Should().Be(new AlertSettings(15, true));
        (await svc.UpdateSettingsAsync(new AlertSettings(20, false))).Should().Be(new AlertSettings(20, false));
        await FluentActions.Awaiting(() => svc.UpdateSettingsAsync(new AlertSettings(0, true))).Should().ThrowAsync<AlertException>();
    }
}
