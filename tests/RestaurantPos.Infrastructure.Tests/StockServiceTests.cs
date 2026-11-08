using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class StockServiceTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("StockDb_" + Guid.NewGuid().ToString("N")).Options);

    private static Ingredient Raw(AppDbContext db, string name, long mc = 1000)
    {
        var i = new Ingredient { Name = name, Unit = "g", UnitCostMilliCents = mc };
        db.Ingredients.Add(i);
        return i;
    }

    private static Product Dish(AppDbContext db, string name)
    {
        var p = new Product { Name = name, CategoryId = "PLATS", Price = Money.FromCents(1100) };
        db.Products.Add(p);
        return p;
    }

    private static void Line(AppDbContext db, string kind, Guid owner, Ingredient ingredient, long qty) =>
        db.RecipeLines.Add(new RecipeLine { OwnerKind = kind, OwnerId = owner, IngredientId = ingredient.Id, QuantityMilli = qty });

    private static async Task<Order> PayAsync(AppDbContext db, Product product, int quantity, params string[] modifiers)
    {
        var order = new Order { TableNumber = "1", Status = OrderStatus.Open };
        var item = new OrderItem { OrderId = order.Id, ProductId = product.Id, ProductName = product.Name, Quantity = quantity, UnitPrice = product.Price, TaxRatePercent = 10m };
        item.SelectedModifiers.AddRange(modifiers);
        order.Items.Add(item);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var checkout = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));
        var result = await checkout.ProcessPaymentTendersAsync(order.Id, "T01", [new PaymentTenderRequest(PaymentMethod.Cash, 1100L * quantity, 1100L * quantity)]);
        result.IsSuccess.Should().BeTrue();
        return order;
    }

    [Fact]
    public async Task Sale_ConsumesRecipeIngredients_IncludingSubRecipes_ExactlyOnce()
    {
        using var db = NewDb();
        var beef = Raw(db, "Boeuf");
        var tomato = Raw(db, "Tomate");
        var sauce = new Ingredient { Name = "Sauce", Unit = "g", IsPreparation = true, YieldMilli = 500_000 };
        db.Ingredients.Add(sauce);
        var dish = Dish(db, "Steak");
        Line(db, RecipeLine.OwnerProduct, dish.Id, beef, 200_000);
        Line(db, RecipeLine.OwnerProduct, dish.Id, sauce, 100_000);              // 100 g de sauce
        Line(db, RecipeLine.OwnerIngredient, sauce.Id, tomato, 1_000_000);       // 1 kg de tomate pour 500 g de sauce
        await db.SaveChangesAsync();

        var order = await PayAsync(db, dish, 3);

        var levels = await new StockService(db).LevelsAsync();
        levels[beef.Id].Should().Be(-600_000);   // 3 × 200 g
        levels[tomato.Id].Should().Be(-600_000); // 3 × 100 g de sauce = 3 × 200 g de tomate
        // Rejouer la consommation de la même commande ne change rien.
        await StockConsumption.RecordSaleAsync(db, order, null, default);
        await db.SaveChangesAsync();
        (await new StockService(db).LevelsAsync())[beef.Id].Should().Be(-600_000);
    }

    [Fact]
    public async Task Sale_WithoutRecipe_WritesNothingAndSucceeds()
    {
        using var db = NewDb();
        var dish = Dish(db, "Eau");
        await db.SaveChangesAsync();

        await PayAsync(db, dish, 2);

        db.StockMovements.Should().BeEmpty();
    }

    [Fact]
    public async Task ModifierConsumption_AddsAndRemovesPerPortion()
    {
        using var db = NewDb();
        var cheese = Raw(db, "Fromage");
        var bun = Raw(db, "Pain");
        var dish = Dish(db, "Burger");
        Line(db, RecipeLine.OwnerProduct, dish.Id, bun, 80_000);
        var group = new ProductModifierGroup { ProductId = dish.Id, GroupName = "Extras" };
        var extra = new ProductModifierOption { GroupId = group.Id, Name = "Extra fromage" };
        var noBun = new ProductModifierOption { GroupId = group.Id, Name = "Sans pain" };
        group.Options.AddRange([extra, noBun]);
        db.ModifierGroups.Add(group);
        db.ModifierOptionConsumptions.AddRange(
            new ModifierOptionConsumption { OptionId = extra.Id, IngredientId = cheese.Id, DeltaQuantityMilli = 30_000 },
            new ModifierOptionConsumption { OptionId = noBun.Id, IngredientId = bun.Id, DeltaQuantityMilli = -80_000 });
        await db.SaveChangesAsync();

        await PayAsync(db, dish, 2, "Extra fromage");
        await PayAsync(db, dish, 1, "Sans pain");

        var levels = await new StockService(db).LevelsAsync();
        levels[cheese.Id].Should().Be(-60_000);
        levels[bun.Id].Should().Be(-160_000); // 2 pains vendus, 1 retiré sur la deuxième vente (80 g rendus)
    }

    [Fact]
    public async Task Receipt_UpdatesWeightedAveragePrice_AndNegativeStockIsTolerated()
    {
        using var db = NewDb();
        var flour = Raw(db, "Farine", mc: 1000);
        await db.SaveChangesAsync();
        var svc = new StockService(db);

        await svc.ReceiveAsync(flour.Id, 10_000_000, 1000, null, null);     // 10 kg à 1 c/g
        await svc.ReceiveAsync(flour.Id, 10_000_000, 2000, null, null);     // 10 kg à 2 c/g
        (await db.Ingredients.FindAsync(flour.Id))!.UnitCostMilliCents.Should().Be(1500);

        await svc.LossAsync(flour.Id, 25_000_000, "casse", null);           // plus que le stock : négatif toléré
        (await svc.LevelsAsync())[flour.Id].Should().Be(-5_000_000);
        var alerts = await svc.AlertsAsync();
        alerts.LowStock.Should().BeEmpty(); // pas de seuil défini
    }

    [Fact]
    public async Task Count_WritesTheVarianceValued_AndRefusesBadInput()
    {
        using var db = NewDb();
        var oil = Raw(db, "Huile", mc: 2500);
        await db.SaveChangesAsync();
        var svc = new StockService(db);
        await svc.ReceiveAsync(oil.Id, 5_000_000, 2500, null, null);

        var result = await svc.CountAsync(oil.Id, 4_200_000, "inventaire", null);

        result!.VarianceMilli.Should().Be(-800_000);
        result.ValueCents.Should().Be(-2000); // 800 g × 2,5 c/g = 20 €
        (await svc.LevelsAsync())[oil.Id].Should().Be(4_200_000);
        (await svc.CountAsync(oil.Id, 4_200_000, null, null))!.VarianceMilli.Should().Be(0);
        await FluentActions.Awaiting(() => svc.ReceiveAsync(oil.Id, 0, 100, null, null)).Should().ThrowAsync<IngredientException>();
    }

    [Fact]
    public async Task Restock_OnlyWhenAsked()
    {
        using var db = NewDb();
        var beef = Raw(db, "Boeuf");
        var dish = Dish(db, "Steak");
        Line(db, RecipeLine.OwnerProduct, dish.Id, beef, 200_000);
        await db.SaveChangesAsync();
        var order = await PayAsync(db, dish, 1);

        // Sans restitution : l'avoir ne touche pas au stock.
        var receipt = await db.FiscalReceipts.FirstAsync(r => r.OrderId == order.Id);
        var checkout = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));
        (await checkout.VoidReceiptAsync(receipt.Id, receipt.TerminalId, Guid.NewGuid())).IsSuccess.Should().BeTrue();
        (await new StockService(db).LevelsAsync())[beef.Id].Should().Be(-200_000);

        // Avec restitution (autre commande) : mouvement saleReversal.
        var order2 = await PayAsync(db, dish, 1);
        var receipt2 = await db.FiscalReceipts.FirstAsync(r => r.OrderId == order2.Id && r.VoidedReceiptId == null);
        (await checkout.VoidReceiptAsync(receipt2.Id, receipt2.TerminalId, Guid.NewGuid(), restock: true)).IsSuccess.Should().BeTrue();
        (await new StockService(db).LevelsAsync())[beef.Id].Should().Be(-200_000);
        db.StockMovements.Count(m => m.Kind == StockMovementKinds.SaleReversal).Should().Be(1);
    }

    [Fact]
    public async Task AutoUnavailable_TurnsOffWhenSoldOut_AndBackOnAtReceipt_ButNeverLiftsAManualOutage()
    {
        using var db = NewDb();
        var cream = Raw(db, "Creme");
        var dish = Dish(db, "Dessert");
        var manual = Dish(db, "Tarte");
        manual.IsAvailable = false;
        Line(db, RecipeLine.OwnerProduct, dish.Id, cream, 100_000);
        Line(db, RecipeLine.OwnerProduct, manual.Id, cream, 100_000);
        await db.SaveChangesAsync();
        var svc = new StockService(db);
        await svc.UpdateSettingsAsync(new StockSettings(true, false, null));
        await svc.ReceiveAsync(cream.Id, 100_000, 1000, null, null);

        await PayAsync(db, dish, 1);   // stock = 0
        var changes = await svc.RefreshAutoAvailabilityAsync();

        changes.Should().ContainSingle(c => c.ProductId == dish.Id && !c.IsAvailable);
        var stored = await db.Products.FindAsync(dish.Id);
        stored!.IsAvailable.Should().BeFalse();
        stored.AutoUnavailable.Should().BeTrue();

        await svc.ReceiveAsync(cream.Id, 500_000, 1000, null, null);
        (await svc.RefreshAutoAvailabilityAsync()).Should().ContainSingle(c => c.ProductId == dish.Id && c.IsAvailable);
        (await db.Products.FindAsync(manual.Id))!.IsAvailable.Should().BeFalse(); // rupture manuelle : jamais levée par le stock
    }

    [Fact]
    public async Task LowStockAlert_ListsIngredientsUnderTheirThreshold()
    {
        using var db = NewDb();
        var salt = Raw(db, "Sel");
        salt.LowStockMilli = 1_000_000;
        await db.SaveChangesAsync();
        var svc = new StockService(db);
        await svc.ReceiveAsync(salt.Id, 800_000, 100, null, null);

        var alerts = await svc.AlertsAsync();

        alerts.LowStock.Should().ContainSingle(a => a.IngredientId == salt.Id && a.StockMilli == 800_000);
    }
}
