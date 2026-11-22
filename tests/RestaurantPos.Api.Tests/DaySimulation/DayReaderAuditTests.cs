using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Lecteurs des commandes rouvertes par l'annulation d'une part (<c>v26</c> à <c>v28</c>) : Z refusée avec le reste dû de l'oracle, table du
/// client suivant jamais prise ni libérée, puis, une fois la commande re-soldée, une seule vente aux yeux des lecteurs (statut, pourboire,
/// reste dû, commandes ouvertes).
/// </summary>
public class DayReaderAuditTests(DayFixture day) : IClassFixture<DayFixture>
{
    private string Ctx(string reference, string what)
    {
        var op = day.Scenario.Operations.Single(o => o.Ref == reference);
        return $"{DayRunner.Context(day.Scenario.Seed, day.Scenario.Operations.ToList().IndexOf(op), op.Terminal)}, {reference}, {what}";
    }

    [Fact]
    public void AReopenedOrder_BlocksTheZ_WithTheRemainingDueOfTheOracle()
    {
        var expected = DayLedgerOracle.ExpectedZBlocked(day.Scenario.Operations);
        expected.Should().HaveCount(3, "v26, v27, v28");
        day.Run.ZBlockedAttempts.Should().HaveCount(expected.Count);
        foreach (var attempt in day.Run.ZBlockedAttempts)
        {
            var op = day.Scenario.Operations.Single(o => o.Ref == attempt.Ref);
            string because = Ctx(attempt.Ref, $"Z tentée après l'étape {attempt.Step}");
            attempt.Status.Should().Be(409, because);
            attempt.Body.GetProperty("code").GetString().Should().Be("open_orders", because);
            var open = attempt.Body.GetProperty("openOrders").EnumerateArray().ToList();
            open.Should().ContainSingle(because); // la commande du client suivant est vide : elle ne bloque pas
            open[0].GetProperty("tableNumber").GetString().Should().Be(op.Table, because);
            DayAssert.Cents(open[0].GetProperty("remainingTtc")).Should().Be(expected[$"{attempt.Ref}:{attempt.Step}"], $"{because} : TTC + pourboire actif − règlements actifs");
        }
    }

    [Fact]
    public void AReopenedOrderWhoseTableWasTaken_NeitherTakesNorFreesTheTableOfTheNextCustomer()
    {
        day.Run.TableChecks.Select(c => c.Stage).Should().Equal("after_void", "after_paid");
        foreach (var check in day.Run.TableChecks)
        {
            check.ActiveOrderId.Should().Be(check.Occupant, Ctx(check.Ref, $"{check.Stage} : la table reste au client installé entre-temps"));
            check.ActiveOrderId.Should().NotBe(check.OrderId);
        }
    }

    [Fact]
    public async Task ReopenedThenRepaidOrders_AreOneSettledSaleForTheReaders()
    {
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fiscal = scope.ServiceProvider.GetRequiredService<INF525FiscalAuditService>();
        var openAtEnd = await fiscal.FindOpenOrdersAsync();
        foreach (var reference in new[] { "v26", "v27", "v28" })
        {
            var op = day.Scenario.Operations.Single(o => o.Ref == reference);
            string because = Ctx(reference, "après re-règlement");
            var orderId = await OrderIdAsync(db, op);
            var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
            long netTip = DayLedgerOracle.NetTipsCents([op]);
            order.Status.Should().Be(OrderStatus.Paid, because);
            order.TipAmount.AmountInCents.Should().Be(netTip, $"{because} : pourboire des parts encore actives seulement");
            long due = DayLedgerOracle.OrderTotalCents(op) + netTip;
            (await PaymentSettlementRules.PaidCentsAsync(db, orderId)).Should().Be(due, $"{because} : règlements actifs = TTC + pourboire");
            openAtEnd.Should().NotContain(o => o.OrderId == orderId, because);
            (await db.HeldOrders.AsNoTracking().AnyAsync(h => h.OrderId == orderId)).Should().BeFalse(because);
        }
    }

    /// <summary>La commande réglée d'une opération (tables 43 à 45 propres à ces cas ; la commande vide du client suivant reste ouverte).</summary>
    private static async Task<Guid> OrderIdAsync(AppDbContext db, DayOperation op)
    {
        var table = op.Table!;
        var candidates = await db.Orders.AsNoTracking().Where(o => o.TableNumber == table && o.Status == OrderStatus.Paid).Select(o => o.Id).ToListAsync();
        candidates.Should().ContainSingle($"{op.Ref} : une seule commande réglée sur la table {table}");
        return candidates[0];
    }
}
