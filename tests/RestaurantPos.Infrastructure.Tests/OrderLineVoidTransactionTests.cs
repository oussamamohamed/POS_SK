using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

/// <summary>
/// Annulation de ligne sur un vrai SQLite (transaction réelle, impossible à observer avec InMemory) : un échec d'écriture du JET
/// annule tout (ligne, trace, article de bon) ; un succès écrit les quatre ensemble.
/// </summary>
public sealed class OrderLineVoidTransactionTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Guid _managerId = Guid.NewGuid();

    public OrderLineVoidTransactionTests() => _connection.Open();

    public void Dispose() => _connection.Dispose();

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    private async Task<(Guid OrderId, Guid ItemId, Guid TicketItemId)> SeedAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new User { Id = _managerId, Name = "Alexandre", Role = UserRole.FloorManager, PinHash = "x", PinSalt = "x" });
        var order = new Order { TableNumber = "12", Status = OrderStatus.SentToKitchen };
        var item = new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Entrecôte", Quantity = 2, UnitPrice = Money.FromCents(1500), TaxRatePercent = 10, IsDispatched = true };
        order.Items.Add(item);
        db.Orders.Add(order);
        var ticket = new KitchenTicket { OrderId = order.Id, TableNumber = "12", StationId = "HOT_KITCHEN" };
        var ti = new KitchenTicketItem { TicketId = ticket.Id, ProductId = item.ProductId, ProductName = item.ProductName, Quantity = 2, OrderItemId = item.Id };
        ticket.Items.Add(ti);
        db.KitchenTickets.Add(ticket);
        await db.SaveChangesAsync();
        return (order.Id, item.Id, ti.Id);
    }

    private sealed class FailingJournal : IFiscalJournal
    {
        public Task<TransactionJournalEntry> AppendAsync(string eventType, string payloadJson, string? terminalId = null, Guid? operatorId = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("JET indisponible");

        public Task<TransactionJournalEntry> AppendAsync<T>(string eventType, T payload, string? terminalId = null, Guid? operatorId = null, CancellationToken cancellationToken = default) where T : class =>
            throw new InvalidOperationException("JET indisponible");
    }

    [Fact]
    public async Task JournalFailure_RollsBackLineVoidRowAndTicketItem()
    {
        var (orderId, itemId, ticketItemId) = await SeedAsync();

        await using (var db = NewContext())
        {
            var act = () => new OrderLineVoidService(db, new FailingJournal()).VoidAsync(orderId, itemId, 1, "erreur", _managerId);
            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("JET indisponible");
        }

        await using var check = NewContext();
        (await check.OrderItems.AsNoTracking().SingleAsync(i => i.Id == itemId)).Quantity.Should().Be(2);
        (await check.OrderLineVoids.AsNoTracking().CountAsync()).Should().Be(0);
        var ti = await check.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.Id == ticketItemId);
        ti.CancelledQuantity.Should().Be(0);
        ti.Status.Should().Be(TicketItemStatus.Pending);
        (await check.JournalEntries.AsNoTracking().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Success_OnSqlite_WritesLineVoidRowTicketItemAndJetTogether()
    {
        var (orderId, itemId, ticketItemId) = await SeedAsync();

        await using (var db = NewContext())
        {
            var result = await new OrderLineVoidService(db, new FiscalJournalService(db)).VoidAsync(orderId, itemId, 2, "  client parti  ", _managerId);
            result.Outcome.Should().Be(OrderLineVoidOutcome.Voided);
        }

        await using var check = NewContext();
        (await check.OrderItems.AsNoTracking().AnyAsync(i => i.Id == itemId)).Should().BeFalse();
        (await check.OrderLineVoids.AsNoTracking().SingleAsync()).Reason.Should().Be("client parti");
        (await check.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.Id == ticketItemId)).Status.Should().Be(TicketItemStatus.Cancelled);
        (await check.JournalEntries.AsNoTracking().CountAsync(j => j.EventType == JournalEventTypes.OrderLineVoided)).Should().Be(1);
    }

    [Fact]
    public async Task SameIdempotencyKey_ConcurrentRequestsOnSqliteFile_ApplyOnce()
    {
        // Fichier (et non :memory:) : deux connexions, donc deux vraies transactions Serializable (BEGIN IMMEDIATE) en concurrence.
        var path = Path.Combine(Path.GetTempPath(), $"linevoid-{Guid.NewGuid():N}.db");
        DbContextOptions<AppDbContext> Options() => new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
        try
        {
            Guid orderId, itemId;
            await using (var db = new AppDbContext(Options()))
            {
                await db.Database.EnsureCreatedAsync();
                db.Users.Add(new User { Id = _managerId, Name = "Alexandre", Role = UserRole.FloorManager, PinHash = "x", PinSalt = "x" });
                var order = new Order { TableNumber = "12", Status = OrderStatus.SentToKitchen };
                var item = new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Entrecôte", Quantity = 3, UnitPrice = Money.FromCents(1500), TaxRatePercent = 10, IsDispatched = true };
                order.Items.Add(item);
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                (orderId, itemId) = (order.Id, item.Id);
            }

            var key = Guid.NewGuid();
            async Task<OrderLineVoidResult> VoidOnce()
            {
                await using var db = new AppDbContext(Options());
                return await new OrderLineVoidService(db, new FiscalJournalService(db)).VoidAsync(orderId, itemId, 1, "erreur", _managerId, key);
            }
            var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(VoidOnce)));

            results.Should().OnlyContain(r => r.Outcome == OrderLineVoidOutcome.Voided && r.TotalTtcCents == 3000);
            results.Count(r => !r.Replayed).Should().Be(1);
            results.Select(r => r.VoidId).Distinct().Should().ContainSingle();
            await using var check = new AppDbContext(Options());
            (await check.OrderItems.AsNoTracking().SingleAsync(i => i.Id == itemId)).Quantity.Should().Be(2);
            (await check.OrderLineVoids.AsNoTracking().CountAsync()).Should().Be(1);
            (await check.JournalEntries.AsNoTracking().CountAsync(j => j.EventType == JournalEventTypes.OrderLineVoided)).Should().Be(1);
            (await check.OrderLineVoidRequests.AsNoTracking().SingleAsync()).IdempotencyKey.Should().Be(key);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Schema_ReasonIsNotNull()
    {
        await SeedAsync();
        await using var db = NewContext();
        var insert = () => db.Database.ExecuteSqlRawAsync(
            "INSERT INTO OrderLineVoids (Id, OrderId, OrderItemId, ProductId, ProductName, Quantity, UnitPrice, Reason, OperatorId, OperatorName, ServerName, TableNumber, VoidedAtUtc) " +
            "VALUES ('a', 'b', 'c', 'd', 'P', 1, 100, NULL, 'e', 'O', '', '1', '2026-01-01')");
        (await insert.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("NOT NULL");
    }
}
