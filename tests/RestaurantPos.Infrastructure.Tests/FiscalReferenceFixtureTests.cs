using System.Globalization;
using System.Text.Json;
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

/// <summary>
/// Fixture de référence partagée avec l'iPad (mode autonome) : le Swift rejoue ces vecteurs et doit obtenir les mêmes signatures.
/// Régénérer avec <c>WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests</c>
/// après toute modification fiscale du .NET, puis rejouer les tests PosKit.
/// </summary>
public class FiscalReferenceFixtureTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly string[] JsonStringInputs = new[] { "plain", "Zoë \"Chef\" <A&B>", "Dérogation +10% d'été", "a\\b\tc\nd", "pizza 🍕 `x`" };

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx")))
        {
            dir = dir.Parent;
        }

        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "fiscal_reference.json");
    }

    private static DateTimeOffset At(int hour, int minute, int second, int millisecond) =>
        new(2026, 10, 10, hour, minute, second, millisecond, TimeSpan.Zero);

    private static object HashVectors()
    {
        string genesis = NF525FiscalAuditService.GenesisHash;
        var operatorId = Guid.Parse("0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9");
        var documentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var receiptInputs = new (long Sequence, long Amount, DateTimeOffset At, string Tax)[]
        {
            (1, 1250, At(9, 0, 0, 123), "{\"10\":114}"),
            (2, 4000, At(9, 5, 30, 7), "{\"10\":364,\"5.5\":88}"),
            (3, -1250, At(9, 6, 0, 999), "{\"10\":-114}"),
        };
        var receipts = new List<object>();
        string previous = genesis;
        foreach (var r in receiptInputs)
        {
            string hash = FiscalHashing.ComputeReceiptHash(previous, "T01", r.Sequence, r.Amount, r.At, r.Tax);
            receipts.Add(new
            {
                previousHash = previous,
                terminalId = "T01",
                sequence = r.Sequence,
                amountCents = r.Amount,
                timestamp = r.At.ToString("O"),
                taxBreakdownJson = r.Tax,
                hash,
            });
            previous = hash;
        }

        var jetInputs = new (string EventType, DateTimeOffset At, string? Terminal, Guid? Operator, string Payload)[]
        {
            ("RECEIPT_VOIDED", At(9, 6, 0, 999), "T01", operatorId,
                JsonSerializer.Serialize(new { OriginalReceiptId = documentId, TotalTtcCents = 1250L })),
            ("EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED", At(9, 7, 0, 0), "T01", operatorId,
                JsonSerializer.Serialize(new { Operator = "Zoë \"Chef\" <A&B>", Reason = "Dérogation +10% d'été", DurationMinutes = 60 })),
            ("DUPLICATE_PRINTED", At(9, 8, 0, 500), null, null,
                JsonSerializer.Serialize(new { documentId, documentType = "receipt", duplicateNumber = 1, printQueued = false })),
        };
        var jet = new List<object>();
        previous = genesis;
        long chainSequence = 0;
        foreach (var e in jetInputs)
        {
            chainSequence++;
            string hash = FiscalHashing.ComputeJetHash(previous, chainSequence, e.EventType, e.At, e.Terminal, e.Operator, e.Payload);
            jet.Add(new
            {
                previousHash = previous,
                chainSequence,
                eventType = e.EventType,
                timestamp = e.At.ToString("O"),
                terminalId = e.Terminal,
                operatorId = e.Operator?.ToString(),
                payloadJson = e.Payload,
                hash,
            });
            previous = hash;
        }

        var jsonStrings = JsonStringInputs
            .Select(s => new { input = s, expected = JsonSerializer.Serialize(s) })
            .ToList();

        var dateTimes = new[] { At(9, 0, 0, 0), At(9, 0, 0, 120), At(9, 0, 0, 123) }
            .Select(d => new { timestamp = d.ToString("O"), expected = JsonSerializer.Serialize(d) })
            .ToList();

        return new { genesisHash = genesis, receipts, jet, jsonStrings, dateTimes };
    }

    private sealed record LineSpec(long UnitPriceCents, int Quantity, decimal Rate, decimal? TakeawayRate = null, decimal DiscountPercent = 0, bool IsComp = false, long ModifiersExtraCents = 0);

    private sealed record Pay(PaymentMethod Method, long Amount, long Tendered);

    private sealed record ScenarioSpec(
        string Name,
        OrderDestination Destination,
        LineSpec[] Lines,
        Pay[][] Payments,
        DiscountType? GlobalType = null,
        decimal GlobalValue = 0,
        long TipCents = 0,
        int? VoidPayment = null);

    private static readonly ScenarioSpec[] Scenarios =
    [
        new("simple-eat-in", OrderDestination.EatIn, [new(2000, 2, 10m)], [[new(PaymentMethod.Cash, 4000, 5000)]], VoidPayment: 0),
        new("multi-rate", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m), new(450, 3, 5.5m)], [[new(PaymentMethod.CreditCard, 4380, 4380)]]),
        new("takeaway-reduced-rate", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m)], [[new(PaymentMethod.Cash, 1000, 1000)]]),
        new("global-percent-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [[new(PaymentMethod.Cash, 2700, 2700)]], DiscountType.Percentage, 10m),
        new("global-fixed-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [[new(PaymentMethod.Cash, 2700, 3000)]], DiscountType.FixedAmount, 3m),
        new("split-two-payments", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m)], [[new(PaymentMethod.CreditCard, 1000, 1000)], [new(PaymentMethod.Cash, 2030, 2030)]], VoidPayment: 1),
        new("tip", OrderDestination.EatIn, [new(1250, 1, 10m)], [[new(PaymentMethod.Cash, 1450, 2000)]], TipCents: 200),
        new("line-discount-comp-and-modifiers", OrderDestination.EatIn, [new(1000, 2, 10m, DiscountPercent: 10m), new(500, 1, 10m, IsComp: true), new(300, 1, 20m, ModifiersExtraCents: 50)], [[new(PaymentMethod.Cash, 2150, 2150)]]),
        new("zero-total", OrderDestination.EatIn, [new(500, 1, 10m, IsComp: true)], [[]]),
    ];

    private static string Rate(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static async Task<object> RunScenarioAsync(ScenarioSpec spec)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("FiscalReference_" + Guid.NewGuid().ToString("N"))
            .Options;
        using var db = new AppDbContext(options);
        var service = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));

        var order = new Order { TableNumber = "T01", Destination = spec.Destination, TipAmount = Money.FromCents(spec.TipCents) };
        foreach (var l in spec.Lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductName = "Produit",
                Quantity = l.Quantity,
                UnitPrice = Money.FromCents(l.UnitPriceCents),
                TaxRatePercent = l.Rate,
                TaxRateTakeawayPercent = l.TakeawayRate,
                DiscountPercent = l.DiscountPercent,
                IsComp = l.IsComp,
                ModifiersPriceExtra = Money.FromCents(l.ModifiersExtraCents),
            });
        }

        if (spec.GlobalType is { } type)
        {
            order.GlobalDiscountType = type;
            order.GlobalDiscountValue = spec.GlobalValue;
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var payments = new List<object>();
        var receiptIds = new List<Guid>();
        foreach (var tenders in spec.Payments)
        {
            var result = await service.ProcessPaymentTendersAsync(
                order.Id, "T01", tenders.Select(t => new PaymentTenderRequest(t.Method, t.Amount, t.Tendered)).ToList());
            result.IsSuccess.Should().BeTrue();
            var receipt = await db.FiscalReceipts.SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber);
            receiptIds.Add(receipt.Id);
            payments.Add(new
            {
                tenders = tenders.Select(t => new { method = (int)t.Method, amountCents = t.Amount, tenderedCents = t.Tendered }).ToList(),
                expected = new
                {
                    totalTtcCents = receipt.TotalTtcAmount.AmountInCents,
                    totalHtCents = receipt.TotalHtAmount.AmountInCents,
                    taxBreakdownJson = receipt.TaxBreakdownJson,
                    changeGivenCents = result.ChangeGivenCents,
                    remainingBalanceCents = result.RemainingBalanceCents,
                },
            });
        }

        object? voided = null;
        if (spec.VoidPayment is { } index)
        {
            (await service.VoidReceiptAsync(receiptIds[index], "T01", Guid.NewGuid())).IsSuccess.Should().BeTrue();
            var voidReceipt = await db.FiscalReceipts.SingleAsync(r => r.VoidedReceiptId == receiptIds[index]);
            voided = new
            {
                paymentIndex = index,
                totalTtcCents = voidReceipt.TotalTtcAmount.AmountInCents,
                totalHtCents = voidReceipt.TotalHtAmount.AmountInCents,
                taxBreakdownJson = voidReceipt.TaxBreakdownJson,
            };
        }

        return new
        {
            name = spec.Name,
            destination = (int)spec.Destination,
            globalDiscountType = spec.GlobalType.HasValue ? (int?)spec.GlobalType.Value : null,
            globalDiscountValue = Rate(spec.GlobalValue),
            tipCents = spec.TipCents,
            lines = spec.Lines.Select(l => new
            {
                unitPriceCents = l.UnitPriceCents,
                quantity = l.Quantity,
                taxRatePercent = Rate(l.Rate),
                takeawayRatePercent = l.TakeawayRate is { } t ? Rate(t) : null,
                discountPercent = Rate(l.DiscountPercent),
                isComp = l.IsComp,
                modifiersExtraCents = l.ModifiersExtraCents,
            }).ToList(),
            payments,
            voided,
        };
    }

    public static async Task<string> BuildAsync()
    {
        var taxScenarios = new List<object>();
        foreach (var spec in Scenarios)
        {
            taxScenarios.Add(await RunScenarioAsync(spec));
        }

        return JsonSerializer.Serialize(new { hashVectors = HashVectors(), taxScenarios }, Options);
    }

    [Fact]
    public async Task FixtureMatchesTheCurrentBackend()
    {
        string actual = (await BuildAsync()).ReplaceLineEndings("\n") + "\n";
        string path = FixturePath();
        if (Environment.GetEnvironmentVariable("WRITE_FISCAL_FIXTURE") == "1")
        {
            await File.WriteAllTextAsync(path, actual);
        }

        (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n").Should().Be(
            actual,
            "la fixture fiscale doit refléter le .NET : WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests");
    }
}
