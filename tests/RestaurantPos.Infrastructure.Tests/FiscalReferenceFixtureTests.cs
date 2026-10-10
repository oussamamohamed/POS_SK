using System.Text.Json;
using FluentAssertions;
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

    public static Task<string> BuildAsync() =>
        Task.FromResult(JsonSerializer.Serialize(new { hashVectors = HashVectors() }, Options));

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
