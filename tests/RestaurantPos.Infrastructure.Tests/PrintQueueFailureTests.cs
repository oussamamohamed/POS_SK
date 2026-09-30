using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintQueueFailureTests
{
    private sealed class FailOnPrintJobInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; } = true;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<PrintJob>().Any(e => e.State == EntityState.Added))
                throw new InvalidOperationException("database is locked");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task FailedEnqueue_DoesNotPoisonLaterBusinessSave()
    {
        var interceptor = new FailOnPrintJobInterceptor();
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("QueueFail_" + Guid.NewGuid().ToString("N")).AddInterceptors(interceptor).Options);
        var queue = new PrintQueue(db, new PrintSignal(), TimeProvider.System);

        var act = () => queue.EnqueueAsync(Guid.NewGuid(), PrintJobKind.KitchenTicket, new TicketDocument("fr", false, []), false);
        await act.Should().ThrowAsync<InvalidOperationException>();

        db.Orders.Add(new Order { TableNumber = "T05" });
        await db.SaveChangesAsync();   // ne doit pas relancer l'insertion du PrintJob en échec

        db.Orders.Count().Should().Be(1);
        db.PrintJobs.Should().BeEmpty();
    }
}
