using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class StockAppendOnlyTests
{
    [Fact]
    public async Task Update_And_Delete_OnStockMovements_AreRefused_AndSaleIsUnique()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var statement in StockSchema.Statements) await db.Database.ExecuteSqlRawAsync(statement);

        var ingredient = new Ingredient { Name = "Sel", Unit = "g" };
        var order = Guid.NewGuid();
        db.Ingredients.Add(ingredient);
        db.StockMovements.Add(new StockMovement { IngredientId = ingredient.Id, Kind = StockMovementKinds.Sale, QuantityMilli = -1000, OrderId = order });
        await db.SaveChangesAsync();

        var update = () => db.Database.ExecuteSqlRawAsync("UPDATE StockMovements SET QuantityMilli = 0");
        (await update.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("stock_movements_append_only");
        var delete = () => db.Database.ExecuteSqlRawAsync("DELETE FROM StockMovements");
        (await delete.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("stock_movements_append_only");

        db.StockMovements.Add(new StockMovement { IngredientId = ingredient.Id, Kind = StockMovementKinds.Sale, QuantityMilli = -1000, OrderId = order });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
    }
}
