using System.Text.Json;

namespace RestaurantPos.Api.Tests.DaySimulation;

public sealed record StockLine(string Ingredient, long QuantityMilli);

/// <summary>Ingrédient brut (prix en milli-centimes par unité de base) ou préparation (rendement + fiche).</summary>
public sealed record StockIngredientSpec(string Name, long PriceMilli, bool IsPreparation = false, long? YieldMilli = null, IReadOnlyList<StockLine>? Lines = null);

public sealed record StockProductSpec(string Name, long PriceCents, IReadOnlyList<StockLine> Recipe);

/// <summary>
/// <c>receipt</c> (Ingredient, QuantityMilli, UnitCostMilliCents), <c>sale</c> (Product, Quantity), <c>void</c> (SaleIndex = rang de la vente, Restock),
/// <c>loss</c> (Ingredient, QuantityMilli), <c>count</c> (Ingredient, QuantityMilli = quantité comptée).
/// </summary>
public sealed record StockOp(string Type, string? Product = null, int Quantity = 0, string? Ingredient = null, long QuantityMilli = 0,
    long UnitCostMilliCents = 0, bool Restock = false, int? SaleIndex = null);

public sealed record StockDayExpected(IReadOnlyDictionary<string, long> StockMilli, IReadOnlyDictionary<string, long> UnitCostMilli, IReadOnlyDictionary<string, long> ProductCostCents);

/// <summary>Contenu de <c>stock_day_reference.json</c> : la journée et son oracle, rejoués par le .NET (API et services réels) et par l'iPad.</summary>
public sealed record StockDayFile(int Seed, IReadOnlyList<StockIngredientSpec> Ingredients, IReadOnlyList<StockProductSpec> Products, IReadOnlyList<StockOp> Operations, StockDayExpected Expected);

public static class StockDay
{
    public const int Seed = 20261107;
    public static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static StockDayFile Build()
    {
        IReadOnlyList<StockIngredientSpec> ingredients =
        [
            new("Boeuf", 1800), new("Tomate", 300), new("Farine", 125), new("Mozzarella", 1900), new("Creme", 450), new("Sel", 20),
            new("Sauce tomate", 0, true, 500_000, [new("Tomate", 1_000_000), new("Sel", 5_000)]),
        ];
        IReadOnlyList<StockProductSpec> products =
        [
            new("Steak", 1900, [new("Boeuf", 200_000), new("Sel", 2_000)]),
            new("Pizza", 1300, [new("Farine", 250_000), new("Sauce tomate", 80_000), new("Mozzarella", 125_000)]),
            new("Dessert", 700, [new("Creme", 90_000)]),
            new("Eau", 300, []),
        ];

        var rng = new Random(Seed);
        var ops = new List<StockOp>();
        foreach (var name in new[] { "Boeuf", "Tomate", "Farine", "Mozzarella", "Creme", "Sel" })
            ops.Add(new("receipt", Ingredient: name, QuantityMilli: rng.Next(5, 30) * 1_000_000L, UnitCostMilliCents: ingredients.First(i => i.Name == name).PriceMilli));
        var sales = new List<int>();
        for (int i = 0; i < 45; i++)
        {
            switch (rng.Next(12))
            {
                case 0:
                    ops.Add(new("receipt", Ingredient: ingredients[rng.Next(6)].Name, QuantityMilli: rng.Next(1, 8) * 1_000_000L, UnitCostMilliCents: rng.Next(20, 2000)));
                    break;
                case 1:
                    ops.Add(new("loss", Ingredient: ingredients[rng.Next(6)].Name, QuantityMilli: rng.Next(1, 500) * 1_000L));
                    break;
                case 2 when i > 20:
                    ops.Add(new("count", Ingredient: ingredients[rng.Next(6)].Name, QuantityMilli: rng.Next(0, 20) * 1_000_000L));
                    break;
                case 3 when sales.Count > 0:
                    ops.Add(new("void", SaleIndex: sales[rng.Next(sales.Count)], Restock: rng.Next(2) == 0));
                    break;
                default:
                    sales.Add(ops.Count(o => o.Type == "sale"));
                    ops.Add(new("sale", Product: products[rng.Next(products.Count)].Name, Quantity: rng.Next(1, 5)));
                    break;
            }
        }
        // Une vente ne s'annule qu'une fois.
        var voided = new HashSet<int>();
        var deduped = ops.Where(o => o.Type != "void" || voided.Add(o.SaleIndex!.Value)).ToList();

        return new StockDayFile(Seed, ingredients, products, deduped, Oracle(ingredients, products, deduped));
    }

    // ---- Oracle : arithmétique indépendante du code de production (dictionnaires et entiers, arrondis au demi supérieur).

    private static long Div(long numerator, long denominator) => (long)(((Int128)numerator + denominator / 2) / denominator);

    /// <summary>Quantités brutes consommées par <paramref name="quantity"/> portions d'une fiche.</summary>
    private static void Flatten(IReadOnlyList<StockLine> lines, long quantity, IReadOnlyList<StockIngredientSpec> specs, Dictionary<string, long> into)
    {
        foreach (var line in lines)
        {
            var spec = specs.First(s => s.Name == line.Ingredient);
            var q = line.QuantityMilli * quantity;
            if (!spec.IsPreparation) { into[spec.Name] = into.GetValueOrDefault(spec.Name) + q; continue; }
            foreach (var inner in spec.Lines!) Flatten([new StockLine(inner.Ingredient, Div(q * inner.QuantityMilli, spec.YieldMilli!.Value))], 1, specs, into);
        }
    }

    private static StockDayExpected Oracle(IReadOnlyList<StockIngredientSpec> specs, IReadOnlyList<StockProductSpec> products, IReadOnlyList<StockOp> ops)
    {
        var stock = specs.Where(s => !s.IsPreparation).ToDictionary(s => s.Name, _ => 0L);
        var cost = specs.Where(s => !s.IsPreparation).ToDictionary(s => s.Name, s => s.PriceMilli);
        var salesConsumption = new List<Dictionary<string, long>>();
        foreach (var op in ops)
        {
            switch (op.Type)
            {
                case "receipt":
                    {
                        var n = op.Ingredient!;
                        cost[n] = stock[n] > 0 ? (long)(((Int128)stock[n] * cost[n] + (Int128)op.QuantityMilli * op.UnitCostMilliCents + (stock[n] + op.QuantityMilli) / 2) / (stock[n] + op.QuantityMilli)) : op.UnitCostMilliCents;
                        stock[n] += op.QuantityMilli;
                        break;
                    }
                case "loss": stock[op.Ingredient!] -= op.QuantityMilli; break;
                case "count": stock[op.Ingredient!] = op.QuantityMilli; break;
                case "sale":
                    {
                        var used = new Dictionary<string, long>();
                        Flatten(products.First(p => p.Name == op.Product).Recipe, op.Quantity, specs, used);
                        foreach (var (n, q) in used) stock[n] -= q;
                        salesConsumption.Add(used);
                        break;
                    }
                case "void" when op.Restock:
                    foreach (var (n, q) in salesConsumption[op.SaleIndex!.Value]) stock[n] += q;
                    break;
            }
        }

        // Coût d'une fiche avec les prix finaux : Σ quantité × prix / 1e6, arrondi une fois ; prix d'une préparation arrondi par unité de base.
        long UnitPrice(StockIngredientSpec s)
        {
            if (!s.IsPreparation) return cost[s.Name];
            decimal total = s.Lines!.Sum(l => (decimal)l.QuantityMilli * cost[l.Ingredient]);
            return (long)Math.Round(total / s.YieldMilli!.Value, MidpointRounding.AwayFromZero);
        }
        var productCosts = products.Where(p => p.Recipe.Count > 0).ToDictionary(p => p.Name, p =>
        {
            decimal total = p.Recipe.Sum(l => (decimal)l.QuantityMilli * UnitPrice(specs.First(s => s.Name == l.Ingredient)));
            return (long)Math.Round(total / 1_000_000m, MidpointRounding.AwayFromZero);
        });
        return new StockDayExpected(stock, cost, productCosts);
    }
}
