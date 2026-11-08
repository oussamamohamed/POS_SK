using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class RecipeCostCalculatorTests
{
    private static Ingredient Raw(string name, long milliCentsPerUnit, int mask = 0, bool active = true) =>
        new() { Name = name, Unit = "g", UnitCostMilliCents = milliCentsPerUnit, AllergenMask = mask, IsActive = active };

    private static Ingredient Prep(string name, long yieldMilli) =>
        new() { Name = name, Unit = "g", IsPreparation = true, YieldMilli = yieldMilli };

    private static RecipeLine Line(Ingredient ingredient, long quantityMilli, Guid? owner = null) =>
        new() { OwnerKind = RecipeLine.OwnerProduct, OwnerId = owner ?? Guid.Empty, IngredientId = ingredient.Id, QuantityMilli = quantityMilli };

    private static RecipeCost Run(IReadOnlyCollection<RecipeLine> lines, IEnumerable<Ingredient> all, Dictionary<Guid, IReadOnlyCollection<RecipeLine>>? preps = null) =>
        RecipeCostCalculator.Compute(lines, all.ToDictionary(i => i.Id), preps ?? []);

    [Fact]
    public void Sum_IsRoundedOnceAtTheEnd()
    {
        // 1 g à 0,4 centime deux fois : 0,8 centime au total = 1 centime (arrondir ligne à ligne donnerait 0).
        var a = Raw("a", 400);
        var b = Raw("b", 400);
        Run([Line(a, 1000), Line(b, 1000)], [a, b]).Cents.Should().Be(1);
    }

    [Fact]
    public void ThreeIngredients_CostIsQuantityTimesUnitPrice()
    {
        var flour = Raw("farine", 1250, mask: 1);   // 12,50 €/kg = 1,25 c/g
        var tomato = Raw("tomate", 300);            // 0,30 c/g
        var cheese = Raw("mozzarella", 1800, mask: 1 << 6);
        var result = Run([Line(flour, 150_000), Line(tomato, 100_000), Line(cheese, 125_000)], [flour, tomato, cheese]);
        // 187,5 + 30 + 225 = 442,5 centimes, demi supérieur.
        result.Cents.Should().Be(443);
        result.AllergenMask.Should().Be(1 | (1 << 6));
    }

    [Fact]
    public void Preparation_UsesYieldAndIsIncluded()
    {
        var tomato = Raw("tomate", 300, mask: 1 << 8);
        var sauce = Prep("sauce", 500_000);                    // 1 kg de tomate pour 500 g de sauce
        var preps = new Dictionary<Guid, IReadOnlyCollection<RecipeLine>> { [sauce.Id] = [Line(tomato, 1_000_000, sauce.Id)] };
        var all = new[] { tomato, sauce };

        RecipeCostCalculator.PreparationUnitCost(sauce.Id, all.ToDictionary(i => i.Id), preps).Should().Be(600);
        var result = Run([Line(sauce, 100_000)], all, preps);   // 100 g de sauce à 0,6 c/g
        result.Cents.Should().Be(60);
        result.AllergenMask.Should().Be(1 << 8);
    }

    [Fact]
    public void EmptyRecipe_CostsNothing()
    {
        Run([], []).Should().Be(new RecipeCost(0, 0));
    }

    [Fact]
    public void InactiveIngredient_IsRefused()
    {
        var old = Raw("vieux", 100, active: false);
        var act = () => Run([Line(old, 1000)], [old]);
        act.Should().Throw<RecipeException>().Which.Code.Should().Be("ingredient_inactive");
    }

    [Fact]
    public void Cycle_IsRefused()
    {
        var a = Prep("a", 1000);
        var b = Prep("b", 1000);
        var preps = new Dictionary<Guid, IReadOnlyCollection<RecipeLine>>
        {
            [a.Id] = [Line(b, 1000, a.Id)],
            [b.Id] = [Line(a, 1000, b.Id)],
        };
        var act = () => Run([Line(a, 1000)], [a, b], preps);
        act.Should().Throw<RecipeException>().Which.Code.Should().Be("recipe_cycle");
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void Depth_IsLimitedToFive(int chainLength, bool allowed)
    {
        var raw = Raw("base", 1000);
        var chain = Enumerable.Range(0, chainLength).Select(i => Prep($"p{i}", 1000)).ToList();
        var preps = new Dictionary<Guid, IReadOnlyCollection<RecipeLine>>();
        for (var i = 0; i < chainLength; i++)
            preps[chain[i].Id] = [Line(i == chainLength - 1 ? raw : chain[i + 1], 1000, chain[i].Id)];

        var act = () => Run([Line(chain[0], 1000)], chain.Append(raw), preps);
        if (allowed) act.Should().NotThrow();
        else act.Should().Throw<RecipeException>().Which.Code.Should().Be("recipe_depth");
    }

    [Fact]
    public void LargeNumbers_DoNotOverflow()
    {
        // 4 000 000 g × 9 000 000 mc/g = 3,6e19 (au-delà d'un long) / 1e6 = 36 000 000 000 000 centimes.
        var gold = Raw("or", 9_000_000_000);
        Run([Line(gold, 4_000_000_000)], [gold]).Cents.Should().Be(36_000_000_000_000L);
    }
}
