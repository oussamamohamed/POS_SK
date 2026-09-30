using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintJobSchemaTests
{
    [Fact]
    public async Task PrintJob_RoundTripsThroughSqlite()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        var now = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        using (var db = new AppDbContext(options))
        {
            db.Database.EnsureCreated();
            db.PrintJobs.Add(new PrintJob
            {
                PrinterId = Guid.NewGuid(),
                Kind = PrintJobKind.KitchenTicket,
                DocumentJson = "{}",
                NextAttemptAtUtc = now,
                DeadlineAtUtc = now + PrintJob.Lifetime,
                CreatedAtUtc = now
            });
            await db.SaveChangesAsync();
        }
        using (var db = new AppDbContext(options))
        {
            var job = (await db.PrintJobs.Where(j => j.Status == PrintJobStatus.Pending).ToListAsync()).Single();
            job.Kind.Should().Be(PrintJobKind.KitchenTicket);
            job.DeadlineAtUtc.Should().Be(now.AddMinutes(30));
            job.Attempts.Should().Be(0);
        }
    }
}
