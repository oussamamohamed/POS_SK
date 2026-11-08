using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class CashAppendOnlyTests
{
    [Fact]
    public async Task Update_Delete_AndASecondOpenSession_AreRefusedBySql()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        foreach (var statement in CashSchema.Statements) await db.Database.ExecuteSqlRawAsync(statement);

        var session = new CashSession { TerminalId = "T01", OpeningFloatCents = 100 };
        db.CashSessions.Add(session);
        await db.SaveChangesAsync();
        db.CashMovements.Add(new CashMovement { SessionId = session.Id, Kind = "out", AmountCents = 10, Reason = "Achat de glace" });
        db.CashCounts.Add(new CashCount { SessionId = session.Id, CountedCents = 90, ExpectedCents = 90 });
        await db.SaveChangesAsync();

        foreach (var sql in new[] { "UPDATE CashSessions SET Id = Id", "DELETE FROM CashSessions", "UPDATE CashMovements SET Id = Id", "DELETE FROM CashMovements", "UPDATE CashCounts SET Id = Id", "DELETE FROM CashCounts" })
        {
            var statement = () => db.Database.ExecuteSqlRawAsync(sql);
            (await statement.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("cash_append_only");
        }

        // Une session déjà comptée libère le poste ; un deuxième comptage de la même session est refusé par l'index unique.
        db.CashCounts.Add(new CashCount { SessionId = session.Id, CountedCents = 1, ExpectedCents = 1 });
        await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        db.ChangeTracker.Clear();

        db.CashSessions.Add(new CashSession { TerminalId = "T01" });
        await db.SaveChangesAsync(); // la précédente est comptée : une nouvelle s'ouvre
        db.CashSessions.Add(new CashSession { TerminalId = "T01" });
        var second = await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<Exception>();
        second.Which.ToString().Should().Contain("cash_session_already_open");
    }
}
