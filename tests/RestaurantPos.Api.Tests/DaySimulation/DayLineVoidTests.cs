using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Annulations de lignes envoyées et corrections de lignes non envoyées dans la journée simulée (commandes <c>v0</c> à <c>v10</c>) :
/// réponses de la route, absence d'effet fiscal, JET <c>ORDER_LINE_VOIDED</c>, lignes <c>OrderLineVoids</c>, articles de bon et bons
/// d'annulation, contre les attentes indépendantes de <see cref="DayLedgerOracle"/>.
/// </summary>
public class DayLineVoidTests(DayFixture day) : IClassFixture<DayFixture>
{
    private List<DayOperation> VoidOrders => day.Scenario.Operations.Where(o => o.LineVoids is { Count: > 0 }).ToList();

    private int OpIndex(DayOperation op) => day.Scenario.Operations.ToList().IndexOf(op);

    private string Ctx(DayOperation op, string what) => $"{DayRunner.Context(day.Scenario.Seed, OpIndex(op), op.Terminal)}, {op.Ref}, {what}";

    [Fact]
    public void EveryStepGetsTheExpectedAnswer_AndAcceptedStepsReturnTheOracleTotal()
    {
        day.Run.LineVoidAttempts.Should().HaveCount(VoidOrders.Sum(o => o.LineVoids!.Count), "chaque étape est jouée une fois");
        foreach (var op in VoidOrders)
        {
            var totals = DayLedgerOracle.TotalsAfterSteps(op);
            var attempts = day.Run.LineVoidAttempts.Where(a => a.Ref == op.Ref).OrderBy(a => a.Step).ToList();
            attempts.Select(a => a.Step).Should().Equal(Enumerable.Range(0, op.LineVoids!.Count), Ctx(op, "étapes"));
            foreach (var attempt in attempts)
            {
                var step = op.LineVoids![attempt.Step];
                string because = Ctx(op, $"étape {attempt.Step} ({step.Phase}, ligne {step.Line} x{step.Quantity}) : {attempt.Message}");
                attempt.Status.Should().Be(step.Expect, because);
                attempt.Code.Should().Be(step.Code, because);
                attempt.TotalCents.Should().Be(totals[attempt.Step], $"{because} : total TTC après l'étape");
                if (step.Code == "order_has_receipts")
                {
                    attempt.ActiveReceipts.Should().NotBeEmpty(because);
                    attempt.ActiveReceipts.Should().OnlyContain(n => attempt.Message!.Contains(n, StringComparison.Ordinal), $"{because} : numéros des reçus listés");
                }
            }
        }
    }

    [Fact]
    public void VoidsAndCorrectionsHaveNoFiscalEffect()
    {
        foreach (var attempt in day.Run.LineVoidAttempts)
        {
            string because = $"{DayRunner.Context(day.Scenario.Seed, attempt.OpIndex, attempt.Key)}, {attempt.Ref} étape {attempt.Step} : X avant = X après";
            DayAssert.SummariesEqual(attempt.XAfter, attempt.XBefore, because);
        }
    }

    [Fact]
    public async Task TheJournalAndTheVoidRowsMatchTheAcceptedVoids()
    {
        var expected = DayLedgerOracle.ExpectedLineVoids(day.Scenario.Operations);
        expected.Should().NotBeEmpty();
        string manager = day.Run.Manager.Name;

        var page = await DayRunner.SendAsync(day.Run.Clients.Values.First(), HttpMethod.Get,
            $"/api/fiscal/journal?eventType={JournalEventTypes.OrderLineVoided}&pageSize=200", null, day.Run.Manager.Token);
        var jet = page.GetProperty("items").EnumerateArray().Select(i =>
        {
            using var doc = JsonDocument.Parse(i.GetProperty("payloadJson").GetString()!);
            var p = doc.RootElement;
            return (Table: p.GetProperty("tableNumber").GetString(), Product: p.GetProperty("productName").GetString(), Qty: p.GetProperty("quantity").GetInt32(),
                Price: p.GetProperty("unitPriceCents").GetInt64(), Reason: p.GetProperty("reason").GetString(), Operator: p.GetProperty("operatorName").GetString(),
                HasTicket: p.GetProperty("ticketId").ValueKind != JsonValueKind.Null);
        }).ToList();
        jet.Should().BeEquivalentTo(expected.Select(e => (Table: (string?)e.Table, Product: (string?)e.ProductName, Qty: e.Quantity, Price: e.UnitPriceCents,
            Reason: (string?)e.Reason, Operator: (string?)manager, HasTicket: e.HasTicket)), "une écriture du JET par annulation acceptée, aucune pour une correction ou un refus");

        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = (await db.OrderLineVoids.AsNoTracking().ToListAsync())
            .Select(r => (r.TableNumber, r.ProductName, r.Quantity, Price: r.UnitPrice.AmountInCents, r.Reason, r.ServerName, r.OperatorName, HasTicket: r.TicketId != null))
            .ToList();
        rows.Should().BeEquivalentTo(expected.Select(e => (TableNumber: e.Table, e.ProductName, e.Quantity, Price: e.UnitPriceCents, e.Reason, e.ServerName,
            OperatorName: manager, e.HasTicket)), "une ligne OrderLineVoids par annulation acceptée (R2 serveur, R9 prix + suppléments)");
    }

    [Fact]
    public async Task KitchenTicketItemsCarryTheCancelledQuantities()
    {
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var op in VoidOrders)
        {
            var lineIds = day.Run.LineIds[op.Ref!];
            var expected = DayLedgerOracle.TicketLines(op);
            foreach (var (lineId, index) in lineIds.Select((id, i) => (id, i)))
            {
                var items = await db.KitchenTicketItems.AsNoTracking().Where(t => t.OrderItemId == lineId).ToListAsync();
                var want = expected.SingleOrDefault(t => t.Line == index);
                string because = Ctx(op, $"ligne {index}");
                if (want is null)
                {
                    items.Should().BeEmpty($"{because} : jamais envoyée, ou ancienne (article sans OrderItemId)");
                    continue;
                }

                var item = items.Should().ContainSingle(because).Subject;
                item.Quantity.Should().Be(want.Sent, because);
                item.CancelledQuantity.Should().Be(want.Cancelled, because);
                item.Status.Should().Be(want.Cancelled >= want.Sent ? TicketItemStatus.Cancelled : TicketItemStatus.Pending, because);
            }

            foreach (var step in op.LineVoids!.Where(s => s.ReAdd > 0))
            {
                // le même produit sur deux bons : la ligne rajoutée part sur un nouveau bon
                var first = lineIds[step.Line];
                var original = await db.KitchenTicketItems.AsNoTracking().SingleAsync(t => t.OrderItemId == first);
                var last = lineIds[^1];
                var readded = await db.KitchenTicketItems.AsNoTracking().SingleAsync(t => t.OrderItemId == last);
                readded.TicketId.Should().NotBe(original.TicketId, Ctx(op, "ligne rajoutée"));
                readded.ProductId.Should().Be(original.ProductId, Ctx(op, "même produit"));
            }
        }
    }

    [Fact]
    public async Task EachVoidQueuesOneCancellationSlip_AndCorrectionsNone()
    {
        var expected = DayLedgerOracle.ExpectedLineVoids(day.Scenario.Operations);
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // l'imprimante du rejeu (DayRunner) ; la base de démonstration en a d'autres sur le même poste
        var printer = await db.PrinterConfigurations.AsNoTracking().SingleAsync(p => p.Name == "Cuisine chaude");
        var reasons = (await db.PrintJobs.AsNoTracking().Where(j => j.PrinterId == printer.Id && j.Kind == PrintJobKind.KitchenTicket).Select(j => j.DocumentJson).ToListAsync())
            .Select(json => TicketDocumentJson.Deserialize(json).Lines.OfType<TicketText>().Select(t => t.Text).FirstOrDefault(t => t.StartsWith("Motif : ", StringComparison.Ordinal)))
            .Where(r => r is not null)
            .ToList();
        reasons.Should().BeEquivalentTo(expected.Select(e => $"Motif : {e.Reason}"), "un bon d'annulation par annulation acceptée, aucun pour une correction ou un refus");
    }

    [Fact]
    public async Task TheEmptiedOrderStaysOnItsTable_AtZero_WithoutReceipt_AndDoesNotBlockTheZ()
    {
        var emptied = VoidOrders.Single(o => DayLedgerOracle.OrderTotalCents(o) == 0 && o.Payments!.Count == 0);
        using var scope = day.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.TableNumber == emptied.Table && o.Items.Count == 0);
        string because = Ctx(emptied, "commande vidée");
        order.TotalTtc.AmountInCents.Should().Be(0, because);
        PaymentSettlementRules.IsSettleable(order.Status).Should().BeTrue($"{because} : la commande reste ouverte (aucune règle ne la clôt)");
        (await db.FiscalReceipts.CountAsync(r => r.OrderId == order.Id)).Should().Be(0, because);
        day.Run.ZClosures[emptied.Terminal!].Should().ContainSingle($"{because} : une commande sans ligne ne bloque pas la Z");
    }
}
