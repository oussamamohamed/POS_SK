using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

/// <summary>Annulation d'une ligne envoyée (manager, motif, JET, bon marqué) et correction d'une ligne non envoyée (tout opérateur, sans trace).</summary>
public class OrderLineVoidTests : IClassFixture<PosApiApplicationFactory>
{
    private const string Terminal = "T01";
    private readonly PosApiApplicationFactory _factory;
    private readonly Guid _managerId = Guid.NewGuid();
    private readonly Guid _waiterId = Guid.NewGuid();

    public OrderLineVoidTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Users.Add(new User { Id = _managerId, Name = "Alexandre", Role = UserRole.FloorManager, PinHash = "x", PinSalt = "x" });
        db.Users.Add(new User { Id = _waiterId, Name = "Sophie", Role = UserRole.Waiter, PinHash = "x", PinSalt = "x" });
        db.SaveChanges();
    }

    private HttpClient Client(Guid operatorId, string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        client.DefaultRequestHeaders.Add("X-Test-Operator-Id", operatorId.ToString());
        return client;
    }

    private HttpClient Manager() => Client(_managerId, "FloorManager");
    private HttpClient Waiter() => Client(_waiterId, "Waiter");

    private sealed record Seeded(Guid OrderId, Guid ItemId, Guid? TicketItemId, string Table);

    /// <summary>Commande d'une ligne (1 500 c l'unité) sur une table servie par « Sophie », avec son article de bon si la ligne est envoyée.</summary>
    private async Task<Seeded> SeedAsync(int quantity = 1, bool dispatched = true, bool linkTicketItem = true, int extraLines = 0)
    {
        var table = "LV" + Random.Shared.Next(10000, 99999);
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DiningTables.Add(new DiningTable { TableNumber = table, ActiveOrderId = orderId, Status = TableStatus.Occupied, AssignedWaiterName = "Sophie" });
        var order = new Order { Id = orderId, TableNumber = table, Status = dispatched ? OrderStatus.SentToKitchen : OrderStatus.Open };
        var item = new OrderItem
        {
            OrderId = orderId,
            ProductId = Guid.NewGuid(),
            ProductName = "Entrecôte",
            Quantity = quantity,
            UnitPrice = Money.FromCents(1500),
            TaxRatePercent = 10,
            PreparationStationId = "HOT_KITCHEN",
            SelectedModifiers = ["saignant"],
            IsDispatched = dispatched
        };
        order.Items.Add(item);
        for (int i = 0; i < extraLines; i++)
        {
            order.Items.Add(new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Café", Quantity = 1, UnitPrice = Money.FromCents(200), TaxRatePercent = 10, IsDispatched = dispatched });
        }
        db.Orders.Add(order);
        Guid? ticketItemId = null;
        if (dispatched)
        {
            var ticket = new KitchenTicket { OrderId = orderId, TableNumber = table, StationId = "HOT_KITCHEN" };
            var ticketItem = new KitchenTicketItem
            {
                TicketId = ticket.Id,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = quantity,
                OrderItemId = linkTicketItem ? item.Id : null
            };
            ticket.Items.Add(ticketItem);
            db.KitchenTickets.Add(ticket);
            ticketItemId = ticketItem.Id;
        }
        await db.SaveChangesAsync();
        return new Seeded(orderId, item.Id, ticketItemId, table);
    }

    private static Task<HttpResponseMessage> VoidAsync(HttpClient client, Seeded s, int quantity, string? reason) =>
        client.PostAsJsonAsync($"/api/orders/{s.OrderId}/items/{s.ItemId}/void", new { quantity, reason });

    private async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<Order> OrderAsync(Guid id) => ReadAsync(db => db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == id));
    private Task<List<OrderLineVoid>> VoidsAsync(Guid orderId) => ReadAsync(db => db.OrderLineVoids.AsNoTracking().Where(v => v.OrderId == orderId).ToListAsync());
    private Task<List<TransactionJournalEntry>> JetAsync(Guid orderId) => ReadAsync(async db =>
        (await db.JournalEntries.AsNoTracking().Where(j => j.EventType == JournalEventTypes.OrderLineVoided).ToListAsync())
            .Where(j => j.PayloadJson.Contains(orderId.ToString(), StringComparison.Ordinal)).ToList());

    [Fact]
    public async Task Void_SentLine_ByManager_RemovesLineWritesVoidAndJet()
    {
        var s = await SeedAsync();

        var resp = await VoidAsync(Manager(), s, 1, "erreur de saisie");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderAsync(s.OrderId)).Items.Should().BeEmpty();
        var v = (await VoidsAsync(s.OrderId)).Should().ContainSingle().Subject;
        v.OrderItemId.Should().Be(s.ItemId);
        v.ProductName.Should().Be("Entrecôte");
        v.Quantity.Should().Be(1);
        v.UnitPrice.AmountInCents.Should().Be(1500);
        v.ModifiersSummary.Should().Be("saignant");
        v.PreparationStationId.Should().Be("HOT_KITCHEN");
        v.Reason.Should().Be("erreur de saisie");
        v.OperatorId.Should().Be(_managerId);
        v.OperatorName.Should().Be("Alexandre");
        v.ServerName.Should().Be("Sophie");
        v.TableNumber.Should().Be(s.Table);
        v.TicketId.Should().NotBeNull();

        var jet = (await JetAsync(s.OrderId)).Should().ContainSingle().Subject;
        jet.OperatorId.Should().Be(_managerId);
        using var payload = JsonDocument.Parse(jet.PayloadJson);
        payload.RootElement.GetProperty("reason").GetString().Should().Be("erreur de saisie");
        payload.RootElement.GetProperty("quantity").GetInt32().Should().Be(1);
        payload.RootElement.GetProperty("unitPriceCents").GetInt64().Should().Be(1500);
        payload.RootElement.GetProperty("tableNumber").GetString().Should().Be(s.Table);
        payload.RootElement.GetProperty("operatorName").GetString().Should().Be("Alexandre");
        payload.RootElement.GetProperty("ticketId").GetGuid().Should().Be(v.TicketId!.Value);

        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("voidId").GetGuid().Should().Be(v.Id);
    }

    [Fact]
    public async Task Void_SentLine_QueuesCancellationSlipOnStationPrinter()
    {
        Guid printerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var printer = new PrinterConfiguration { Name = "Chaud-LV", IpAddress = "10.0.0.9", AssignedStationIds = ["HOT_KITCHEN"] };
            db.PrinterConfigurations.Add(printer);
            await db.SaveChangesAsync();
            printerId = printer.Id;
        }
        var s = await SeedAsync();

        (await VoidAsync(Manager(), s, 1, "erreur de saisie")).StatusCode.Should().Be(HttpStatusCode.OK);

        var jobs = await ReadAsync(db => db.PrintJobs.AsNoTracking().Where(j => j.PrinterId == printerId).ToListAsync());
        var job = jobs.Should().ContainSingle().Subject;
        job.Kind.Should().Be(RestaurantPos.Domain.Entities.PrintJobKind.KitchenTicket);
        job.OpenCashDrawer.Should().BeFalse();
        var texts = RestaurantPos.Infrastructure.Printing.TicketDocumentJson.Deserialize(job.DocumentJson).Lines.OfType<RestaurantPos.Infrastructure.Printing.TicketText>().Select(t => t.Text).ToList();
        texts.Should().Contain("-1  Entrecôte").And.Contain("Motif : erreur de saisie");
    }

    [Fact]
    public async Task Void_SentLine_HubFailure_StillReturnsOk()
    {
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(sv =>
            sv.AddSingleton<Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.KitchenHub>, ThrowingHub>()));
        var s = await SeedAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");
        client.DefaultRequestHeaders.Add("X-Test-Operator-Id", _managerId.ToString());

        (await VoidAsync(client, s, 1, "erreur de saisie")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await VoidsAsync(s.OrderId)).Should().ContainSingle();
    }

    private sealed class ThrowingHub : Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.KitchenHub>
    {
        public Microsoft.AspNetCore.SignalR.IHubClients Clients => throw new InvalidOperationException("hub down");
        public Microsoft.AspNetCore.SignalR.IGroupManager Groups => throw new InvalidOperationException("hub down");
    }

    [Fact]
    public async Task Void_SentLine_Partial_KeepsRemainder()
    {
        var s = await SeedAsync(quantity: 3);

        (await VoidAsync(Manager(), s, 1, "trop commandé")).StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await OrderAsync(s.OrderId);
        order.Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        order.TotalTtc.AmountInCents.Should().Be(3000);
        (await VoidsAsync(s.OrderId)).Should().ContainSingle().Which.Quantity.Should().Be(1);
    }

    [Fact]
    public async Task Void_SentLine_ByWaiter_Returns403()
    {
        var s = await SeedAsync();

        (await VoidAsync(Waiter(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle();
        (await VoidsAsync(s.OrderId)).Should().BeEmpty();
        (await JetAsync(s.OrderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Void_SentLine_WithoutReason_Returns400()
    {
        var s = await SeedAsync();

        (await VoidAsync(Manager(), s, 1, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await VoidAsync(Manager(), s, 1, "   ")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle();
        (await VoidsAsync(s.OrderId)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task Void_QuantityZeroOrAboveLine_Returns400AndChangesNothing(int quantity)
    {
        var s = await SeedAsync(quantity: 2);

        (await VoidAsync(Manager(), s, quantity, "erreur")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        (await VoidsAsync(s.OrderId)).Should().BeEmpty();
        (await JetAsync(s.OrderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Void_OrderWithReceipt_Returns409OrderHasReceipts()
    {
        var s = await SeedAsync(quantity: 2);
        string receiptNumber;
        using (var scope = _factory.Services.CreateScope())
        {
            var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
            var paid = await checkout.ProcessPaymentTendersAsync(s.OrderId, Terminal, [new PaymentTenderRequest(PaymentMethod.Cash, 1000, 1000)]);
            paid.IsSuccess.Should().BeTrue();
            receiptNumber = paid.ReceiptNumber;
        }

        var resp = await VoidAsync(Manager(), s, 1, "erreur");

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("order_has_receipts");
        body.RootElement.GetProperty("message").GetString().Should().Contain(receiptNumber);
        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle().Which.Quantity.Should().Be(2);
        (await VoidsAsync(s.OrderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Void_OrderNoLongerSettleable_Returns409()
    {
        var s = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.Orders.SingleAsync(o => o.Id == s.OrderId)).Status = OrderStatus.Cancelled;
            await db.SaveChangesAsync();
        }

        (await VoidAsync(Manager(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Void_UnknownLine_Returns404()
    {
        var s = await SeedAsync();

        var resp = await Manager().PostAsJsonAsync($"/api/orders/{s.OrderId}/items/{Guid.NewGuid()}/void", new { quantity = 1, reason = "x" });

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Void_UnsentSavedLine_ByWaiter_RemovesWithoutJetOrVoidRow()
    {
        var s = await SeedAsync(quantity: 2, dispatched: false);

        (await VoidAsync(Waiter(), s, 1, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderAsync(s.OrderId)).Items.Should().ContainSingle().Which.Quantity.Should().Be(1);

        (await VoidAsync(Waiter(), s, 1, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await OrderAsync(s.OrderId)).Items.Should().BeEmpty();

        (await VoidsAsync(s.OrderId)).Should().BeEmpty();
        (await JetAsync(s.OrderId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Void_MarksKitchenTicketItem()
    {
        var s = await SeedAsync(quantity: 2);
        Task<KitchenTicketItem> TicketItemAsync() => ReadAsync(db => db.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.Id == s.TicketItemId));

        (await VoidAsync(Manager(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);
        var partial = await TicketItemAsync();
        partial.CancelledQuantity.Should().Be(1);
        partial.Status.Should().Be(TicketItemStatus.Pending);

        (await VoidAsync(Manager(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);
        var full = await TicketItemAsync();
        full.CancelledQuantity.Should().Be(2);
        full.Status.Should().Be(TicketItemStatus.Cancelled);

        // L'écran cuisine lit les entités : l'état annulé et la quantité y figurent.
        var kds = await Manager().GetStringAsync("/api/kds/tickets");
        kds.Should().Contain("\"cancelledQuantity\":2");
    }

    [Fact]
    public async Task Void_LegacyLineWithoutOrderItemId_StillWritesJet()
    {
        var s = await SeedAsync(linkTicketItem: false);

        (await VoidAsync(Manager(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await JetAsync(s.OrderId)).Should().ContainSingle();
        (await VoidsAsync(s.OrderId)).Should().ContainSingle().Which.TicketId.Should().BeNull();
        var ticketItem = await ReadAsync(db => db.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.Id == s.TicketItemId));
        ticketItem.CancelledQuantity.Should().Be(0);
        ticketItem.Status.Should().Be(TicketItemStatus.Pending);
    }

    [Fact]
    public async Task Void_LastLine_LeavesEmptyOrderWithZeroTotal()
    {
        var s = await SeedAsync(quantity: 2);

        (await VoidAsync(Manager(), s, 2, "client parti")).StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await OrderAsync(s.OrderId);
        order.Items.Should().BeEmpty();
        order.TotalTtc.AmountInCents.Should().Be(0);
        PaymentSettlementRules.IsSettleable(order.Status).Should().BeTrue();
    }

    [Fact]
    public async Task Void_SentLine_ByAdmin_Succeeds_AndByKitchenStaff_Returns403()
    {
        var adminId = Guid.NewGuid();
        var kitchenId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = adminId, Name = "Administrateur", Role = UserRole.Admin, PinHash = "x", PinSalt = "x" });
            db.Users.Add(new User { Id = kitchenId, Name = "Cuisinier", Role = UserRole.KitchenStaff, PinHash = "x", PinSalt = "x" });
            await db.SaveChangesAsync();
        }
        var s = await SeedAsync(quantity: 2);

        (await VoidAsync(Client(kitchenId, "KitchenStaff"), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await VoidAsync(Client(adminId, "Admin"), s, 1, "erreur admin")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await VoidsAsync(s.OrderId)).Should().ContainSingle().Which.OperatorName.Should().Be("Administrateur");
    }

    [Fact]
    public async Task Void_ConcurrentDispatchAndCorrection_ReadsIsDispatchedInTransaction()
    {
        // Approximation séquentielle (InMemory, sans transaction réelle) : la ligne part en cuisine entre l'affichage
        // (non envoyée) et la demande de correction ; le service relit IsDispatched et applique les règles d'annulation.
        var s = await SeedAsync(dispatched: false, extraLines: 1);
        using (var scope = _factory.Services.CreateScope())
        {
            // Même enchaînement que la route d'envoi : bons (avec OrderItemId), puis lignes marquées envoyées.
            await scope.ServiceProvider.GetRequiredService<IKitchenRoutingService>().SplitAndRouteOrderAsync(s.OrderId);
            (await scope.ServiceProvider.GetRequiredService<ITableManagementService>().DispatchOrderLinesAsync(s.Table)).Should().BeTrue();
        }

        (await VoidAsync(Waiter(), s, 1, null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await VoidAsync(Manager(), s, 1, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await OrderAsync(s.OrderId)).Items.Should().HaveCount(2);

        (await VoidAsync(Manager(), s, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JetAsync(s.OrderId)).Should().ContainSingle();
        (await VoidsAsync(s.OrderId)).Should().ContainSingle().Which.TicketId.Should().NotBeNull();
        var routed = await ReadAsync(db => db.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.OrderItemId == s.ItemId));
        routed.Status.Should().Be(TicketItemStatus.Cancelled);
    }
}
