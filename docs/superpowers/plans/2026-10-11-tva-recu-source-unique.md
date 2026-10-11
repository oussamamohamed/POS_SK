# TVA du reçu à source unique : plan d'implémentation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le reçu fiscal .NET tire son HT, sa TVA par taux et ses règlements partiels d'une seule ventilation (`Order.CalculateTaxBreakdown`, taux effectif, remise de commande de tout type, répartition au plus fort reste), portée à l'identique en Swift ; le tout prouvé par des tests exhaustifs et par une journée simulée qui contrôle X et Z.

**Architecture:** Un répartiteur pur `TaxAllocation.Allocate` (plus fort reste, déterministe) alimente `Order.CalculateTaxBreakdown` (groupes par taux effectif, ordre croissant des taux) puis `ReceiptTaxCalculator` (reçu complet ou partiel). `CheckoutPaymentService` n'a plus de calcul de TVA propre. Un générateur .NET écrit la fixture `fiscal_reference.json` (commande + reçus), rejouée par `OrderMath` / `FiscalReceiptMath` en Swift. Un test d'API rejoue une journée sur l'API réelle, décrite en données JSON, et compare X et Z à un grand livre indépendant.

**Tech Stack:** .NET 9 (xUnit, FluentAssertions, EF InMemory, `WebApplicationFactory`), Swift 6 (PosKit, Swift Testing). Aucune dépendance ajoutée.

**Spec:** `docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md`. La tâche 1 amende la spec sur trois points décidés pendant la rédaction du plan (voir « Rulings du plan »).

## Rulings du plan (amendements de la spec, appliqués par la tâche 1)

1. **Répartition au plus fort reste** (méthode de Hamilton, ex æquo départagés par l'ordre des groupes) à la place de « arrondi puis dernier groupe prend le reste » : cette dernière règle peut produire une part négative (ex. total 3 réparti sur cinq poids égaux à 1). Elle est donc aussi adoptée par `OrderMath.totals` (Swift) ; un écart d'au plus 1 centime avec l'ancien affichage iPad est possible dans de rares cas, et c'est voulu.
2. **Ordre des groupes = taux effectif croissant**, pour la répartition (départage des restes), pour le JSON de TVA (clés triées) et pour l'iPad. Avant, l'ordre dépendait de la première ligne de la commande.
3. **Reçus partiels : répartition par reçu, sans rejouer l'historique.** La part d'un reçu de montant `X` est `Allocate(X, parts_de_la_commande_par_taux)`. Pour chaque reçu `TTC − HT = Σ TVA` exactement et `Σ TTC des reçus = TTC de la commande` exactement ; par taux, la somme des reçus s'écarte du total de la commande d'au plus **1 centime par reçu** (la TVA est arrondie ticket par ticket, usage courant). La « répartition cumulée exacte » de la spec est abandonnée : elle exigeait de rejouer les reçus et d'ajouter des données au reçu. L'invariant 3 de la spec devient : `Σ TTC = TTC commande` exact, écart par taux ≤ nombre de reçus.

## Global Constraints

- .NET : `TreatWarningsAsErrors` + `EnforceCodeStyleInBuild` ; lancer `dotnet format RestaurantPos.slnx --include <fichiers touchés>` avant de compiler et ne pas reformater d'autres fichiers (annuler les reformatages hors périmètre).
- Montants en centimes entiers (`long`). Arrondi des découpes HT/TVA : `TaxBreakdownItem.Calculate` (`MidpointRounding.AwayFromZero`), inchangé.
- Aucun changement de schéma, de format de signature, ni de format du JSON de TVA : `{"<taux>":<centimes TVA>,...}` avec clés `decimal` en notation invariante (`10`, `5.5`), mais **triées par taux croissant**.
- Pas de mécanisme de transition (aucun reçu réel émis, décision de l'utilisateur) ; les reçus de test existants restent immuables.
- Swift 6, Swift Testing seulement dans PosKit ; tests UI inchangés (la suite UI n'est lancée qu'à la tâche 7).
- Les tests existants qui figeaient les anciens montants sont révisés un par un : chaque valeur changée est justifiée par un invariant dans le rapport de la tâche ; il est interdit d'affaiblir ou de supprimer une assertion pour la faire passer.
- La fixture `fiscal_reference.json` est générée par le .NET (`WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FiscalReferenceFixtureTests`) ; en cas d'écart Swift/.NET, le .NET fait foi.
- Suivi : mettre à jour `docs/plans/tva-recu-source-unique-progress.md` après chaque tâche (statut, totaux .NET / PosKit, findings ouverts) et lancer toute la suite concernée avant de marquer une tâche faite.
- Commits en français, sur la branche `fix/tva-recu-source-unique` (ne pas pousser).
- Base de référence : .NET 418, PosKit 394, UI 52 (tous verts à la fin du sous-projet 2).

## Review Focus

1. Un seul centime d'écart entre `TTC − HT` et `Σ TVA` d'un reçu, ou une part négative sur un reçu de vente : défaut critique (tâches 1, 2, 3, 4, 6).
2. Commande à total nul (gratuité, remise 100 %, remise fixe supérieure au total) : aucun montant négatif, JSON `{"<taux>":0}` et HT 0 (tâches 1, 2, 4).
3. Plusieurs lignes à des taux différents dans une commande à emporter dont une seule a un taux réduit : le reçu doit déclarer chaque ligne à son taux effectif (tâches 2, 4, 6).
4. Règlement partiel avec pourboire, puis solde, puis annulation d'un des reçus : sommes exactes et annulation = négation exacte (tâches 2, 3, 6).
5. Z refusé tant qu'une commande est ouverte sur n'importe quel terminal ; annulation refusée après la clôture du terminal (`void_after_closure`) : la journée simulée doit respecter ces règles et vérifier le refus (tâches 5, 6).

---

### Task 1: Répartiteur `TaxAllocation`, ventilation `Order.CalculateTaxBreakdown` et amendement de la spec

**Files:**
- Create: `src/RestaurantPos.Domain/ValueObjects/TaxAllocation.cs`
- Modify: `src/RestaurantPos.Domain/Entities/Order.cs` (`CalculateTaxBreakdown`, ajout `Subtotal`, `DiscountAmount`)
- Create: `tests/RestaurantPos.Domain.Tests/TaxAllocationTests.cs`
- Create: `tests/RestaurantPos.Domain.Tests/OrderTaxBreakdownTests.cs`
- Modify: `docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md` (amendements)
- Create: `docs/plans/tva-recu-source-unique-progress.md`

**Interfaces:**
- Consumes: `Order`, `OrderItem`, `TaxBreakdownItem.Calculate`, `Money`.
- Produces :
  - `public static class TaxAllocation { public static long[] Allocate(long total, IReadOnlyList<long> weights); }` (somme des parts = `min(total, Σ weights)`, parts ≥ 0, parts ≤ poids).
  - `Order.Subtotal : Money` (somme des TTC de lignes, avant remise de commande), `Order.DiscountAmount : Money` (`Subtotal − TotalTtc`).
  - `Order.CalculateTaxBreakdown() : List<TaxBreakdownItem>` : un élément par taux effectif, **ordre croissant**, `TaxableBaseCents + TaxAmountCents` = part du groupe, Σ parts = `TotalTtc`.

- [ ] **Step 1: Vérifier le projet de tests du domaine**

Run : `cd /Users/oussama/RiderProjects/POS_SK && ls tests/RestaurantPos.Domain.Tests | head -20 && sed -n 1,40p tests/RestaurantPos.Domain.Tests/RestaurantPos.Domain.Tests.csproj`
Expected : un projet xUnit référençant `RestaurantPos.Domain`. Si `FluentAssertions` n'est pas référencé, utiliser `Assert` d'xUnit dans les tests ci-dessous (même assertions, autre syntaxe) et le noter dans le rapport.

- [ ] **Step 2: Écrire les tests du répartiteur (ils échouent : `TaxAllocation` n'existe pas)**

Créer `tests/RestaurantPos.Domain.Tests/TaxAllocationTests.cs` :
```csharp
using FluentAssertions;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class TaxAllocationTests
{
    [Fact]
    public void Allocate_WhenTotalEqualsTheSum_ReturnsTheWeightsUnchanged()
    {
        long[] weights = [1000, 2000, 5];
        TaxAllocation.Allocate(3005, weights).Should().Equal(weights);
    }

    [Fact]
    public void Allocate_WhenTotalExceedsTheSum_IsCappedAtTheSum()
    {
        TaxAllocation.Allocate(9999, [100, 200]).Should().Equal(100, 200);
    }

    [Theory]
    [InlineData(3, new long[] { 1, 1, 1, 1, 1 }, new long[] { 1, 1, 1, 0, 0 })] // restes égaux : l'ordre des groupes départage
    [InlineData(2, new long[] { 1, 1, 1 }, new long[] { 1, 1, 0 })]
    [InlineData(100, new long[] { 1, 1, 1 }, new long[] { 34, 33, 33 })]
    [InlineData(2700, new long[] { 1000, 2000 }, new long[] { 900, 1800 })]
    [InlineData(1, new long[] { 3, 3 }, new long[] { 1, 0 })]
    [InlineData(5, new long[] { 0, 10, 0 }, new long[] { 0, 5, 0 })]
    public void Allocate_UsesTheLargestRemainderMethod(long total, long[] weights, long[] expected)
    {
        TaxAllocation.Allocate(total, weights).Should().Equal(expected);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-5L)]
    public void Allocate_WithNothingToSpread_ReturnsZeros(long total)
    {
        TaxAllocation.Allocate(total, [10, 20]).Should().Equal(0, 0);
    }

    [Fact]
    public void Allocate_WithZeroWeightsOrNoGroups_ReturnsZerosOrEmpty()
    {
        TaxAllocation.Allocate(50, [0, 0]).Should().Equal(0, 0);
        TaxAllocation.Allocate(50, []).Should().BeEmpty();
    }

    [Fact]
    public void Allocate_IsDeterministicAndKeepsEveryInvariant_OnManyRandomCases()
    {
        var random = new Random(20261011);
        for (int i = 0; i < 5000; i++)
        {
            int count = random.Next(1, 7);
            var weights = Enumerable.Range(0, count).Select(_ => (long)random.Next(0, 100_000)).ToArray();
            long sum = weights.Sum();
            long total = sum == 0 ? 0 : random.NextInt64(0, sum + 1);

            var first = TaxAllocation.Allocate(total, weights);
            var second = TaxAllocation.Allocate(total, weights);

            string context = $"case {i}: total={total} weights=[{string.Join(",", weights)}]";
            second.Should().Equal(first, context);
            first.Sum().Should().Be(Math.Min(total, sum), context);
            for (int g = 0; g < count; g++)
            {
                first[g].Should().BeInRange(0, weights[g], context);
                // plus fort reste : jamais plus d'un centime d'écart avec la part exacte
                double exact = sum == 0 ? 0 : (double)total * weights[g] / sum;
                Math.Abs(first[g] - exact).Should().BeLessThan(1.0 + 1e-9, context);
            }
        }
    }
}
```

- [ ] **Step 3: Écrire les tests de la ventilation (ils échouent : `Subtotal`, `DiscountAmount` et le nouvel ordre n'existent pas)**

Créer `tests/RestaurantPos.Domain.Tests/OrderTaxBreakdownTests.cs` :
```csharp
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class OrderTaxBreakdownTests
{
    private static OrderItem Line(long unitCents, int qty, decimal rate, decimal? takeaway = null, decimal discountPercent = 0, bool comp = false, long extraCents = 0) => new()
    {
        ProductName = "Produit",
        Quantity = qty,
        UnitPrice = Money.FromCents(unitCents),
        TaxRatePercent = rate,
        TaxRateTakeawayPercent = takeaway,
        DiscountPercent = discountPercent,
        IsComp = comp,
        ModifiersPriceExtra = Money.FromCents(extraCents),
    };

    private static Order Build(OrderDestination destination, DiscountType? discount, decimal discountValue, params OrderItem[] items)
    {
        var order = new Order { TableNumber = "T1", Destination = destination, GlobalDiscountType = discount, GlobalDiscountValue = discountValue };
        order.Items.AddRange(items);
        return order;
    }

    private static void AssertInvariants(Order order, string context)
    {
        var breakdown = order.CalculateTaxBreakdown();
        long total = order.TotalTtc.AmountInCents;
        breakdown.Sum(b => b.TaxableBaseCents + b.TaxAmountCents).Should().Be(total, context);
        order.TotalHt.AmountInCents.Should().Be(breakdown.Sum(b => b.TaxableBaseCents), context);
        (order.TotalTtc.AmountInCents - order.TotalHt.AmountInCents).Should().Be(breakdown.Sum(b => b.TaxAmountCents), context);
        breakdown.Select(b => b.TaxRatePercent).Should().BeInAscendingOrder(context);
        breakdown.Select(b => b.TaxRatePercent).Should().OnlyHaveUniqueItems(context);
        breakdown.Should().OnlyContain(b => b.TaxableBaseCents >= 0 && b.TaxAmountCents >= 0, context);
        order.DiscountAmount.AmountInCents.Should().Be(order.Subtotal.AmountInCents - total, context);
        order.DiscountAmount.AmountInCents.Should().BeGreaterThanOrEqualTo(0, context);
    }

    [Fact]
    public void EatIn_TwoRates_NoDiscount()
    {
        var order = Build(OrderDestination.EatIn, null, 0, Line(1000, 1, 10m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((10m, 909L, 91L), (20m, 1667L, 333L));
        order.Subtotal.AmountInCents.Should().Be(3000);
        order.DiscountAmount.AmountInCents.Should().Be(0);
        order.TotalHt.AmountInCents.Should().Be(2576);
        AssertInvariants(order, nameof(EatIn_TwoRates_NoDiscount));
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 10)]
    [InlineData(DiscountType.FixedAmount, 3)]
    public void GlobalDiscountOf300Cents_IsSpreadOnEveryRate(DiscountType type, int value)
    {
        var order = Build(OrderDestination.EatIn, type, value, Line(1000, 1, 10m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((10m, 818L, 82L), (20m, 1500L, 300L));
        order.TotalTtc.AmountInCents.Should().Be(2700);
        order.DiscountAmount.AmountInCents.Should().Be(300);
        order.TotalHt.AmountInCents.Should().Be(2318);
        AssertInvariants(order, $"{type} {value}");
    }

    [Fact]
    public void Takeaway_UsesTheEffectiveRate_AndOnlyOnTheLinesThatHaveOne()
    {
        var order = Build(OrderDestination.Takeaway, null, 0, Line(1000, 1, 10m, takeaway: 5.5m), Line(2000, 1, 20m));
        var b = order.CalculateTaxBreakdown();
        b.Select(x => (x.TaxRatePercent, x.TaxableBaseCents, x.TaxAmountCents)).Should().Equal((5.5m, 948L, 52L), (20m, 1667L, 333L));
        order.TotalHt.AmountInCents.Should().Be(2615);
        AssertInvariants(order, nameof(Takeaway_UsesTheEffectiveRate_AndOnlyOnTheLinesThatHaveOne));
    }

    [Fact]
    public void EatIn_IgnoresTheTakeawayRate()
    {
        var order = Build(OrderDestination.EatIn, null, 0, Line(1000, 1, 10m, takeaway: 5.5m));
        order.CalculateTaxBreakdown().Single().TaxRatePercent.Should().Be(10m);
    }

    [Fact]
    public void LinesWithTheSameEffectiveRate_AreGroupedEvenIfTheyComeFromDifferentBaseRates()
    {
        var order = Build(OrderDestination.Takeaway, null, 0, Line(1000, 1, 10m, takeaway: 5.5m), Line(500, 1, 5.5m));
        var b = order.CalculateTaxBreakdown();
        b.Should().ContainSingle();
        b[0].TaxRatePercent.Should().Be(5.5m);
        (b[0].TaxableBaseCents + b[0].TaxAmountCents).Should().Be(1500);
    }

    [Fact]
    public void FixedDiscountAboveTheTotal_ClampsEverythingToZero()
    {
        var order = Build(OrderDestination.EatIn, DiscountType.FixedAmount, 15, Line(1000, 1, 10m));
        order.TotalTtc.AmountInCents.Should().Be(0);
        order.DiscountAmount.AmountInCents.Should().Be(1000);
        order.CalculateTaxBreakdown().Single().Should().Match<TaxBreakdownItem>(x => x.TaxableBaseCents == 0 && x.TaxAmountCents == 0);
        AssertInvariants(order, nameof(FixedDiscountAboveTheTotal_ClampsEverythingToZero));
    }

    [Fact]
    public void FullComp_AndHundredPercentDiscount_GiveAZeroOrder()
    {
        var comp = Build(OrderDestination.EatIn, null, 0, Line(500, 2, 10m, comp: true));
        comp.TotalTtc.AmountInCents.Should().Be(0);
        AssertInvariants(comp, "comp");
        var full = Build(OrderDestination.EatIn, DiscountType.Percentage, 100, Line(500, 2, 10m), Line(300, 1, 20m));
        full.TotalTtc.AmountInCents.Should().Be(0);
        full.DiscountAmount.AmountInCents.Should().Be(1300);
        AssertInvariants(full, "100%");
    }

    [Fact]
    public void LineDiscountsModifiersAndQuantities_FeedTheGroups()
    {
        var order = Build(
            OrderDestination.EatIn, DiscountType.Percentage, 33.33m,
            Line(1000, 2, 10m, discountPercent: 10m), Line(500, 1, 10m, comp: true), Line(300, 3, 20m, extraCents: 50), Line(1, 1, 5.5m), Line(199, 7, 2.1m));
        AssertInvariants(order, nameof(LineDiscountsModifiersAndQuantities_FeedTheGroups));
    }

    [Fact]
    public void RandomOrders_KeepEveryInvariant()
    {
        var random = new Random(20261012);
        decimal[] rates = [0m, 2.1m, 5.5m, 10m, 20m];
        for (int i = 0; i < 2000; i++)
        {
            var items = Enumerable.Range(0, random.Next(1, 7)).Select(_ =>
            {
                decimal baseRate = rates[random.Next(rates.Length)];
                decimal? takeaway = random.Next(3) == 0 ? rates[random.Next(rates.Length)] : null;
                return Line(random.Next(1, 4000), random.Next(1, 6), baseRate, takeaway, random.Next(4) == 0 ? new[] { 10m, 33.33m, 100m }[random.Next(3)] : 0m, random.Next(12) == 0, random.Next(3) == 0 ? random.Next(0, 300) : 0);
            }).ToArray();
            int kind = random.Next(3);
            DiscountType? type = kind == 0 ? null : kind == 1 ? DiscountType.Percentage : DiscountType.FixedAmount;
            decimal value = kind == 1 ? new[] { 5m, 10m, 33.33m, 50m, 100m }[random.Next(5)] : kind == 2 ? random.Next(1, 8000) / 100m : 0m;
            var order = Build(random.Next(2) == 0 ? OrderDestination.EatIn : OrderDestination.Takeaway, type, value, items);
            AssertInvariants(order, $"random case {i}");
        }
    }
}
```
Note : si le projet n'utilise pas `FluentAssertions.Equal` sur des tuples nommés, comparer avec `.Select(x => $"{x.TaxRatePercent}|{x.TaxableBaseCents}|{x.TaxAmountCents}")` contre des chaînes ; ne pas changer les valeurs attendues (calculées à la main : 1000/1,1 = 909,09 → 909 ; 2000/1,2 = 1666,67 → 1667 ; 900/1,1 = 818,18 → 818 ; 1800/1,2 = 1500 ; 1000/1,055 = 947,87 → 948).

- [ ] **Step 4: Lancer les tests pour les voir échouer**

Run : `cd /Users/oussama/RiderProjects/POS_SK && dotnet test tests/RestaurantPos.Domain.Tests --filter "FullyQualifiedName~TaxAllocationTests|FullyQualifiedName~OrderTaxBreakdownTests" 2>&1 | tail -15`
Expected : erreurs de compilation (`TaxAllocation`, `Order.Subtotal`, `Order.DiscountAmount` absents).

- [ ] **Step 5: Implémenter**

Créer `src/RestaurantPos.Domain/ValueObjects/TaxAllocation.cs` :
```csharp
using System.Numerics;

namespace RestaurantPos.Domain.ValueObjects;

/// <summary>
/// Répartit un montant en centimes au prorata de poids, par la méthode du plus fort reste : somme des parts exacte,
/// jamais de part négative ni supérieure à son poids, résultat déterministe (restes égaux départagés par l'ordre des groupes).
/// </summary>
public static class TaxAllocation
{
    public static long[] Allocate(long total, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var shares = new long[weights.Count];
        if (weights.Count == 0 || total <= 0)
        {
            return shares;
        }

        long sum = 0;
        foreach (long weight in weights)
        {
            sum += weight;
        }

        if (sum <= 0)
        {
            return shares;
        }

        if (total >= sum)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                shares[i] = weights[i];
            }

            return shares;
        }

        var remainders = new (int Index, BigInteger Remainder)[weights.Count];
        long allocated = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            BigInteger product = (BigInteger)total * weights[i];
            shares[i] = (long)(product / sum);
            remainders[i] = (i, product % sum);
            allocated += shares[i];
        }

        long left = total - allocated;
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Remainder).ThenBy(r => r.Index).Take((int)left))
        {
            shares[index]++;
        }

        return shares;
    }
}
```
Dans `src/RestaurantPos.Domain/Entities/Order.cs`, ajouter sous `TotalHt` :
```csharp
    /// <summary>Somme des TTC de lignes, avant remise de commande.</summary>
    public Money Subtotal => new(Items.Sum(i => i.CalculateTotalTtc().AmountInCents));

    /// <summary>Montant de la remise de commande réellement appliquée (`Subtotal − TotalTtc`).</summary>
    public Money DiscountAmount => new(Subtotal.AmountInCents - CalculateTotalTtc().AmountInCents);
```
et remplacer entièrement `CalculateTaxBreakdown` par :
```csharp
    /// <summary>
    /// Ventilation fiscale de la commande : un groupe par taux effectif (ordre croissant), la remise de commande (pourcentage ou montant
    /// fixe) étant répartie sur les groupes au prorata de leur TTC (plus fort reste). Σ (base + TVA) = <see cref="TotalTtc"/>.
    /// </summary>
    public List<TaxBreakdownItem> CalculateTaxBreakdown()
    {
        var groups = Items
            .GroupBy(GetEffectiveTaxRate)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Ttc: g.Sum(x => x.CalculateTotalTtc().AmountInCents)))
            .ToList();
        long[] shares = TaxAllocation.Allocate(CalculateTotalTtc().AmountInCents, groups.Select(g => g.Ttc).ToList());
        return groups.Select((g, i) => TaxBreakdownItem.Calculate(g.Rate, shares[i])).ToList();
    }
```

- [ ] **Step 6: Lancer les tests du domaine et toute la suite .NET**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include src/RestaurantPos.Domain/ValueObjects/TaxAllocation.cs src/RestaurantPos.Domain/Entities/Order.cs tests/RestaurantPos.Domain.Tests/TaxAllocationTests.cs tests/RestaurantPos.Domain.Tests/OrderTaxBreakdownTests.cs
dotnet test tests/RestaurantPos.Domain.Tests 2>&1 | tail -6
dotnet test RestaurantPos.slnx 2>&1 | tail -12
```
Expected : tests du domaine verts. La suite complète peut maintenant **échouer** sur des tests qui figeaient l'ancien HT (`TotalHt`, `TakeawayTaxCalculationTests`, rapports) : c'est attendu. Pour chaque échec : lire le test, déterminer si l'ancienne valeur encodait l'écart corrigé (remise fixe ignorée, ordre des groupes) ou un vrai défaut ; corriger la valeur attendue **uniquement** dans le premier cas, en recalculant à la main et en le notant dans le rapport avec l'invariant qui la justifie. Ne rien affaiblir.

- [ ] **Step 7: Amender la spec**

Dans `docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md` :
- Section « Règle de calcul (.NET) », point 2 : remplacer « le **dernier groupe prenant le reste**, pour que la somme des parts soit exactement `Order.TotalTtc`. C'est la règle déjà appliquée par `OrderMath.totals` côté iPad. » par « selon la **méthode du plus fort reste** (somme exacte, aucune part négative, restes égaux départagés par l'ordre des groupes), qui remplace aussi la règle « dernier groupe prend le reste » de `OrderMath.totals` côté iPad (elle pouvait produire une part négative). Les groupes sont ordonnés par **taux effectif croissant**. »
- Section « Règlements partiels et pourboire » : remplacer le deuxième puce (répartition cumulée) et le troisième (repli) par : « La part d'un reçu de montant `X` est `Allocate(X, parts de la commande par taux)` (plus fort reste), sans rejouer l'historique des reçus. Pour chaque reçu, `TTC − HT = Σ TVA` exactement ; `Σ TTC des reçus = TTC de la commande` exactement ; par taux, la somme des reçus s'écarte du total de la commande d'au plus 1 centime par reçu (arrondi de la TVA ticket par ticket). »
- Section « Tests » > invariant 3 : remplacer « = ventilation de la commande, par taux, **exactement** » par « : `Σ TTC` exact, et écart par taux ≤ 1 centime par reçu ».
- Section « Risques » : supprimer la puce « Répartition cumulée ».

- [ ] **Step 8: Suivi et commit**

Créer `docs/plans/tva-recu-source-unique-progress.md` (modèle du suivi du sous-projet 2 : tableau Tâche / Statut / .NET / PosKit / findings) avec la ligne 1.
```bash
git add src/RestaurantPos.Domain tests/RestaurantPos.Domain.Tests docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md docs/plans/tva-recu-source-unique-progress.md
git add -u tests
git commit -m "feat(domain): ventilation fiscale de la commande au taux effectif avec répartition au plus fort reste"
```

---

### Task 2: Reçu tiré de la ventilation, scénarios partagés et invariants

**Files:**
- Create: `src/RestaurantPos.Domain/ValueObjects/ReceiptTaxCalculator.cs`
- Modify: `src/RestaurantPos.Infrastructure/Services/CheckoutPaymentService.cs` (calcul de TVA et de HT du reçu)
- Create: `tests/RestaurantPos.Infrastructure.Tests/FiscalScenarios.cs` (modèle, exécuteur, matrice, générateur aléatoire)
- Modify: `tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs` (utilise `FiscalScenarios`)
- Create: `tests/RestaurantPos.Infrastructure.Tests/ReceiptTaxInvariantTests.cs`
- Modify: tests existants qui figeaient les anciens montants (voir étape 6)

**Interfaces:**
- Consumes: `Order.CalculateTaxBreakdown`, `TaxAllocation`, `TaxBreakdownItem.Calculate` (tâche 1).
- Produces :
  - `public sealed record ReceiptTax(long TtcCents, long HtCents, IReadOnlyList<KeyValuePair<decimal, long>> VatByRate)` et `public static class ReceiptTaxCalculator { public static ReceiptTax Compute(Order order, long fiscalPaidCents); }` : `paid = clamp(fiscalPaidCents, 0, TotalTtc)` ; `paid == TotalTtc` ⇒ parts de la commande ; sinon `Allocate(paid, parts de la commande)` ; `VatByRate` en taux croissant (un élément par taux, TVA ≥ 0).
  - `internal static class FiscalScenarios` : `record LineSpec`, `record PaySpec`, `record ScenarioSpec`, `record GroupOutcome`, `record OrderOutcome`, `record ReceiptOutcome`, `record ScenarioOutcome`, `static IReadOnlyList<ScenarioSpec> All`, `static Task<ScenarioOutcome> RunAsync(ScenarioSpec spec)`.

- [ ] **Step 1: Écrire le calculateur de reçu (test d'abord)**

Créer `tests/RestaurantPos.Domain.Tests/ReceiptTaxCalculatorTests.cs` :
```csharp
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Domain.Tests;

public class ReceiptTaxCalculatorTests
{
    private static Order TwoRates(OrderDestination destination = OrderDestination.EatIn)
    {
        var order = new Order { TableNumber = "T1", Destination = destination };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(1000), TaxRatePercent = 10m });
        order.Items.Add(new OrderItem { ProductName = "B", Quantity = 1, UnitPrice = Money.FromCents(2000), TaxRatePercent = 20m });
        return order;
    }

    [Fact]
    public void FullPayment_IsTheOrderBreakdown()
    {
        var tax = ReceiptTaxCalculator.Compute(TwoRates(), 3000);
        tax.TtcCents.Should().Be(3000);
        tax.HtCents.Should().Be(2576);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((10m, 91L), (20m, 333L));
    }

    [Fact]
    public void PartialPayment_SpreadsTheAmountOnTheRates_AndStaysExact()
    {
        var tax = ReceiptTaxCalculator.Compute(TwoRates(), 1000); // parts exactes 333,33 / 666,67 -> 333 / 667
        tax.TtcCents.Should().Be(1000);
        (tax.TtcCents - tax.HtCents).Should().Be(tax.VatByRate.Sum(v => v.Value));
        tax.VatByRate.Should().OnlyContain(v => v.Value >= 0);
    }

    [Fact]
    public void ThePaidAmountIsClampedToTheOrderTotal_AndToZero()
    {
        ReceiptTaxCalculator.Compute(TwoRates(), 99_999).TtcCents.Should().Be(3000);
        ReceiptTaxCalculator.Compute(TwoRates(), -10).TtcCents.Should().Be(0);
    }

    [Fact]
    public void ZeroTotalOrder_GivesZeroVatPerRate()
    {
        var order = new Order { TableNumber = "T1" };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(500), TaxRatePercent = 10m, IsComp = true });
        var tax = ReceiptTaxCalculator.Compute(order, 0);
        tax.TtcCents.Should().Be(0);
        tax.HtCents.Should().Be(0);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((10m, 0L));
    }

    [Fact]
    public void TakeawayReceipt_IsDeclaredAtTheReducedRate()
    {
        var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway };
        order.Items.Add(new OrderItem { ProductName = "A", Quantity = 1, UnitPrice = Money.FromCents(1000), TaxRatePercent = 10m, TaxRateTakeawayPercent = 5.5m });
        var tax = ReceiptTaxCalculator.Compute(order, 1000);
        tax.VatByRate.Select(v => (v.Key, v.Value)).Should().Equal((5.5m, 52L));
        tax.HtCents.Should().Be(948);
    }
}
```
Run (échec attendu : type absent) : `dotnet test tests/RestaurantPos.Domain.Tests --filter ReceiptTaxCalculatorTests 2>&1 | tail -6`.

- [ ] **Step 2: Implémenter le calculateur**

Créer `src/RestaurantPos.Domain/ValueObjects/ReceiptTaxCalculator.cs` :
```csharp
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Domain.ValueObjects;

public sealed record ReceiptTax(long TtcCents, long HtCents, IReadOnlyList<KeyValuePair<decimal, long>> VatByRate);

/// <summary>
/// Montants fiscaux d'un reçu : une seule source, <see cref="Order.CalculateTaxBreakdown"/>. Un reçu qui encaisse tout le TTC de la
/// commande reprend ses parts ; un reçu partiel répartit son montant sur les parts de la commande (plus fort reste), sans rejouer l'historique.
/// Pour chaque reçu : TTC − HT = Σ TVA, exactement.
/// </summary>
public static class ReceiptTaxCalculator
{
    public static ReceiptTax Compute(Order order, long fiscalPaidCents)
    {
        ArgumentNullException.ThrowIfNull(order);
        var breakdown = order.CalculateTaxBreakdown();
        long orderTtc = order.CalculateTotalTtc().AmountInCents;
        long paid = Math.Clamp(fiscalPaidCents, 0, orderTtc);
        long[] orderShares = breakdown.Select(b => b.TaxableBaseCents + b.TaxAmountCents).ToArray();
        long[] shares = paid == orderTtc ? orderShares : TaxAllocation.Allocate(paid, orderShares);

        long ht = 0;
        var vat = new List<KeyValuePair<decimal, long>>(breakdown.Count);
        for (int i = 0; i < breakdown.Count; i++)
        {
            var part = TaxBreakdownItem.Calculate(breakdown[i].TaxRatePercent, shares[i]);
            ht += part.TaxableBaseCents;
            vat.Add(new KeyValuePair<decimal, long>(breakdown[i].TaxRatePercent, part.TaxAmountCents));
        }

        return new ReceiptTax(paid, ht, vat);
    }
}
```
Run : `dotnet test tests/RestaurantPos.Domain.Tests 2>&1 | tail -4` → tous verts.

- [ ] **Step 3: Brancher `CheckoutPaymentService`**

Dans `ProcessPaymentTendersAsync`, **remplacer** le bloc allant de `// Montant fiscal de ce paiement …` jusqu'à la ligne `long receiptHtCents = …;` incluse par :
```csharp
                // Montant fiscal de ce paiement : jamais le pourboire. Avec pourboire, la part fiscale est bornée
                // au TTC restant après les reçus antérieurs (hors avoirs) ; sans pourboire, le montant payé.
                long orderTtcCents = order.TotalTtc.AmountInCents;
                bool hasTip = order.TipAmount.AmountInCents > 0;
                long priorFiscalTtcCents = priorReceipts
                    .Where(r => r.VoidedReceiptId == null)
                    .Sum(r => r.TotalTtcAmount.AmountInCents);
                long fiscalPaidCents = hasTip
                    ? Math.Min(totalPaidCents, Math.Max(0, orderTtcCents - priorFiscalTtcCents))
                    : totalPaidCents;

                // Une seule source de calcul : la ventilation de la commande (taux effectif, remise), répartie sur le reçu.
                var receiptTax = ReceiptTaxCalculator.Compute(order, fiscalPaidCents);
                var vatDict = new Dictionary<decimal, long>();
                foreach (var kvp in receiptTax.VatByRate)
                {
                    vatDict[kvp.Key] = kvp.Value;
                }

                string taxJson = JsonSerializer.Serialize(vatDict);
                long receiptTtcCents = receiptTax.TtcCents;
                long receiptHtCents = receiptTax.HtCents;
```
et supprimer devenus inutiles `isPartialReceipt`, `paymentRatio` (le compilateur signale tout reste). Ajouter `using RestaurantPos.Domain.ValueObjects;` si absent (il y est déjà). Ne pas toucher à `VoidReceiptAsync`.

- [ ] **Step 4: Créer les scénarios partagés**

Créer `tests/RestaurantPos.Infrastructure.Tests/FiscalScenarios.cs` : déplacer depuis `FiscalReferenceFixtureTests.cs` les types `LineSpec`, `Pay`, `ScenarioSpec`, `Rate(...)`, `RunScenarioAsync` (renommés ci-dessous) et la liste `Scenarios`, puis les étendre. Contenu cible :
```csharp
using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Infrastructure.Tests;

internal sealed record LineSpec(long UnitPriceCents, int Quantity, decimal Rate, decimal? TakeawayRate = null, decimal DiscountPercent = 0, bool IsComp = false, long ModifiersExtraCents = 0);

/// <summary>
/// Un règlement : montant TTC fiscal encaissé (le pourboire est ajouté au dernier), moyen, et espèces remises en plus (rendu monnaie).
/// Un montant négatif (<see cref="Rest"/>) veut dire « tout le reste de la commande » : le test le calcule à partir du total réel.
/// </summary>
internal sealed record PaySpec(long AmountCents, PaymentMethod Method = PaymentMethod.CreditCard, long ExtraTenderedCents = 0)
{
    public static PaySpec Rest(PaymentMethod method = PaymentMethod.CreditCard, long extraTenderedCents = 0) => new(-1, method, extraTenderedCents);
}

internal sealed record ScenarioSpec(
    string Name,
    OrderDestination Destination,
    LineSpec[] Lines,
    PaySpec[] Payments,
    DiscountType? GlobalType = null,
    decimal GlobalValue = 0,
    long TipCents = 0,
    int? VoidPayment = null);

internal sealed record GroupOutcome(decimal Rate, long TtcCents, long BaseHtCents, long VatCents);

internal sealed record OrderOutcome(long SubtotalCents, long DiscountCents, long TotalTtcCents, long TotalHtCents, long TotalVatCents, IReadOnlyList<GroupOutcome> Groups);

/// <summary><c>TenderAmountCents</c> : somme réglée pour ce reçu, pourboire compris sur le dernier (0 pour une annulation).</summary>
internal sealed record ReceiptOutcome(long TtcCents, long HtCents, string TaxJson, IReadOnlyList<KeyValuePair<decimal, long>> VatByRate, long ChangeGivenCents, long RemainingBalanceCents, long TenderAmountCents = 0);

internal sealed record ScenarioOutcome(ScenarioSpec Spec, OrderOutcome Order, IReadOnlyList<ReceiptOutcome> Payments, ReceiptOutcome? Voided);

internal static class FiscalScenarios
{
    public static string Rate(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static Order BuildOrder(ScenarioSpec spec)
    {
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

        return order;
    }

    public static OrderOutcome DescribeOrder(Order order)
    {
        var groups = order.CalculateTaxBreakdown();
        return new OrderOutcome(
            order.Subtotal.AmountInCents, order.DiscountAmount.AmountInCents, order.TotalTtc.AmountInCents, order.TotalHt.AmountInCents,
            order.TotalTtc.AmountInCents - order.TotalHt.AmountInCents,
            groups.Select(g => new GroupOutcome(g.TaxRatePercent, g.TaxableBaseCents + g.TaxAmountCents, g.TaxableBaseCents, g.TaxAmountCents)).ToList());
    }

    public static async Task<ScenarioOutcome> RunAsync(ScenarioSpec spec)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("FiscalScenario_" + Guid.NewGuid().ToString("N")).Options;
        using var db = new AppDbContext(options);
        var service = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));

        var order = BuildOrder(spec);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var orderOutcome = DescribeOrder(order);

        var payments = new List<ReceiptOutcome>();
        var receiptIds = new List<Guid>();
        long paidSoFar = 0;
        for (int p = 0; p < spec.Payments.Length; p++)
        {
            var pay = spec.Payments[p];
            long fiscalAmount = pay.AmountCents < 0 ? orderOutcome.TotalTtcCents - paidSoFar : pay.AmountCents;
            paidSoFar += fiscalAmount;
            long amount = fiscalAmount + (p == spec.Payments.Length - 1 ? spec.TipCents : 0);
            var tenders = amount == 0
                ? new List<PaymentTenderRequest>()
                : [new PaymentTenderRequest(pay.Method, amount, pay.Method == PaymentMethod.Cash ? amount + pay.ExtraTenderedCents : amount)];
            var result = await service.ProcessPaymentTendersAsync(order.Id, "T01", tenders);
            result.IsSuccess.Should().BeTrue($"{spec.Name} payment {p}");
            var receipt = await db.FiscalReceipts.SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber);
            receiptIds.Add(receipt.Id);
            payments.Add(ToOutcome(receipt, result.ChangeGivenCents, result.RemainingBalanceCents) with { TenderAmountCents = amount });
        }

        paidSoFar.Should().Be(orderOutcome.TotalTtcCents, $"{spec.Name}: les règlements doivent solder exactement la commande");

        ReceiptOutcome? voided = null;
        if (spec.VoidPayment is { } index)
        {
            (await service.VoidReceiptAsync(receiptIds[index], "T01", Guid.NewGuid())).IsSuccess.Should().BeTrue();
            var voidReceipt = await db.FiscalReceipts.SingleAsync(r => r.VoidedReceiptId == receiptIds[index]);
            voided = ToOutcome(voidReceipt, 0, 0);
        }

        return new ScenarioOutcome(spec, orderOutcome, payments, voided);
    }

    private static ReceiptOutcome ToOutcome(FiscalReceipt receipt, long change, long remaining)
    {
        var vat = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.TaxBreakdownJson)!
            .Select(kv => new KeyValuePair<decimal, long>(decimal.Parse(kv.Key, CultureInfo.InvariantCulture), kv.Value)).ToList();
        return new ReceiptOutcome(receipt.TotalTtcAmount.AmountInCents, receipt.TotalHtAmount.AmountInCents, receipt.TaxBreakdownJson, vat, change, remaining);
    }

    private static PaySpec Card(long cents) => new(cents);

    /// <summary>Cas écrits à la main (plus lisibles à relire) puis jeu aléatoire déterministe.</summary>
    public static IReadOnlyList<ScenarioSpec> All { get; } = [.. Handwritten(), .. Random(20261011, 120)];

    private static IEnumerable<ScenarioSpec> Handwritten()
    {
        // total TTC calculé à la main dans chaque commentaire
        yield return new("simple-eat-in", OrderDestination.EatIn, [new(2000, 2, 10m)], [new(4000, PaymentMethod.Cash, 1000)], VoidPayment: 0); // 4000
        yield return new("multi-rate", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m), new(450, 3, 5.5m)], [Card(4380)]); // 1250+1780+1350
        yield return new("takeaway-reduced-rate", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m)], [Card(1000)]);
        yield return new("takeaway-one-reduced-line-among-others", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m), new(2000, 1, 20m), new(500, 1, 10m)], [Card(3500)]);
        yield return new("global-percent-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [Card(2700)], DiscountType.Percentage, 10m);
        yield return new("global-percent-33.33", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m), new(777, 1, 5.5m)], [PaySpec.Rest()], DiscountType.Percentage, 33.33m);
        yield return new("global-percent-100", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [new(0)], DiscountType.Percentage, 100m);
        yield return new("global-fixed-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [new(2700, PaymentMethod.Cash, 300)], DiscountType.FixedAmount, 3m);
        yield return new("global-fixed-equal-to-total", OrderDestination.EatIn, [new(1000, 1, 10m)], [new(0)], DiscountType.FixedAmount, 10m);
        yield return new("global-fixed-above-total", OrderDestination.EatIn, [new(1000, 1, 10m)], [new(0)], DiscountType.FixedAmount, 15m);
        yield return new("split-two-payments", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m)], [Card(1000), new(2030, PaymentMethod.Cash, 500)], VoidPayment: 1);
        yield return new("split-three-equal-with-remainder", OrderDestination.EatIn, [new(1000, 1, 10m), new(1000, 1, 20m), new(1, 1, 5.5m)], [Card(668), Card(667), Card(666)], VoidPayment: 0); // 2001
        yield return new("split-five-with-discount-and-takeaway", OrderDestination.Takeaway, [new(999, 1, 10m, 5.5m), new(1999, 1, 20m), new(1, 1, 0m)], [Card(500), Card(500), Card(500), Card(500), PaySpec.Rest()], DiscountType.Percentage, 33.33m);
        yield return new("tip-single", OrderDestination.EatIn, [new(1250, 1, 10m)], [new(1250, PaymentMethod.Cash, 750)], TipCents: 200);
        yield return new("tip-with-split", OrderDestination.EatIn, [new(1250, 1, 10m), new(2000, 1, 20m)], [Card(1000), Card(2250)], TipCents: 350, VoidPayment: 1);
        yield return new("line-discount-comp-and-modifiers", OrderDestination.EatIn, [new(1000, 2, 10m, DiscountPercent: 10m), new(500, 1, 10m, IsComp: true), new(300, 1, 20m, ModifiersExtraCents: 50)], [Card(2150)]); // 1800 + 0 + 350
        yield return new("zero-total-comp", OrderDestination.EatIn, [new(500, 1, 10m, IsComp: true)], [new(0)]);
        yield return new("one-cent", OrderDestination.EatIn, [new(1, 1, 20m)], [Card(1)]);
        yield return new("many-rates", OrderDestination.EatIn, [new(333, 1, 0m), new(333, 1, 2.1m), new(333, 1, 5.5m), new(333, 1, 10m), new(333, 1, 20m)], [PaySpec.Rest()], DiscountType.FixedAmount, 0.07m);
        yield return new("big-total", OrderDestination.EatIn, [new(9_999_999, 9, 10m), new(8_888_888, 7, 20m)], [PaySpec.Rest()]);
    }

    private static IEnumerable<ScenarioSpec> Random(int seed, int count)
    {
        var random = new System.Random(seed);
        decimal[] rates = [0m, 2.1m, 5.5m, 10m, 20m];
        for (int i = 0; i < count; i++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 7)).Select(_ => new LineSpec(
                random.Next(1, 4000), random.Next(1, 6), rates[random.Next(rates.Length)],
                random.Next(3) == 0 ? rates[random.Next(rates.Length)] : null,
                random.Next(4) == 0 ? new[] { 10m, 33.33m, 100m }[random.Next(3)] : 0m,
                random.Next(12) == 0,
                random.Next(3) == 0 ? random.Next(0, 300) : 0)).ToArray();
            int kind = random.Next(3);
            DiscountType? type = kind == 0 ? null : kind == 1 ? DiscountType.Percentage : DiscountType.FixedAmount;
            decimal value = kind == 1 ? new[] { 5m, 10m, 33.33m, 50m, 100m }[random.Next(5)] : kind == 2 ? random.Next(1, 8000) / 100m : 0m;
            var destination = random.Next(2) == 0 ? OrderDestination.EatIn : OrderDestination.Takeaway;
            long tip = random.Next(4) == 0 ? random.Next(1, 500) : 0;

            // les montants de règlement sont dérivés du total réel de la commande
            var probe = BuildOrder(new ScenarioSpec("probe", destination, lines, [], type, value, tip));
            long total = probe.TotalTtc.AmountInCents;
            int parts = new[] { 1, 1, 2, 3, 5 }[random.Next(5)];
            var amounts = Split(total, parts, random);
            var payments = amounts.Select((a, idx) => new PaySpec(a, idx % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.CreditCard, random.Next(0, 800))).ToArray();
            int? voidIndex = random.Next(4) == 0 && payments.Length > 0 ? random.Next(payments.Length) : null;
            yield return new ScenarioSpec($"random-{seed}-{i}", destination, lines, payments, type, value, tip, voidIndex);
        }
    }

    private static long[] Split(long total, int parts, System.Random random)
    {
        if (parts <= 1 || total < parts)
        {
            return [total];
        }

        var cuts = Enumerable.Range(0, parts - 1).Select(_ => random.NextInt64(1, total)).OrderBy(c => c).ToList();
        var result = new List<long>();
        long previous = 0;
        foreach (long cut in cuts)
        {
            result.Add(cut - previous);
            previous = cut;
        }

        result.Add(total - previous);
        return result.Where(r => r > 0).ToArray();
    }
}
```
Notes pour l'implémenteur :
- Les règlements d'un scénario doivent solder **exactement** le TTC réel de la commande (hors pourboire) : `RunAsync` l'asserte (`paidSoFar == TotalTtc`). Les cas écrits à la main dont le total dépend d'arrondis utilisent `PaySpec.Rest()` ; pour les autres les montants sont vérifiés à la main dans les commentaires. Si l'assertion échoue sur un cas écrit à la main, corriger le montant du cas d'après le total réel, jamais le code de production. Un cas à total nul a `new(0)` : aucun tender (`amount == 0`).
- Jeu aléatoire : si `Split` produit un total différent de la commande, corriger `Split`, pas l'assertion.
- Si `FiscalReferenceFixtureTests` utilisait des noms identiques (`LineSpec`, `Pay`, …), les supprimer de ce fichier et le faire passer par `FiscalScenarios` (étape 5).

- [ ] **Step 5: Faire utiliser `FiscalScenarios` à la fixture existante**

Dans `FiscalReferenceFixtureTests.cs` : supprimer `LineSpec`, `Pay`, `ScenarioSpec`, `Scenarios`, `Rate`, `RunScenarioAsync` ; `BuildAsync` itère `FiscalScenarios.All` (en écrivant les mêmes champs JSON qu'avant — `name`, `destination`, `globalDiscountType`, `globalDiscountValue`, `tipCents`, `lines`, `payments[{tenders, expected}]`, `voided` — à partir de `ScenarioOutcome`). Le détail et l'enrichissement de la fixture sont faits à la tâche 3 ; ici on garde le format et on vérifie seulement que la fixture se régénère : `WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests` doit passer.

- [ ] **Step 6: Invariants sur tous les scénarios**

Créer `tests/RestaurantPos.Infrastructure.Tests/ReceiptTaxInvariantTests.cs` :
```csharp
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ReceiptTaxInvariantTests
{
    public static IEnumerable<object[]> Names() => FiscalScenarios.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryScenarioKeepsTheFiscalInvariants(string name)
    {
        var spec = FiscalScenarios.All.Single(s => s.Name == name);
        var outcome = await FiscalScenarios.RunAsync(spec);
        string context = $"{name} (lines: {string.Join("; ", spec.Lines)})";
        var order = outcome.Order;

        // commande
        order.Groups.Sum(g => g.TtcCents).Should().Be(order.TotalTtcCents, context);
        order.Groups.Sum(g => g.BaseHtCents).Should().Be(order.TotalHtCents, context);
        order.Groups.Sum(g => g.VatCents).Should().Be(order.TotalVatCents, context);
        (order.TotalTtcCents - order.TotalHtCents).Should().Be(order.TotalVatCents, context);
        order.Groups.Should().OnlyContain(g => g.BaseHtCents + g.VatCents == g.TtcCents && g.TtcCents >= 0, context);
        order.DiscountCents.Should().Be(order.SubtotalCents - order.TotalTtcCents, context);
        order.Groups.Select(g => g.Rate).Should().BeInAscendingOrder(context);

        // reçus
        foreach (var (receipt, index) in outcome.Payments.Select((r, i) => (r, i)))
        {
            string at = $"{context} payment {index}";
            (receipt.TtcCents - receipt.HtCents).Should().Be(receipt.VatByRate.Sum(v => v.Value), at);
            receipt.VatByRate.Should().OnlyContain(v => v.Value >= 0, at);
            receipt.HtCents.Should().BeGreaterThanOrEqualTo(0, at);
            receipt.VatByRate.Select(v => v.Key).Should().Equal(order.Groups.Select(g => g.Rate), at);
        }

        // Σ TTC des reçus = TTC de la commande (commande soldée) ; écart par taux ≤ 1 centime par reçu
        outcome.Payments.Sum(r => r.TtcCents).Should().Be(order.TotalTtcCents, context);
        outcome.Payments.Last().RemainingBalanceCents.Should().Be(0, context);
        for (int g = 0; g < order.Groups.Count; g++)
        {
            long vatAcrossReceipts = outcome.Payments.Sum(r => r.VatByRate[g].Value);
            Math.Abs(vatAcrossReceipts - order.Groups[g].VatCents).Should().BeLessThanOrEqualTo(outcome.Payments.Count, $"{context} rate {order.Groups[g].Rate}");
        }

        // annulation : négation exacte de l'original
        if (outcome.Voided is { } voided && spec.VoidPayment is { } v)
        {
            var original = outcome.Payments[v];
            voided.TtcCents.Should().Be(-original.TtcCents, context);
            voided.HtCents.Should().Be(-original.HtCents, context);
            voided.VatByRate.Select(x => x.Value).Should().Equal(original.VatByRate.Select(x => -x.Value), context);
        }
    }

    [Fact]
    public async Task TakeawayAtReducedRate_IsDeclaredUnderThatRate_NeverUnderTheBaseRate()
    {
        var spec = FiscalScenarios.All.Single(s => s.Name == "takeaway-reduced-rate");
        var outcome = await FiscalScenarios.RunAsync(spec);
        outcome.Payments.Single().TaxJson.Should().Be("{\"5.5\":52}");
        outcome.Payments.Single().HtCents.Should().Be(948);
    }

    [Fact]
    public async Task ZeroTotalOrders_GiveAZeroReceipt()
    {
        foreach (var name in new[] { "zero-total-comp", "global-percent-100", "global-fixed-equal-to-total", "global-fixed-above-total" })
        {
            var outcome = await FiscalScenarios.RunAsync(FiscalScenarios.All.Single(s => s.Name == name));
            var receipt = outcome.Payments.Single();
            receipt.TtcCents.Should().Be(0, name);
            receipt.HtCents.Should().Be(0, name);
            receipt.VatByRate.Should().OnlyContain(v => v.Value == 0, name);
        }
    }

    [Fact]
    public void ScenarioNamesAreUnique_AndTheMatrixIsBigEnough()
    {
        FiscalScenarios.All.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        FiscalScenarios.All.Count.Should().BeGreaterThanOrEqualTo(120);
    }
}
```

- [ ] **Step 7: Lancer, réviser les tests existants, commit**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include src/RestaurantPos.Domain/ValueObjects/ReceiptTaxCalculator.cs src/RestaurantPos.Infrastructure/Services/CheckoutPaymentService.cs tests/RestaurantPos.Infrastructure.Tests/FiscalScenarios.cs tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs tests/RestaurantPos.Infrastructure.Tests/ReceiptTaxInvariantTests.cs tests/RestaurantPos.Domain.Tests/ReceiptTaxCalculatorTests.cs
dotnet test RestaurantPos.slnx 2>&1 | tail -20
```
Expected : `ReceiptTaxInvariantTests` verts. Les échecs restants sont des tests existants (`CheckoutPaymentServiceTests`, `ReportAccuracyTests`, `NF525FiscalAuditTests`, `PeriodClosureTests`, `FecExportServiceTests`, …) qui figeaient l'ancien calcul : appliquer la règle des Global Constraints (réviser chacun, justifier par invariant, jamais affaiblir). Regénérer ensuite la fixture : `WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests` puis relancer sans la variable. Suivi : ligne 2 de `docs/plans/tva-recu-source-unique-progress.md`.
```bash
git add -A src tests ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json docs/plans/tva-recu-source-unique-progress.md
git commit -m "fix(fiscal): TVA et HT du reçu tirés de la ventilation de la commande (taux effectif, remise, règlements partiels)"
```
Remarque : les tests PosKit `FiscalReceiptMathTests` échouent tant que la tâche 4 n'est pas faite (la fixture a changé) : c'est attendu entre les tâches 2 et 4 ; ne pas les lancer ici.

---

### Task 3: Fixture enrichie (totaux de commande et reçus) générée par le .NET

**Files:**
- Modify: `tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json` (régénéré)

**Interfaces:**
- Consumes: `FiscalScenarios.All`, `FiscalScenarios.RunAsync`, `ScenarioOutcome`.
- Produces : dans chaque `taxScenarios[]`, un bloc `order` (`subtotalCents`, `discountCents`, `totalTtcCents`, `totalHtCents`, `totalVatCents`, `groups[{rate, ttcCents, baseHtCents, vatCents}]`) en plus des champs existants (`payments[].expected` contient `totalTtcCents`, `totalHtCents`, `taxBreakdownJson`, `changeGivenCents`, `remainingBalanceCents` ; `voided` contient `paymentIndex`, `totalTtcCents`, `totalHtCents`, `taxBreakdownJson`). Taux écrits en chaînes invariantes (`"5.5"`).

- [ ] **Step 1: Écrire la sortie JSON des scénarios**

Dans `FiscalReferenceFixtureTests.BuildAsync`, pour chaque `ScenarioOutcome o` produire :
```csharp
new
{
    name = o.Spec.Name,
    destination = (int)o.Spec.Destination,
    globalDiscountType = o.Spec.GlobalType.HasValue ? (int?)o.Spec.GlobalType.Value : null,
    globalDiscountValue = FiscalScenarios.Rate(o.Spec.GlobalValue),
    tipCents = o.Spec.TipCents,
    lines = o.Spec.Lines.Select(l => new
    {
        unitPriceCents = l.UnitPriceCents,
        quantity = l.Quantity,
        taxRatePercent = FiscalScenarios.Rate(l.Rate),
        takeawayRatePercent = l.TakeawayRate is { } t ? FiscalScenarios.Rate(t) : null,
        discountPercent = FiscalScenarios.Rate(l.DiscountPercent),
        isComp = l.IsComp,
        modifiersExtraCents = l.ModifiersExtraCents,
    }).ToList(),
    order = new
    {
        subtotalCents = o.Order.SubtotalCents,
        discountCents = o.Order.DiscountCents,
        totalTtcCents = o.Order.TotalTtcCents,
        totalHtCents = o.Order.TotalHtCents,
        totalVatCents = o.Order.TotalVatCents,
        groups = o.Order.Groups.Select(g => new { rate = FiscalScenarios.Rate(g.Rate), ttcCents = g.TtcCents, baseHtCents = g.BaseHtCents, vatCents = g.VatCents }).ToList(),
    },
    payments = o.Payments.Select((r, i) => new
    {
        tenders = new[] { new { amountCents = r.TenderAmountCents } },
        expected = new { totalTtcCents = r.TtcCents, totalHtCents = r.HtCents, taxBreakdownJson = r.TaxJson, changeGivenCents = r.ChangeGivenCents, remainingBalanceCents = r.RemainingBalanceCents },
    }).ToList(),
    voided = o.Voided is { } v ? new { paymentIndex = o.Spec.VoidPayment, totalTtcCents = v.TtcCents, totalHtCents = v.HtCents, taxBreakdownJson = v.TaxJson } : null,
}
```
Le champ `tenders` de chaque paiement ne garde que `amountCents` (la somme réglée par ce reçu, pourboire compris sur le dernier) : c'est tout ce dont le Swift a besoin.

- [ ] **Step 2: Régénérer et vérifier**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include tests/RestaurantPos.Infrastructure.Tests/FiscalReferenceFixtureTests.cs
WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests 2>&1 | tail -4
dotnet test tests/RestaurantPos.Infrastructure.Tests --filter FullyQualifiedName~FiscalReferenceFixtureTests 2>&1 | tail -4
```
Expected : `Passed: 1` deux fois ; la fixture contient `order` et `groups` pour chaque scénario ; `takeaway-reduced-rate` donne `{"5.5":52}` et HT 948 ; `hashVectors` inchangés.

- [ ] **Step 3: Suivi et commit**

Ligne 3 du suivi.
```bash
git add tests/RestaurantPos.Infrastructure.Tests ios/Packages/PosKit/Tests/PosKitTests/Fixtures/fiscal_reference.json docs/plans/tva-recu-source-unique-progress.md
git commit -m "test(fiscal): fixture de référence enrichie (totaux de commande, taux et montants de TVA par groupe)"
```

---

### Task 4: Swift : répartition au plus fort reste, `OrderMath.totals` et `FiscalReceiptMath` alignés

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Core/OrderMath.swift` (`allocate`, `totals`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/FiscalReceiptMath.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/FiscalReceiptMathTests.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/OrderMathAllocationTests.swift`
- Modify: tests PosKit existants qui figeaient les anciennes valeurs (`MoneyAndMathTests`, `LocalFiscalPaymentTests`, …)

**Interfaces:**
- Consumes: fixture enrichie (tâche 3), `OrderMath.split`, `OrderMath.lineTotal`, `Money.roundAwayFromZero`.
- Produces :
  - `OrderMath.allocate(total: Int, weights: [Int]) -> [Int]` (public, plus fort reste, mêmes propriétés que `TaxAllocation.Allocate`).
  - `OrderMath.totals(...)` : groupes par taux effectif **croissant**, parts de remise par `allocate`, `vatLines` inchangé en forme.
  - `FiscalReceiptMath.amounts(order:totalPaidCents:tipCents:priorFiscalTtcCents:) -> FiscalReceiptAmounts` : même signature, calcul = `ReceiptTaxCalculator` ; `negatedTaxJson` inchangé.

- [ ] **Step 1: Tests Swift de la répartition (ils échouent)**

Créer `ios/Packages/PosKit/Tests/PosKitTests/OrderMathAllocationTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Répartition au plus fort reste")
struct OrderMathAllocationTests {
    @Test func totalEqualToTheSumReturnsTheWeights() {
        #expect(OrderMath.allocate(total: 3005, weights: [1000, 2000, 5]) == [1000, 2000, 5])
    }

    @Test func totalAboveTheSumIsCapped() {
        #expect(OrderMath.allocate(total: 9999, weights: [100, 200]) == [100, 200])
    }

    @Test(arguments: [
        (3, [1, 1, 1, 1, 1], [1, 1, 1, 0, 0]),
        (2, [1, 1, 1], [1, 1, 0]),
        (100, [1, 1, 1], [34, 33, 33]),
        (2700, [1000, 2000], [900, 1800]),
        (1, [3, 3], [1, 0]),
        (5, [0, 10, 0], [0, 5, 0]),
        (0, [10, 20], [0, 0]),
        (-5, [10, 20], [0, 0]),
        (50, [0, 0], [0, 0]),
    ])
    func knownCases(total: Int, weights: [Int], expected: [Int]) {
        #expect(OrderMath.allocate(total: total, weights: weights) == expected)
    }

    @Test func noGroupsGivesNothing() {
        #expect(OrderMath.allocate(total: 50, weights: []).isEmpty)
    }

    @Test func keepsEveryInvariantOnManyRandomCases() {
        var generator = SplitMix64(seed: 20261011)
        for index in 0..<5000 {
            let count = Int(generator.next() % 6) + 1
            let weights = (0..<count).map { _ in Int(generator.next() % 100_000) }
            let sum = weights.reduce(0, +)
            let total = sum == 0 ? 0 : Int(generator.next() % UInt64(sum + 1))
            let shares = OrderMath.allocate(total: total, weights: weights)
            let context = "case \(index): total=\(total) weights=\(weights)"
            #expect(shares.reduce(0, +) == min(total, sum), Comment(rawValue: context))
            for (share, weight) in zip(shares, weights) {
                #expect(share >= 0 && share <= weight, Comment(rawValue: context))
                let exact = sum == 0 ? 0 : Double(total) * Double(weight) / Double(sum)
                #expect(abs(Double(share) - exact) < 1.0 + 1e-9, Comment(rawValue: context))
            }
        }
    }
}

/// Générateur déterministe (graine fixe) : un échec est reproductible.
struct SplitMix64: RandomNumberGenerator {
    private var state: UInt64
    init(seed: UInt64) { state = seed }
    mutating func next() -> UInt64 {
        state &+= 0x9E37_79B9_7F4A_7C15
        var z = state
        z = (z ^ (z >> 30)) &* 0xBF58_476D_1CE4_E5B9
        z = (z ^ (z >> 27)) &* 0x94D0_49BB_1331_11EB
        return z ^ (z >> 31)
    }
}
```
Si `SplitMix64` existe déjà dans les tests (tâche finale du sous-projet 2), supprimer cette copie et réutiliser l'existante. Run (échec attendu : `OrderMath.allocate` absent) : `cd ios/Packages/PosKit && swift test --filter OrderMathAllocationTests 2>&1 | tail -6`.

- [ ] **Step 2: Implémenter `allocate` et aligner `totals`**

Dans `OrderMath.swift`, ajouter dans `enum OrderMath` :
```swift
    /// Répartit `total` (centimes) au prorata de `weights` par la méthode du plus fort reste : somme exacte, aucune part négative ni
    /// supérieure à son poids, restes égaux départagés par l'ordre des groupes. Miroir de `TaxAllocation.Allocate` (.NET).
    public static func allocate(total: Int, weights: [Int]) -> [Int] {
        var shares = [Int](repeating: 0, count: weights.count)
        let sum = weights.reduce(0, +)
        guard total > 0, sum > 0 else { return shares }
        if total >= sum { return weights }
        var remainders: [(index: Int, remainder: Int)] = []
        var allocated = 0
        for (index, weight) in weights.enumerated() {
            let product = total * weight // <= ~1e16 : tient dans un Int 64 bits
            shares[index] = product / sum
            remainders.append((index, product % sum))
            allocated += shares[index]
        }
        let left = total - allocated
        let order = remainders.sorted { $0.remainder != $1.remainder ? $0.remainder > $1.remainder : $0.index < $1.index }
        for entry in order.prefix(left) { shares[entry.index] += 1 }
        return shares
    }
```
Remplacer dans `totals(lines:destination:discount:)` toute la boucle de répartition (de `let rates = groups.keys.sorted()` jusqu'à la construction de `vatLines`) par :
```swift
        let rates = groups.keys.sorted()
        let shares = allocate(total: total.cents, weights: rates.map { groups[$0]!.cents })
        var vatLines: [VatLine] = []
        for (rate, shareCents) in zip(rates, shares) {
            let share = Money(cents: shareCents)
            let parts = split(ttc: share, ratePercent: rate)
            vatLines.append(VatLine(ratePercent: rate, baseHt: parts.ht, vat: parts.vat, ttc: share))
        }
```
Conserver le calcul de `subtotal`, `total`, `groups` et la fin de la fonction (`ht`, `OrderTotals(...)`). Le sous-total vient de `lineTotal`, la remise = `subtotal − total` comme avant.

- [ ] **Step 3: Réécrire `FiscalReceiptMath.amounts`**

Remplacer le corps de `amounts` (garder la signature, la doc et `negatedTaxJson`) par :
```swift
    static func amounts(order: ActiveOrder, totalPaidCents: Int, tipCents: Int, priorFiscalTtcCents: Int) -> FiscalReceiptAmounts {
        let cart = order.lines.map(CartLine.init(serverLine:))
        let totals = OrderMath.totals(lines: cart, destination: order.destination, discount: order.globalDiscount)
        let orderTtc = totals.totalTtc.cents
        let hasTip = tipCents > 0
        // Montant fiscal du reçu : jamais le pourboire ; avec pourboire il est borné au TTC restant après les reçus antérieurs.
        let fiscalPaid = hasTip ? min(totalPaidCents, max(0, orderTtc - priorFiscalTtcCents)) : totalPaidCents
        let paid = min(max(fiscalPaid, 0), orderTtc)
        let orderShares = totals.vatLines.map { $0.ttc.cents }
        let shares = paid == orderTtc ? orderShares : OrderMath.allocate(total: paid, weights: orderShares)

        var htCents = 0
        var entries: [String] = []
        for (line, share) in zip(totals.vatLines, shares) {
            let parts = OrderMath.split(ttc: Money(cents: share), ratePercent: line.ratePercent)
            htCents += parts.ht.cents
            entries.append("\"\(line.ratePercent)\":\(parts.vat.cents)")
        }
        return FiscalReceiptAmounts(ttcCents: paid, htCents: htCents, taxBreakdownJson: "{" + entries.joined(separator: ",") + "}")
    }
```
Supprimer `htCents(_:destination:discount:)` privé et `bankersRound` (plus utilisés ; vérifier par `grep -rn bankersRound ios/Packages/PosKit` avant de supprimer). Mettre à jour le commentaire d'en-tête de l'enum : plus aucun écart reproduit, la règle est celle de `ReceiptTaxCalculator`.

- [ ] **Step 4: Rejouer toute la fixture enrichie**

Dans `FiscalReceiptMathTests.swift`, étendre les structures de décodage avec le bloc `order` (`subtotalCents`, `discountCents`, `totalTtcCents`, `totalHtCents`, `totalVatCents`, `groups[{rate (String), ttcCents, baseHtCents, vatCents}]`) et les `tenders` réduits à `amountCents` (le champ `method` / `tenderedCents` n'existe plus), puis ajouter ce test **à côté** de `everyScenarioMatchesTheBackend` :
```swift
    @Test func everyOrderTotalsMatchTheBackend() throws {
        for scenario in try Self.scenarios() {
            let order = try Self.order(scenario)
            let totals = OrderMath.totals(lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination, discount: order.globalDiscount)
            let expected = scenario.order
            #expect(totals.subtotalTtc.cents == expected.subtotalCents, "\(scenario.name) sous-total")
            #expect(totals.discountAmount.cents == expected.discountCents, "\(scenario.name) remise")
            #expect(totals.totalTtc.cents == expected.totalTtcCents, "\(scenario.name) total TTC")
            #expect(totals.totalHt.cents == expected.totalHtCents, "\(scenario.name) HT")
            #expect(totals.totalVat.cents == expected.totalVatCents, "\(scenario.name) TVA")
            #expect(totals.vatLines.count == expected.groups.count, "\(scenario.name) nombre de taux")
            for (line, group) in zip(totals.vatLines, expected.groups) {
                #expect("\(line.ratePercent)" == group.rate, "\(scenario.name) taux")
                #expect(line.ttc.cents == group.ttcCents && line.baseHt.cents == group.baseHtCents && line.vat.cents == group.vatCents, "\(scenario.name) taux \(group.rate)")
            }
        }
    }
```
Dans `everyScenarioMatchesTheBackend`, la somme payée d'un reçu est `payment.tenders.reduce(0) { $0 + $1.amountCents }` (inchangé) et le test garde ses comparaisons TTC / HT / JSON. Ajouter dans cette boucle les invariants pour chaque reçu : `amounts.ttcCents - amounts.htCents == Σ TVA du JSON` (extraire les valeurs avec la même expression régulière que `negatedTaxJson`) et `amounts.taxBreakdownJson == payment.expected.taxBreakdownJson`.
Adapter `static func order(_:)` : `OrderLine` n'a plus besoin de la méthode de paiement.

- [ ] **Step 5: Lancer les tests PosKit et réviser les existants**

Run : `cd /Users/oussama/RiderProjects/POS_SK/ios && ./scripts/test.sh unit 2>&1 | tail -15`
Expected : `OrderMathAllocationTests`, `FiscalReceiptMathTests` verts. Les échecs restants (`MoneyAndMathTests` sur `totals`, tests de paiement) sont examinés un par un selon la règle des Global Constraints (la règle « dernier groupe prend le reste » est remplacée ; recalculer à la main, justifier, ne pas affaiblir).

- [ ] **Step 6: Suivi et commit**

Ligne 4 du suivi (PosKit n).
```bash
git add -A ios docs/plans/tva-recu-source-unique-progress.md
git commit -m "fix(ios): répartition au plus fort reste et reçu fiscal Swift identique au .NET"
```

---

### Task 5: Journée simulée : modèle de données, grand livre indépendant et harnais d'API

**Files:**
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DayScenario.cs` (modèle des opérations, sérialisé en JSON)
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DayLedgerOracle.cs` (grand livre indépendant)
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DayRunner.cs` (rejeu par l'API)
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DayOracleTests.cs`
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DaySimulationSmokeTests.cs`

**Interfaces:**
- Consumes: `PosApiApplicationFactory`, `DeviceTestHelper.PairAsync`, endpoints `/api/auth/login`, `/api/tables/...`, `/api/checkout/pay`, `/api/fiscal/x-report`, `/api/fiscal/z-closure`.
- Produces :
  - `DayScenario` : `{ seed, terminals[], operations[] }` ; `DayOperation` (type : `table_order`, `counter_order`, `x_report`, `z_closure`, `hold_then_void`, `void_receipt`, `reprint`) avec les champs décrits ci-dessous.
  - `DayLedgerOracle` : calcule, **sans appeler le code de production** (arithmétique propre en `decimal`/`BigInteger`), les totaux attendus par terminal : `ttcCents`, `htCents`, `receiptCount`, `vatByRate`, `tenderByMethod`.
  - `DayRunner.RunAsync(factory, scenario) : DayRunResult` (réponses X / Z, ids des reçus, journal).

- [ ] **Step 1: Cartographier l'API réelle avant d'écrire du code**

Run (lecture seule) et noter dans le rapport les réponses exactes de chaque endpoint (corps de requête, code de statut, forme JSON) :
```bash
cd /Users/oussama/RiderProjects/POS_SK
sed -n 20,70p src/RestaurantPos.Api/Endpoints/TableEndpoints.cs
sed -n 96,260p src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs
sed -n 26,160p src/RestaurantPos.Api/Endpoints/CheckoutEndpoints.cs
sed -n 195,266p src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs
sed -n 267,310p src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs
grep -rn "SeedDemoUsers\|\"1234\"\|\"2468\"\|\"9999\"" src/RestaurantPos.Api/Program.cs | head
grep -n "class TestAuthHandler" -A30 tests/RestaurantPos.Api.Tests/*.cs | head -50
```
Déterminer notamment : (a) comment obtenir un jeton par PIN pour plusieurs serveurs (comptes seedés `1234` responsable, `2468` serveur, `9999` administrateur ; en créer d'autres via l'API de gestion du personnel ou `AppDbContext` + le hachage du service d'authentification) ; (b) `TestAuthHandler` accepte-t-il un jeton `Bearer` ordinaire en environnement `Testing` ; (c) ce que renvoie `/api/checkout/pay` (numéro de reçu, rendu monnaie) et comment retrouver l'identifiant du reçu (via `AppDbContext.FiscalReceipts`) ; (d) la forme exacte de `/api/fiscal/x-report` (`FiscalSummaryDto`, clés de dictionnaires, enum des moyens de paiement sérialisé en nom ou en entier) et de `/api/fiscal/latest-closure` (taxes et moyens de paiement du Z) ; (e) les paramètres de `/api/fiscal/fec`. Écrire ces constats en tête de `DayRunner.cs` en commentaire.

- [ ] **Step 2: Écrire le modèle de scénario**

Créer `tests/RestaurantPos.Api.Tests/DaySimulation/DayScenario.cs` :
```csharp
using System.Text.Json.Serialization;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Une journée décrite comme des données : rejouable par le .NET (API réelle) et, plus tard, par l'iPad (Swift).</summary>
public sealed record DayScenario(int Seed, IReadOnlyList<DayTerminal> Terminals, IReadOnlyList<DayOperation> Operations);

/// <summary>Un poste de caisse appairé ; ses reçus portent l'identifiant de terminal attribué par l'appairage (T01, T02…).</summary>
public sealed record DayTerminal(string Key, string Name);

public sealed record DayLine(
    string Name,
    int Quantity,
    long UnitPriceCents,
    decimal TaxRatePercent,
    decimal? TaxRateTakeawayPercent = null,
    long ModifiersExtraCents = 0,
    bool Comp = false);

public sealed record DayTender(string Method, long AmountCents, long ExtraTenderedCents = 0);

public sealed record DayOrderDiscount(string Type, decimal Value);

/// <summary>
/// Opérations : <c>table_order</c> (sur place), <c>counter_order</c> (comptoir / à emporter), <c>hold_then_void</c> (mise en attente puis
/// annulation par PIN responsable), <c>void_receipt</c>, <c>reprint</c>, <c>x_report</c>, <c>z_closure</c>.
/// Les champs inutiles pour un type sont nuls.
/// </summary>
public sealed record DayOperation(
    string Type,
    string? Terminal = null,
    string? Waiter = null,
    string? Table = null,
    int Covers = 0,
    string? Destination = null,
    IReadOnlyList<DayLine>? Lines = null,
    DayOrderDiscount? Discount = null,
    long TipCents = 0,
    IReadOnlyList<IReadOnlyList<DayTender>>? Payments = null,
    int? VoidPaymentIndex = null,
    bool Reprint = false,
    bool HoldAndRecall = false);

[JsonSerializable(typeof(DayScenario))]
internal sealed partial class DayScenarioJsonContext : JsonSerializerContext;
```
Si `JsonSerializerContext` complique la compilation (analyseurs), le supprimer et utiliser `JsonSerializer` avec `JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }` : la contrainte est que le JSON produit soit en `camelCase`, stable et lisible par Swift.

- [ ] **Step 3: Grand livre indépendant (test d'abord)**

Créer `tests/RestaurantPos.Api.Tests/DaySimulation/DayOracleTests.cs` avec des cas calculés **à la main** :
```csharp
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

public class DayOracleTests
{
    [Fact]
    public void OneEatInOrderPaidInCash_IsLedgeredExactly()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "5", 2, "EatIn",
            [new DayLine("Menu", 2, 2000, 10m)], null, 0, [[new DayTender("Cash", 4000, 1000)]]);
        var ledger = DayLedgerOracle.Compute([order], new Dictionary<string, string> { ["T1"] = "T01" });
        var t = ledger.ByTerminal["T01"];
        t.TtcCents.Should().Be(4000);
        t.HtCents.Should().Be(3636); // 4000 / 1,1 = 3636,36
        t.VatByRate[10m].Should().Be(364);
        t.TenderByMethod["Cash"].Should().Be(4000);
        t.ReceiptCount.Should().Be(1);
    }

    [Fact]
    public void TakeawayLineAtReducedRate_IsLedgeredAtThatRate()
    {
        var order = new DayOperation("counter_order", "T1", "waiter1", null, 0, "Takeaway",
            [new DayLine("Sandwich", 1, 1000, 10m, 5.5m)], null, 0, [[new DayTender("CreditCard", 1000)]]);
        var t = DayLedgerOracle.Compute([order], new Dictionary<string, string> { ["T1"] = "T01" }).ByTerminal["T01"];
        t.VatByRate.Should().ContainSingle().Which.Should().Be(new KeyValuePair<decimal, long>(5.5m, 52));
        t.HtCents.Should().Be(948);
    }

    [Fact]
    public void FixedOrderDiscount_IsSpreadOnRates_AndAVoidedReceiptNetsToZero()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "2", 2, "EatIn",
            [new DayLine("A", 1, 1000, 10m), new DayLine("B", 1, 2000, 20m)], new DayOrderDiscount("FixedAmount", 3m), 0,
            [[new DayTender("CreditCard", 2700)]], VoidPaymentIndex: 0);
        var t = DayLedgerOracle.Compute([order], new Dictionary<string, string> { ["T1"] = "T01" }).ByTerminal["T01"];
        t.ReceiptCount.Should().Be(2);          // le reçu et son annulation
        t.TtcCents.Should().Be(0);
        t.HtCents.Should().Be(0);
        t.VatByRate.Values.Should().OnlyContain(v => v == 0);
        t.TenderByMethod["CreditCard"].Should().Be(0);
    }

    [Fact]
    public void SplitPaymentWithTip_ExcludesTheTipFromFiscalAmounts_ButNotFromTenders()
    {
        var order = new DayOperation("table_order", "T1", "waiter1", "3", 2, "EatIn",
            [new DayLine("A", 1, 1250, 10m), new DayLine("B", 1, 2000, 20m)], null, 350,
            [[new DayTender("CreditCard", 1000)], [new DayTender("CreditCard", 2600)]]); // 3250 + 350 de pourboire
        var t = DayLedgerOracle.Compute([order], new Dictionary<string, string> { ["T1"] = "T01" }).ByTerminal["T01"];
        t.TtcCents.Should().Be(3250);
        t.ReceiptCount.Should().Be(2);
        t.TenderByMethod["CreditCard"].Should().Be(3600); // les règlements contiennent le pourboire
    }
}
```
Les valeurs : 4000 → HT 3636, TVA 364 ; 1000 à 5,5 % → HT 948, TVA 52 ; remise fixe 3 € : total 2700 (voir tâche 1) ; avec pourboire, `Z.PaymentTotals` additionne les montants des `PaymentTenders` (qui incluent le pourboire, voir l'étape 1 pour confirmer que le reçu .NET enregistre bien le tender complet) : **si l'API réelle se comporte autrement, le fait de l'API fait foi et la règle de l'oracle est corrigée avec la justification dans le rapport**, jamais l'inverse sans expliquer.

- [ ] **Step 4: Implémenter l'oracle**

Créer `tests/RestaurantPos.Api.Tests/DaySimulation/DayLedgerOracle.cs` : implémentation **indépendante** de la ventilation (ne pas référencer `Order`, `TaxAllocation`, `ReceiptTaxCalculator`) :
```csharp
using System.Numerics;

namespace RestaurantPos.Api.Tests.DaySimulation;

public sealed class TerminalLedger
{
    public long TtcCents { get; set; }
    public long HtCents { get; set; }
    public int ReceiptCount { get; set; }
    public SortedDictionary<decimal, long> VatByRate { get; } = [];
    public Dictionary<string, long> TenderByMethod { get; } = [];
}

public sealed class DayLedger
{
    public Dictionary<string, TerminalLedger> ByTerminal { get; } = [];
    public TerminalLedger Total { get; } = new();
}

/// <summary>
/// Grand livre de la journée, calculé à partir des entrées (lignes, taux, remises, règlements) avec une arithmétique entière propre au test,
/// indépendante du code de production. Règles : TTC de ligne = (prix + suppléments) × quantité (0 si gratuit) ; groupes par taux effectif
/// croissant ; remise de commande répartie au plus fort reste ; HT = TTC / (1 + taux) arrondi au centime supérieur (demi éloigné de zéro).
/// </summary>
public static class DayLedgerOracle
{
    public static DayLedger Compute(IEnumerable<DayOperation> operations, IReadOnlyDictionary<string, string> terminalIds)
    {
        var ledger = new DayLedger();
        foreach (var op in operations.Where(o => o.Type is "table_order" or "counter_order"))
        {
            var terminal = terminalIds[op.Terminal!];
            if (!ledger.ByTerminal.TryGetValue(terminal, out var t))
            {
                ledger.ByTerminal[terminal] = t = new TerminalLedger();
            }

            Apply(op, t);
        }

        foreach (var t in ledger.ByTerminal.Values)
        {
            ledger.Total.TtcCents += t.TtcCents;
            ledger.Total.HtCents += t.HtCents;
            ledger.Total.ReceiptCount += t.ReceiptCount;
            foreach (var (rate, vat) in t.VatByRate) { ledger.Total.VatByRate[rate] = ledger.Total.VatByRate.GetValueOrDefault(rate) + vat; }
            foreach (var (method, amount) in t.TenderByMethod) { ledger.Total.TenderByMethod[method] = ledger.Total.TenderByMethod.GetValueOrDefault(method) + amount; }
        }

        return ledger;
    }

    private static void Apply(DayOperation op, TerminalLedger t)
    {
        bool takeaway = op.Destination == "Takeaway";
        var lines = op.Lines ?? [];
        var groups = lines
            .GroupBy(l => takeaway && l.TaxRateTakeawayPercent.HasValue ? l.TaxRateTakeawayPercent.Value : l.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Ttc: g.Sum(l => l.Comp ? 0L : (l.UnitPriceCents + l.ModifiersExtraCents) * l.Quantity)))
            .ToList();
        long subtotal = groups.Sum(g => g.Ttc);
        long total = subtotal - DiscountCents(op.Discount, subtotal);
        if (total < 0) total = 0;
        long[] orderShares = Allocate(total, groups.Select(g => g.Ttc).ToArray());

        // règlements : le pourboire est porté par le dernier ; le TTC fiscal d'un reçu est borné au restant dû
        var payments = op.Payments ?? [];
        long fiscalPaidSoFar = 0;
        var sales = new List<(long Ttc, long Ht, long[] Vat)>();
        for (int p = 0; p < payments.Count; p++)
        {
            long tendered = payments[p].Sum(x => x.AmountCents);
            long tipPart = p == payments.Count - 1 ? op.TipCents : 0;
            long fiscal = Math.Min(tendered - tipPart, total - fiscalPaidSoFar);
            if (fiscal < 0) fiscal = 0;
            fiscalPaidSoFar += fiscal;
            long[] shares = fiscal == total ? orderShares : Allocate(fiscal, orderShares);
            long ht = 0;
            var vat = new long[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                (long baseHt, long tax) = Split(shares[g], groups[g].Rate);
                ht += baseHt;
                vat[g] = tax;
            }

            sales.Add((fiscal, ht, vat));
            AddReceipt(t, groups.Select(g => g.Rate).ToArray(), fiscal, ht, vat, +1);
            foreach (var tender in payments[p])
            {
                t.TenderByMethod[tender.Method] = t.TenderByMethod.GetValueOrDefault(tender.Method) + tender.AmountCents;
            }
        }

        if (op.VoidPaymentIndex is { } index)
        {
            var original = sales[index];
            AddReceipt(t, groups.Select(g => g.Rate).ToArray(), original.Ttc, original.Ht, original.Vat, -1);
            foreach (var tender in payments[index])
            {
                t.TenderByMethod[tender.Method] -= tender.AmountCents;
            }
        }
    }

    private static void AddReceipt(TerminalLedger t, decimal[] rates, long ttc, long ht, long[] vat, int sign)
    {
        t.TtcCents += sign * ttc;
        t.HtCents += sign * ht;
        t.ReceiptCount++;
        for (int g = 0; g < rates.Length; g++)
        {
            t.VatByRate[rates[g]] = t.VatByRate.GetValueOrDefault(rates[g]) + sign * vat[g];
        }
    }

    private static long DiscountCents(DayOrderDiscount? discount, long subtotal)
    {
        if (discount is null || discount.Value <= 0) return 0;
        if (discount.Type == "Percentage")
        {
            long discounted = (long)Math.Round(subtotal * (1m - discount.Value / 100m), MidpointRounding.AwayFromZero);
            return subtotal - Math.Max(0, discounted);
        }

        return Math.Min(subtotal, (long)Math.Round(discount.Value * 100m, MidpointRounding.AwayFromZero));
    }

    /// <summary>HT = TTC / (1 + taux), arrondi au centime (demi éloigné de zéro) ; TVA = TTC − HT.</summary>
    private static (long BaseHt, long Tax) Split(long ttc, decimal rate)
    {
        long ht = (long)Math.Round(ttc / (1m + rate / 100m), MidpointRounding.AwayFromZero);
        return (ht, ttc - ht);
    }

    /// <summary>Plus fort reste avec des entiers exacts (BigInteger), ordre des groupes pour départager.</summary>
    private static long[] Allocate(long total, long[] weights)
    {
        var shares = new long[weights.Length];
        BigInteger sum = weights.Aggregate(BigInteger.Zero, (a, w) => a + w);
        if (total <= 0 || sum == 0) return shares;
        if (total >= sum) return weights.ToArray();
        var rem = new List<(int I, BigInteger R)>();
        long used = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            var product = (BigInteger)total * weights[i];
            shares[i] = (long)(product / sum);
            rem.Add((i, product % sum));
            used += shares[i];
        }

        foreach (var (i, _) in rem.OrderByDescending(x => x.R).ThenBy(x => x.I).Take((int)(total - used))) shares[i]++;
        return shares;
    }
}
```
Run : `dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~DayOracleTests 2>&1 | tail -8` → verts.

- [ ] **Step 5: Harnais d'API et une journée minimale**

Créer `tests/RestaurantPos.Api.Tests/DaySimulation/DayRunner.cs` : `DayRunner.RunAsync(PosApiApplicationFactory factory, DayScenario scenario)` qui :
1. appaire un `HttpClient` par `DayTerminal` avec `DeviceTestHelper.PairAsync` (un client par terminal, chacun avec son jeton d'appareil) et mémorise `terminalIds[key] = paired.TerminalId` ;
2. obtient un jeton `Bearer` par serveur (`/api/auth/login` avec le PIN, comptes créés au besoin) et pose l'en-tête `Authorization` avant chaque opération ;
3. exécute chaque `DayOperation` dans l'ordre, par les endpoints (jamais par la base) :
   - `table_order` : `POST /api/tables/{t}/open` (`OpenTableRequest(WaiterName, Covers, OperatorId)`), `POST /api/tables/{t}/items` avec des `OrderItemInputDto` (ProductId = `Guid.NewGuid()`, `TaxRatePercent`, `TaxRateTakeawayPercent`, `ModifiersPriceExtra` en euros), `POST /api/orders/{orderId}/discount` si `Discount`, `POST /api/orders/{orderId}/items/{lineId}/comp` pour les lignes `Comp` (les identifiants de ligne viennent de `GET /api/tables/{t}/order`), `POST /api/tables/{t}/dispatch`, puis pour chaque règlement `POST /api/checkout/pay` (`PaymentSettlementRequest`, montants en euros, `TipAmount` sur le dernier) ; `VoidPaymentIndex` → `POST /api/checkout/void/{receiptId}` (jeton responsable) ;
   - `counter_order` : `POST /api/orders/counter/direct`, lignes via `POST /api/tables/Comptoir/items`, remise, `POST /api/orders/counter/checkout` ;
   - `x_report` : `GET /api/fiscal/x-report?terminalId=…` ; `z_closure` : `POST /api/fiscal/z-closure` (`ZClosureRequest`) avec le jeton responsable ;
4. renvoie `DayRunResult` : pour chaque `x_report` et `z_closure` la réponse brute (`JsonElement`), plus la liste des numéros et identifiants de reçus par terminal.
Ne pas inventer de route : tout écart avec le plan (nom, corps, code) se corrige d'après l'étape 1 et se note dans le rapport.

Créer `DaySimulationSmokeTests.cs` : une journée minimale (2 terminaux, 2 serveurs, une commande sur place réglée en espèces, une vente à emporter à 5,5 % réglée par carte, un X par terminal, un Z par terminal) et vérifier X = Z = grand livre pour TTC, HT, nombre de reçus, TVA par taux, total par moyen de paiement :
```csharp
using FluentAssertions;
using Xunit;

namespace RestaurantPos.Api.Tests.DaySimulation;

public class DaySimulationSmokeTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DaySimulationSmokeTests(PosApiApplicationFactory factory) => _factory = factory;

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

        var run = await DayRunner.RunAsync(_factory, scenario);
        var ledger = DayLedgerOracle.Compute(scenario.Operations, run.TerminalIds);

        foreach (var (key, id) in run.TerminalIds)
        {
            var expected = ledger.ByTerminal[id];
            DayAssert.SummaryEquals(run.XReports[key].Single(), expected, $"X {key}");
            DayAssert.SummaryEquals(run.ZClosures[key].Single(), expected, $"Z {key}");
        }
    }
}
```
`DayAssert.SummaryEquals(JsonElement, TerminalLedger, string)` est un helper de test (fichier `DayAssert.cs`) qui lit `totalSalesTtcCents`, `totalSalesHtCents`, `receiptCount`, `vatBreakdownCents`, `paymentTotalsCents` (ou les champs équivalents du Z relus par `/api/fiscal/latest-closure`) selon ce que l'étape 1 a établi. Run : `dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~DaySimulation 2>&1 | tail -12`.

- [ ] **Step 6: Suivi et commit**

Ligne 5 du suivi.
```bash
git add -A tests docs/plans/tva-recu-source-unique-progress.md
git commit -m "test(fiscal): harnais de journée simulée, grand livre indépendant et journée minimale X/Z"
```

---

### Task 6: Journée simulée complète : tous les cas, cohérence X/Z, deux jours, intégrité, JSON partagé

**Files:**
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DayScenarioGenerator.cs`
- Create: `tests/RestaurantPos.Api.Tests/DaySimulation/DaySimulationTests.cs`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/day_scenario.json` (généré)
- Modify: `tests/RestaurantPos.Api.Tests/DaySimulation/DayRunner.cs`, `DayAssert.cs` si nécessaire

**Interfaces:**
- Consumes: `DayScenario`, `DayLedgerOracle`, `DayRunner`, `DayAssert` (tâche 5).
- Produces : `DayScenarioGenerator.Build(int seed) : DayScenario` (déterministe) ; `day_scenario.json` (`{ seed, terminals, operations, expected: {terminal: {ttcCents, htCents, receiptCount, vatByRate, tenderByMethod}}, total }`) ; tests `DaySimulationTests`.

- [ ] **Step 1: Générateur déterministe**

Créer `DayScenarioGenerator.Build(seed)` : 3 terminaux (`A`, `B`, `C`), 4 serveurs, 40 à 60 opérations tirées avec `new Random(seed)` : commandes sur place (tables 1..12, couverts 1..6) et comptoir, lignes 1..6 (taux 0 / 2,1 / 5,5 / 10 / 20, taux réduit à emporter sur ~30 % des lignes, suppléments, gratuités ~8 %), remise de commande (aucune 50 %, pourcentage 25 %, montant fixe 25 %), pourboire 25 %, règlements 1 à 5 (espèces avec rendu monnaie, carte, `MealVoucher`, `GiftCard`) dont le total est calculé avec les mêmes règles que `DayLedgerOracle` (extraire sa partie « total de commande » en méthode publique `DayLedgerOracle.OrderTotalCents(op)`), annulation d'un reçu ~15 % (toujours avant tout `z_closure`), duplicata ~10 %, mise en attente puis annulation par PIN ~5 %, mise en attente puis rappel ~5 %. Les opérations `x_report` sont insérées toutes les ~12 opérations (un par terminal) ; toutes les commandes sont soldées ou annulées avant le premier `z_closure` ; la journée se termine par un `x_report` puis un `z_closure` par terminal. Ajouter les cas imposés de la spec (une commande à total nul, une remise fixe supérieure au total, un règlement de 1 centime, un règlement fractionné en 5 avec reste) en tête de liste pour qu'ils soient présents quelle que soit la graine.

- [ ] **Step 2: Tests de la journée complète**

Créer `DaySimulationTests.cs` (IClassFixture<PosApiApplicationFactory>, un test indépendant par vérification de la spec, tous sur `Build(20261011)` sauf mention) :
```csharp
[Fact] public async Task LedgerMatchesX_BeforeAnyClosure_AndXIsReadOnly() { /* X pris en cours de journée = grand livre des opérations déjà jouées ; deux X consécutifs identiques ; aucune écriture fiscale créée par X (compter FiscalReceipts, DailyFiscalClosures, JournalEntries avant/après) */ }
[Fact] public async Task ZIsRefusedWhileAnOrderIsOpen_ThenAccepted() { /* laisser une table ouverte sur un autre terminal : POST z-closure → 409 code "open_orders" ; solder ; Z accepté */ }
[Fact] public async Task ZEqualsTheLedger_PerTerminal() { /* TTC, HT, receiptCount (annulations comprises), TVA par taux, total par moyen de paiement ; TTC − HT = Σ TVA ; Σ moyens = TTC + pourboires */ }
[Fact] public async Task XJustBeforeZ_EqualsZ_AndXAfterZStartsAtZero() { }
[Fact] public async Task TerminalsSumToTheRoom_AndPerpetualTotalGrowsByTheDayTtc() { }
[Fact] public async Task ASecondDayChainsWithoutDoubleCounting() { /* quelques opérations puis second Z : ClosureSequence = 2, hash précédent = signature du premier Z, grand total perpétuel = Σ TTC des deux jours */ }
[Fact] public async Task VoidAfterClosureIsRefused() { /* annuler un reçu d'avant le Z → 409 void_after_closure */ }
[Fact] public async Task ChainsAreIntactAndTheJournalHasEveryEvent() { /* POST /api/fiscal/verify sans anomalie ; JET : Z_CLOSURE par clôture, RECEIPT_VOIDED par annulation, DUPLICATE_PRINTED par duplicata, EVENT_HELD_ORDER_VOIDED par annulation de mise en attente */ }
[Fact] public async Task TheFecIsBalancedAndItsVatMatchesTheLedger() { /* GET /api/fiscal/fec : débit = crédit au centime ; TVA collectée par taux = grand livre */ }
[Fact] public async Task TheSameSeedGivesTheSameTotals() { /* deux factories : mêmes totaux par terminal (hors identifiants et horodatages) */ }
[Fact] public void TheScenarioFileMatchesTheGenerator() { /* day_scenario.json == sérialisation de Build(seed) + grand livre ; WRITE_FISCAL_FIXTURE=1 le régénère (même mécanisme que fiscal_reference.json) */ }
```
Chaque corps est à écrire en s'appuyant sur `DayRunner`, `DayLedgerOracle`, `DayAssert` : **aucun corps ne doit rester vide** ; en cas de divergence entre l'API réelle et la règle attendue, appliquer la consigne de l'étape 3 de la tâche 5 (le fait de l'API fait foi, justification dans le rapport). En cas d'échec d'une assertion, le message doit afficher la graine, l'index de l'opération et le terminal.

- [ ] **Step 3: Générer `day_scenario.json`, lancer, commit**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
dotnet format RestaurantPos.slnx --include tests/RestaurantPos.Api.Tests/DaySimulation
WRITE_FISCAL_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~DaySimulation 2>&1 | tail -15
dotnet test tests/RestaurantPos.Api.Tests --filter FullyQualifiedName~DaySimulation 2>&1 | tail -8
dotnet test RestaurantPos.slnx 2>&1 | tail -8
```
Expected : tous verts. Ligne 6 du suivi, puis :
```bash
git add -A tests ios/Packages/PosKit/Tests/PosKitTests/Fixtures/day_scenario.json docs/plans/tva-recu-source-unique-progress.md
git commit -m "test(fiscal): journée simulée complète, X et Z comparés au grand livre, deux journées et intégrité des chaînes"
```

---

### Task 7: Documentation, rappel du rejeu iPad et vérification finale

**Files:**
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift »)
- Modify: `docs/fiscal.md` (si une formule de TVA du reçu y figure)
- Modify: `docs/superpowers/specs/2026-10-11-tva-recu-source-unique-design.md` (statut)
- Modify: `docs/plans/ipad-standalone-2-progress.md` (condition 8 : format du scénario)
- Modify: `docs/plans/tva-recu-source-unique-progress.md`

**Interfaces:**
- Consumes: tout ce qui précède.
- Produces: documentation à jour ; plus aucune mention d'écart .NET/Swift reproduit.

- [ ] **Step 1: Mettre à jour `CLAUDE.md`**

Dans le paragraphe « Parité fiscale .NET/Swift » : supprimer les phrases qui décrivent les deux écarts reproduits (« Écarts .NET reproduits volontairement … ») et les remplacer par : « La TVA et le HT d'un reçu viennent d'une seule ventilation, `Order.CalculateTaxBreakdown` (taux effectif, remise de commande de tout type répartie au plus fort reste, groupes par taux croissant) ; `ReceiptTaxCalculator` (.NET) et `FiscalReceiptMath` / `OrderMath.allocate` (Swift) en sont le miroir, et `fiscal_reference.json` (commandes + reçus) le prouve. Pour chaque reçu `TTC − HT = Σ TVA` ; la somme des reçus d'une commande est exacte en TTC et à 1 centime par reçu et par taux en TVA. La journée simulée (`tests/RestaurantPos.Api.Tests/DaySimulation`, données dans `day_scenario.json`) doit être rejouée côté iPad dès que ses rapports X et Z existent. » Garder les phrases sur la chambre (reçu avec règlement « chambre », annulation refusée), les dates stockées (`storedText`) et la sauvegarde.

- [ ] **Step 2: Vérifier `docs/fiscal.md`**

Run : `grep -n "TaxBreakdown\|ventilation\|taxBreakdownJson" docs/fiscal.md | head`. Si le document décrit le JSON de TVA du reçu, y ajouter : taux effectif, groupes en ordre croissant, remise répartie au plus fort reste ; sinon ne rien changer et le noter.

- [ ] **Step 3: Statuts et conditions**

- Spec : `Statut : en attente de relecture` devient `Statut : implémentée (plan 2026-10-11-tva-recu-source-unique)`.
- `docs/plans/ipad-standalone-2-progress.md`, condition 8 : ajouter la phrase « Le scénario est dans `ios/Packages/PosKit/Tests/PosKitTests/Fixtures/day_scenario.json` (opérations + grand livre attendu par terminal). »
- `docs/plans/tva-recu-source-unique-progress.md` : ligne 7 et totaux finaux.

- [ ] **Step 4: Vérifications finales**

Run :
```bash
cd /Users/oussama/RiderProjects/POS_SK
grep -rn "Écarts .NET reproduits\|bankersRound\|reproduit.*écart" CLAUDE.md ios/Packages/PosKit/Sources | head
dotnet build RestaurantPos.slnx 2>&1 | tail -3
dotnet test RestaurantPos.slnx 2>&1 | tail -8
cd ios && ./scripts/test.sh unit 2>&1 | tail -3 && ./scripts/test.sh ui 2>&1 | tail -6
```
Expected : le `grep` ne renvoie rien ; build sans avertissement ; tous les tests verts ; UI 52 verts (aucun test UI ne dépend des montants de reçu ; si l'un échoue, l'examiner selon la règle des Global Constraints).

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md docs
git commit -m "docs: TVA du reçu à source unique, journée simulée et rappel du rejeu iPad"
```

---

## Self-review (exécutée à l'écriture du plan)

- **Couverture de la spec** : source unique de ventilation (tâche 1) ; reçu complet et partiel, pourboire, annulation (tâche 2) ; fixture commande + reçus (tâche 3) ; parité Swift et `OrderMath.totals` (tâche 4) ; matrice de cas et jeu aléatoire (tâches 1, 2, 4) ; journée simulée avec X/Z, deux jours, intégrité, FEC, reproductibilité, données JSON pour l'iPad (tâches 5 et 6) ; docs, retrait des écarts, rappel du rejeu iPad (tâche 7). Les trois rulings du plan amendent la spec en tâche 1.
- **Placeholders** : aucun `TBD`. Les endroits où le plan ne peut pas connaître la forme exacte d'une API (réponses de `/api/fiscal/x-report`, authentification multi-serveurs, routes) sont traités par une étape de cartographie explicite (tâche 5, étape 1) et par la règle « le fait de l'API fait foi, justification au rapport ». Les montants de paiement des cas écrits à la main sont validés par une assertion qui compare à `TotalTtc`.
- **Cohérence des types** : `TaxAllocation.Allocate` (1) → `ReceiptTaxCalculator` (2) ; `Order.Subtotal/DiscountAmount` (1) → `FiscalScenarios.DescribeOrder` (2) → fixture (3) → `OrderMath.totals` Swift (4) ; `DayLedgerOracle` (5) → `DayScenarioGenerator` (6).
- **Review Focus** : 1 → tests d'invariants (1, 2, 4, 6) ; 2 → cas à total nul (1, 2, 4) ; 3 → `takeaway-one-reduced-line-among-others` et golden (1, 2) ; 4 → `tip-with-split`, `split-*` + annulation (2, 3) ; 5 → `ZIsRefusedWhileAnOrderIsOpen…` et `VoidAfterClosureIsRefused` (6).
