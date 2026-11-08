using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad : les deltas en pourcentage des cartes de tendance (arrondi à 0,1, demi éloigné de zéro) doivent être identiques en Swift.
/// Régénérer avec <c>WRITE_INSIGHTS_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter InsightsReferenceFixtureTests</c>.
/// </summary>
public class InsightsReferenceFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Case(long Current, long Reference, string Delta);

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "insights_reference.json");
    }

    [Fact]
    public void Fixture_MatchesTheDeltaFormula()
    {
        (long, long)[] inputs = [(3_120, 3_900), (3_900, 3_120), (1_005, 1_000), (1_000, 3_000), (2_000, 1_000), (999, 1_000), (0, 1_000), (5, 0), (5_000, 2_000), (1, 3), (2, 3), (115, 100), (85, 100), (3_333, 10_000), (10_001, 10_000)];
        var cases = inputs.Select(i => new Case(i.Item1, i.Item2, IInsightsService.DeltaPercent(i.Item1, i.Item2).ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
        var json = JsonSerializer.Serialize(cases, JsonOptions) + "\n";
        if (Environment.GetEnvironmentVariable("WRITE_INSIGHTS_FIXTURE") == "1") File.WriteAllText(FixturePath(), json);
        File.ReadAllText(FixturePath()).Should().Be(json, "générer la fixture avec WRITE_INSIGHTS_FIXTURE=1");
    }
}
