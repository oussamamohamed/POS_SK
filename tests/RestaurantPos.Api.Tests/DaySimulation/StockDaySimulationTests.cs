using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Journée de stock (SC-003, SC-004) : ventes, avoirs avec et sans restitution, réceptions, pertes et inventaires rejoués par les services réels ;
/// le stock final et le coût des fiches doivent égaler l'oracle indépendant, et les chaînes fiscales rester valides.
/// Fixture partagée avec l'iPad : <c>WRITE_STOCK_DAY_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests --filter StockDaySimulationTests</c>
/// après toute modification de <c>StockDay</c>, de <c>StockConsumption</c>, de <c>RecipeExpander</c> ou de <c>StockService</c>.
/// </summary>
public class StockDaySimulationTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public StockDaySimulationTests(PosApiApplicationFactory factory) => _factory = factory;

    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "RestaurantPos.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "ios", "Packages", "PosKit", "Tests", "PosKitTests", "Fixtures", "stock_day_reference.json");
    }

    [Fact]
    public async Task TheDay_EndsWithTheOracleStock_AndValidFiscalChains()
    {
        var day = StockDay.Build();
        var json = JsonSerializer.Serialize(day, StockDay.Json) + "\n";
        if (Environment.GetEnvironmentVariable("WRITE_STOCK_DAY_FIXTURE") == "1") File.WriteAllText(FixturePath(), json);
        File.ReadAllText(FixturePath()).Should().Be(json, "générer la fixture avec WRITE_STOCK_DAY_FIXTURE=1");

        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var recipes = sp.GetRequiredService<IRecipeService>();
        var stock = sp.GetRequiredService<IStockService>();
        var checkout = sp.GetRequiredService<ICheckoutPaymentService>();
        var suffix = Guid.NewGuid().ToString("N")[..6];

        var ids = new Dictionary<string, Guid>();
        foreach (var spec in day.Ingredients)
        {
            var created = await recipes.CreateIngredientAsync(new IngredientInput($"{spec.Name} {suffix}", "g", spec.PriceMilli, spec.IsPreparation, spec.YieldMilli, 0, null, true));
            ids[spec.Name] = created.Id;
        }
        foreach (var prep in day.Ingredients.Where(i => i.IsPreparation))
            await recipes.SetPreparationRecipeAsync(ids[prep.Name], prep.Lines!.Select(l => new RecipeLineInput(ids[l.Ingredient], l.QuantityMilli)).ToList());
        var productIds = new Dictionary<string, Guid>();
        foreach (var spec in day.Products)
        {
            var product = new Product { Name = $"{spec.Name} {suffix}", CategoryId = "PLATS", Price = Money.FromCents(spec.PriceCents) };
            db.Products.Add(product);
            await db.SaveChangesAsync();
            productIds[spec.Name] = product.Id;
            if (spec.Recipe.Count > 0) await recipes.SetProductRecipeAsync(product.Id, spec.Recipe.Select(l => new RecipeLineInput(ids[l.Ingredient], l.QuantityMilli)).ToList());
        }

        var receipts = new List<Guid>();
        foreach (var op in day.Operations)
        {
            switch (op.Type)
            {
                case "receipt": await stock.ReceiveAsync(ids[op.Ingredient!], op.QuantityMilli, op.UnitCostMilliCents, null, null); break;
                case "loss": await stock.LossAsync(ids[op.Ingredient!], op.QuantityMilli, null, null); break;
                case "count": await stock.CountAsync(ids[op.Ingredient!], op.QuantityMilli, null, null); break;
                case "sale":
                    {
                        var spec = day.Products.First(p => p.Name == op.Product);
                        var order = new Order { TableNumber = "J" + receipts.Count, Status = OrderStatus.Open };
                        order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = productIds[spec.Name], ProductName = spec.Name, Quantity = op.Quantity, UnitPrice = Money.FromCents(spec.PriceCents), TaxRatePercent = 10m });
                        db.Orders.Add(order);
                        await db.SaveChangesAsync();
                        var cents = spec.PriceCents * op.Quantity;
                        (await checkout.ProcessPaymentTendersAsync(order.Id, "T01", [new PaymentTenderRequest(PaymentMethod.Cash, cents, cents)])).IsSuccess.Should().BeTrue();
                        receipts.Add(db.FiscalReceipts.Single(r => r.OrderId == order.Id).Id);
                        break;
                    }
                case "void":
                    (await checkout.VoidReceiptAsync(receipts[op.SaleIndex!.Value], "T01", Guid.NewGuid(), op.Restock)).IsSuccess.Should().BeTrue();
                    break;
            }
            await stock.RefreshAutoAvailabilityAsync();
        }

        var levels = await stock.LevelsAsync();
        var final = await db.Ingredients.AsNoTracking().ToListAsync();
        foreach (var spec in day.Ingredients.Where(i => !i.IsPreparation))
        {
            levels.GetValueOrDefault(ids[spec.Name]).Should().Be(day.Expected.StockMilli[spec.Name], $"stock final de {spec.Name}");
            final.Single(i => i.Id == ids[spec.Name]).UnitCostMilliCents.Should().Be(day.Expected.UnitCostMilli[spec.Name], $"prix moyen de {spec.Name}");
        }
        foreach (var (name, expectedCents) in day.Expected.ProductCostCents)
            (await recipes.GetProductRecipeAsync(productIds[name]))!.CostCents.Should().Be(expectedCents, $"coût de la fiche {name}");

        // Aucune chaîne fiscale n'est touchée par le stock.
        (await sp.GetRequiredService<IFiscalChainVerificationService>().VerifyAllChainsAsync()).IsValid.Should().BeTrue();
    }
}
