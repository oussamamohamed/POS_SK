using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Journées courtes rejouées par l'API réelle : X, Z et Z relue doivent égaler le grand livre indépendant, poste par poste.</summary>
public class DaySimulationSmokeTests
{
    [Fact]
    public async Task AMinimalDay_HasXAndZEqualToTheLedger()
    {
        var scenario = new DayScenario(1,
            [new("A", "Caisse A"), new("B", "Caisse B")],
            [
                new("table_order", "A", "waiter1", "5", 2, "EatIn", [new("Menu", 2, 2000, 10m)], null, 0, [[new("Cash", 4000, 1000)]]),
                new("counter_order", "B", "waiter2", null, 0, "Takeaway", [new("Sandwich", 1, 1000, 10m, 5.5m)], null, 0, [[new("CreditCard", 1000)]]),
                new("x_report", "A"), new("x_report", "B"),
                new("z_closure", "A"), new("z_closure", "B"),
            ]);

        await AssertDayAsync(scenario);
    }

    [Fact]
    public async Task ADayWithDiscountsCompSplitTipVoidHoldAndReprint_HasXAndZEqualToTheLedger()
    {
        var scenario = new DayScenario(2,
            [new("A", "Caisse A"), new("B", "Caisse B")],
            [
                // 2 × 11,50 + 6,50 à 10 %, vin offert à 20 %, remise 10 % : 26,55 réglés en deux fois, 3 € de pourboire sur le second
                new("table_order", "A", "waiter1", "1", 3, "EatIn",
                    [new("Plat", 2, 1000, 10m, ModifiersExtraCents: 150), new("Vin", 1, 900, 20m, Comp: true), new("Dessert", 1, 650, 10m)],
                    new("Percentage", 10m), 300, [[new("Cash", 1000, 500)], [new("CreditCard", 1955)]], Reprint: true),
                new("table_order", "B", "waiter2", "2", 2, "EatIn",
                    [new("A", 1, 1000, 10m), new("B", 1, 2000, 20m)], new("FixedAmount", 3m), 0, [[new("CreditCard", 2700)]], VoidPaymentIndex: 0),
                new("counter_order", "B", "waiter3", null, 0, "Takeaway",
                    [new("Sandwich", 1, 1000, 10m, 5.5m), new("Bière", 1, 500, 20m)], null, 0,
                    [[new("CreditCard", 1000)], [new("Cash", 500, 1500)]], HoldAndRecall: true),
                new("counter_order", "A", "waiter1", null, 0, "EatIn", [new("Sandwich", 1, 1000, 10m, 5.5m)], null, 0, [[new("Cash", 1000)]]),
                new("hold_then_void", "A", "waiter2", null, 0, "Takeaway", [new("Café", 1, 200, 10m, 5.5m)]),
                new("x_report", "A"), new("x_report", "B"),
                new("z_closure", "A"), new("z_closure", "B"),
            ]);

        var run = await AssertDayAsync(scenario);
        run.Receipts["A"].Should().HaveCount(3);
        run.Receipts["B"].Select(r => r.Number).Should().Equal("T02-000001", "T02-VOID-000002", "T02-000003", "T02-000004");
    }

    private static async Task<DayRunResult> AssertDayAsync(DayScenario scenario)
    {
        using var factory = new PosApiApplicationFactory();
        var run = await DayRunner.RunAsync(factory, scenario);
        var ledger = DayLedgerOracle.Compute(scenario.Operations, run.TerminalIds);

        foreach (var (key, id) in run.TerminalIds)
        {
            var expected = ledger.ByTerminal[id];
            DayAssert.SummaryEquals(run.XReports[key].Single(), expected, $"X {key}");
            DayAssert.SummaryEquals(run.ZClosures[key].Single(), expected, $"Z {key}");
            DayAssert.SummaryEquals(run.LatestClosures[key].Single(), expected, $"Z relue {key}");
        }

        return run;
    }
}
