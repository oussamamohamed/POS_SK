using System.Text.Json;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Fixture partagée avec l'iPad (mode autonome) : le Swift rejoue ces fiches et doit obtenir les mêmes centimes, allergènes et prix de préparation.
/// Régénérer avec <c>WRITE_RECIPE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter RecipeCostReferenceFixtureTests</c>
/// après toute modification de <c>RecipeCostCalculator</c>.
/// </summary>
public class RecipeCostReferenceFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record Ing(Guid Id, string Name, bool IsPreparation, long? YieldMilli, long UnitCostMilliCents, int AllergenMask, bool IsActive);

    private sealed record Line(Guid IngredientId, long QuantityMilli);

    private sealed record Expected(long? Cents, int? AllergenMask, string? Error, Dictionary<string, long> PreparationUnitCosts);

    private sealed record Case(string Name, List<Ing> Ingredients, Dictionary<string, List<Line>> PreparationLines, List<Line> Lines, Expected Expected);

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
        dir.Should().NotBeNull("la racine du dépôt (RestaurantPos.slnx) doit être trouvable");
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "recipe_cost_reference.json");
    }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static Case Build(string name, List<Ing> ingredients, Dictionary<int, List<(int Ing, long Qty)>> preps, List<(int Ing, long Qty)> lines)
    {
        var byId = ingredients.ToDictionary(i => i.Id, i => new Ingredient { Id = i.Id, Name = i.Name, Unit = "g", UnitCostMilliCents = i.UnitCostMilliCents, IsPreparation = i.IsPreparation, YieldMilli = i.YieldMilli, AllergenMask = i.AllergenMask, IsActive = i.IsActive });
        IReadOnlyCollection<RecipeLine> ToLines(Guid owner, IEnumerable<(int Ing, long Qty)> src) =>
            src.Select(l => new RecipeLine { OwnerKind = "x", OwnerId = owner, IngredientId = Id(l.Ing), QuantityMilli = l.Qty }).ToList();
        var prepLines = preps.ToDictionary(p => Id(p.Key), p => ToLines(Id(p.Key), p.Value));

        Expected expected;
        try
        {
            var cost = RecipeCostCalculator.Compute(ToLines(Guid.Empty, lines), byId, prepLines);
            var units = ingredients.Where(i => i.IsPreparation && prepLines.ContainsKey(i.Id) && i.IsActive).ToDictionary(i => i.Id.ToString(), i => RecipeCostCalculator.PreparationUnitCost(i.Id, byId, prepLines));
            expected = new Expected(cost.Cents, cost.AllergenMask, null, units);
        }
        catch (RecipeException ex)
        {
            expected = new Expected(null, null, ex.Code, []);
        }

        return new Case(name, ingredients, preps.ToDictionary(p => Id(p.Key).ToString(), p => p.Value.Select(l => new Line(Id(l.Ing), l.Qty)).ToList()), lines.Select(l => new Line(Id(l.Ing), l.Qty)).ToList(), expected);
    }

    private static Ing Raw(int n, long mc, int mask = 0, bool active = true) => new(Id(n), "i" + n, false, null, mc, mask, active);

    private static Ing Prep(int n, long yield) => new(Id(n), "p" + n, true, yield, 0, 0, true);

    private static List<Case> Cases()
    {
        var cases = new List<Case>
        {
            Build("vide", [], [], []),
            Build("arrondi_unique", [Raw(1, 400), Raw(2, 400)], [], [(1, 1000), (2, 1000)]),
            Build("demi_superieur", [Raw(1, 1250, 1), Raw(2, 300), Raw(3, 1800, 64)], [], [(1, 150_000), (2, 100_000), (3, 125_000)]),
            Build("preparation_rendement", [Raw(1, 300, 256), Prep(2, 500_000)], new() { [2] = [(1, 1_000_000)] }, [(2, 100_000)]),
            Build("preparation_arrondi_milli", [Raw(1, 1000), Prep(2, 3000)], new() { [2] = [(1, 1000)] }, [(2, 7000)]),
            Build("grands_nombres", [Raw(1, 9_000_000_000)], [], [(1, 4_000_000_000)]),
            Build("ingredient_inactif", [Raw(1, 100, 0, false)], [], [(1, 1000)]),
            Build("ingredient_inconnu", [], [], [(9, 1000)]),
            Build("cycle", [Prep(1, 1000), Prep(2, 1000)], new() { [1] = [(2, 1000)], [2] = [(1, 1000)] }, [(1, 1000)]),
            Build("profondeur_4", Chain(4), ChainPreps(4), [(1, 1000)]),
            Build("profondeur_5", Chain(5), ChainPreps(5), [(1, 1000)]),
        };

        // Fiches pseudo-aléatoires (graine fixe) : prix et quantités variés pour exercer l'arrondi.
        var rng = new Random(2026);
        for (var c = 0; c < 30; c++)
        {
            var raws = Enumerable.Range(1, 4).Select(n => Raw(n, rng.Next(1, 5000), 1 << rng.Next(0, 14))).ToList();
            var prep = Prep(5, rng.Next(100, 900_000));
            var lines = Enumerable.Range(1, rng.Next(1, 5)).Select(n => (Ing: n, Qty: (long)rng.Next(1, 800_000))).ToList();
            lines.Add((5, rng.Next(1, 300_000)));
            var prepLines = new Dictionary<int, List<(int, long)>> { [5] = [(1, rng.Next(1, 2_000_000)), (3, rng.Next(1, 500_000))] };
            cases.Add(Build($"aleatoire_{c}", [.. raws, prep], prepLines, lines));
        }

        return cases;
    }

    private static List<Ing> Chain(int length) =>
        [.. Enumerable.Range(1, length).Select(n => Prep(n, 1000)), Raw(length + 1, 1000)];

    private static Dictionary<int, List<(int, long)>> ChainPreps(int length) =>
        Enumerable.Range(1, length).ToDictionary(n => n, n => new List<(int, long)> { (n + 1, 1000) });

    [Fact]
    public void Fixture_MatchesTheCalculator()
    {
        var json = JsonSerializer.Serialize(Cases(), JsonOptions) + "\n";
        var path = FixturePath();
        if (Environment.GetEnvironmentVariable("WRITE_RECIPE_FIXTURE") == "1")
        {
            File.WriteAllText(path, json);
        }

        File.Exists(path).Should().BeTrue("générer la fixture avec WRITE_RECIPE_FIXTURE=1");
        File.ReadAllText(path).Should().Be(json, "le calculateur .NET a changé : régénérer la fixture et reporter le changement dans Swift");
        Cases().Count(c => c.Expected.Error is not null).Should().Be(4);
    }
}
