using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Grand livre indépendant : valeurs calculées à la main.</summary>
public class DayOracleTests
{
    private static readonly Dictionary<string, string> T1 = new() { ["T1"] = "T01" };

    [Fact]
    public void OneEatInOrderPaidInCash_IsLedgeredExactly()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "5", 2, "EatIn",
            [new DayLine("Menu", 2, 2000, 10m)], null, 0, [[new DayTender("Cash", 4000, 1000)]]);
        var ledger = DayLedgerOracle.Compute([order], T1);
        var t = ledger.ByTerminal["T01"];
        t.TtcCents.Should().Be(4000);
        t.HtCents.Should().Be(3636); // 4000 / 1,1 = 3636,36
        t.VatByRate[10m].Should().Be(364);
        t.TenderByMethod["Cash"].Should().Be(4000); // les espèces remises en plus (rendu) ne comptent pas
        t.ReceiptCount.Should().Be(1);
        ledger.Total.TtcCents.Should().Be(4000);
    }

    [Fact]
    public void TakeawayLineAtReducedRate_IsLedgeredAtThatRate()
    {
        var order = new DayOperation("counter_order", "T1", "waiter1", null, 0, "Takeaway",
            [new DayLine("Sandwich", 1, 1000, 10m, 5.5m)], null, 0, [[new DayTender("CreditCard", 1000)]]);
        var t = DayLedgerOracle.Compute([order], T1).ByTerminal["T01"];
        t.VatByRate.Should().ContainSingle().Which.Should().Be(new KeyValuePair<decimal, long>(5.5m, 52));
        t.HtCents.Should().Be(948);
    }

    [Fact]
    public void FixedOrderDiscount_IsSpreadOnRates_AndAVoidedReceiptNetsToZero()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "2", 2, "EatIn",
            [new DayLine("A", 1, 1000, 10m), new DayLine("B", 1, 2000, 20m)], new DayOrderDiscount("FixedAmount", 3m), 0,
            [[new DayTender("CreditCard", 2700)]], VoidPaymentIndex: 0);
        var t = DayLedgerOracle.Compute([order], T1).ByTerminal["T01"];
        t.ReceiptCount.Should().Be(2);          // le reçu et son annulation
        t.TtcCents.Should().Be(0);
        t.HtCents.Should().Be(0);
        t.VatByRate.Keys.Should().Equal(10m, 20m);
        t.VatByRate.Values.Should().OnlyContain(v => v == 0);
        t.TenderByMethod["CreditCard"].Should().Be(0);
    }

    [Fact]
    public void FixedOrderDiscount_WithoutVoid_IsSpreadByLargestRemainder()
    {
        // 2700 sur [1000 ; 2000] : 900 à 10 % (HT 818, TVA 82) et 1800 à 20 % (HT 1500, TVA 300).
        var order = new DayOperation("table_order", "T1", "waiter1", "2", 2, "EatIn",
            [new DayLine("A", 1, 1000, 10m), new DayLine("B", 1, 2000, 20m)], new DayOrderDiscount("FixedAmount", 3m), 0,
            [[new DayTender("CreditCard", 2700)]]);
        var t = DayLedgerOracle.Compute([order], T1).ByTerminal["T01"];
        t.TtcCents.Should().Be(2700);
        t.HtCents.Should().Be(2318);
        t.VatByRate[10m].Should().Be(82);
        t.VatByRate[20m].Should().Be(300);
    }

    [Fact]
    public void SplitPaymentWithTip_ExcludesTheTipFromFiscalAmounts_ButNotFromTenders()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "3", 2, "EatIn",
            [new DayLine("A", 1, 1250, 10m), new DayLine("B", 1, 2000, 20m)], null, 350,
            [[new DayTender("CreditCard", 1000)], [new DayTender("CreditCard", 2600)]]); // 3250 + 350 de pourboire
        var t = DayLedgerOracle.Compute([order], T1).ByTerminal["T01"];
        t.TtcCents.Should().Be(3250);
        t.ReceiptCount.Should().Be(2);
        t.TenderByMethod["CreditCard"].Should().Be(3600); // les règlements contiennent le pourboire
        // reçu 1 (1000) : 385 à 10 % (HT 350, TVA 35), 615 à 20 % (HT 513, TVA 102)
        // reçu 2 (2250) : 865 à 10 % (HT 786, TVA 79), 1385 à 20 % (HT 1154, TVA 231)
        t.HtCents.Should().Be(2803);
        t.VatByRate[10m].Should().Be(114);
        t.VatByRate[20m].Should().Be(333);
    }

    [Fact]
    public void PercentageDiscountAndCompLine_AreLedgeredFromTheInputs()
    {
        // 2 × (1000 + 150) = 2300 à 10 %, ligne offerte à 20 % (groupe à 0), remise 10 % : 2070 → HT 1882, TVA 188.
        var order = new DayOperation("table_order", "T1", "waiter1", "4", 2, "EatIn",
            [new DayLine("Plat", 2, 1000, 10m, ModifiersExtraCents: 150), new DayLine("Vin", 1, 900, 20m, Comp: true)],
            new DayOrderDiscount("Percentage", 10m), 0, [[new DayTender("Cash", 2070)]]);
        var t = DayLedgerOracle.Compute([order], T1).ByTerminal["T01"];
        t.TtcCents.Should().Be(2070);
        t.HtCents.Should().Be(1882);
        t.VatByRate.Should().Equal(new Dictionary<decimal, long> { [10m] = 188, [20m] = 0 });
    }

    [Fact]
    public void ReportsAndHeldOrders_DoNotTouchTheLedger()
    {
        var ops = new[]
        {
            new DayOperation("hold_then_void", "T1", "waiter1", null, 0, "Takeaway", [new DayLine("Café", 1, 200, 10m)]),
            new DayOperation("x_report", "T1"),
            new DayOperation("z_closure", "T1"),
        };
        var t = DayLedgerOracle.Compute(ops, T1).ByTerminal["T01"];
        t.TtcCents.Should().Be(0);
        t.ReceiptCount.Should().Be(0);
        t.VatByRate.Should().BeEmpty();
        t.TenderByMethod.Should().BeEmpty();
    }

    [Fact]
    public void ATerminalWithoutSales_HasAnEmptyLedger_NotAMissingKey()
    {
        var ids = new Dictionary<string, string> { ["A"] = "T01", ["B"] = "T02" };
        var order = new DayOperation("counter_order", "A", "waiter1", null, 0, "EatIn",
            [new DayLine("Menu", 1, 1000, 10m)], null, 0, [[new DayTender("Cash", 1000)]]);
        var ledger = DayLedgerOracle.Compute([order], ids);
        ledger.ByTerminal["T02"].ReceiptCount.Should().Be(0);
        ledger.ByTerminal["T02"].TtcCents.Should().Be(0);
        ledger.Total.TtcCents.Should().Be(1000);
    }

    [Fact]
    public void OrderTotalCents_AppliesTheDiscountAndNeverGoesBelowZero()
    {
        var lines = new[] { new DayLine("Café", 2, 250, 20m), new DayLine("Offert", 1, 900, 10m, Comp: true) };
        DayLedgerOracle.OrderTotalCents(new DayOperation("table_order", Lines: lines)).Should().Be(500);
        DayLedgerOracle.OrderTotalCents(new DayOperation("table_order", Lines: lines, Discount: new("Percentage", 10m))).Should().Be(450);
        DayLedgerOracle.OrderTotalCents(new DayOperation("table_order", Lines: lines, Discount: new("FixedAmount", 10m))).Should().Be(0);
    }

    [Fact]
    public void Scenario_RoundTripsAsCamelCaseJson()
    {
        var scenario = new DayScenario(7, [new DayTerminal("A", "Caisse A")],
            [new DayOperation("counter_order", "A", "waiter1", null, 0, "Takeaway", [new DayLine("Sandwich", 1, 1000, 10m, 5.5m)], null, 0,
                [[new DayTender("CreditCard", 1000)]])]);
        string json = JsonSerializer.Serialize(scenario, DayScenarioJsonContext.Default.DayScenario);
        json.Should().Contain("\"operations\"").And.Contain("\"unitPriceCents\": 1000").And.Contain("\"taxRateTakeawayPercent\": \"5.5\"");
        var back = JsonSerializer.Deserialize(json, DayScenarioJsonContext.Default.DayScenario)!;
        back.Operations[0].Lines![0].Should().Be(scenario.Operations[0].Lines![0]);
        back.Operations[0].Payments![0][0].Should().Be(new DayTender("CreditCard", 1000));
    }
}
