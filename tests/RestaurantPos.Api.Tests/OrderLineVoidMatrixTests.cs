using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

/// <summary>
/// Matrice de cas limites de l'annulation / correction de ligne (route réelle, base InMemory) : rôle × état de ligne, quantités, motifs,
/// états de commande, reçus, particularités de ligne, enchaînements, impression, messages localisés. Chaque cas vérifie le statut HTTP
/// ET l'état : lignes et total, ligne <c>OrderLineVoids</c>, JET, article de bon, bons d'annulation en file ; aucun effet sur un refus.
/// </summary>
public class OrderLineVoidMatrixTests : IClassFixture<PosApiApplicationFactory>
{
    private const string Terminal = "T01";
    private readonly PosApiApplicationFactory _factory;
    private readonly Dictionary<string, Guid> _users = [];

    public OrderLineVoidMatrixTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        foreach (var (key, role) in new[] { ("waiter", UserRole.Waiter), ("cashier", UserRole.Cashier), ("kitchen", UserRole.KitchenStaff), ("manager", UserRole.FloorManager), ("admin", UserRole.Admin) })
        {
            var user = new User { Name = "Op " + key, Role = role, PinHash = "x", PinSalt = "x" };
            db.Users.Add(user);
            _users[key] = user.Id;
        }
        db.SaveChanges();
    }

    // ---------- Semis et lectures ----------

    private sealed record Line(Guid OrderId, Guid ItemId, Guid? TicketItemId, string Table, string Product, string Station, Guid PrinterId);

    private sealed record LineSpec
    {
        public int Quantity { get; init; } = 3;
        public bool Dispatched { get; init; } = true;
        public bool LinkTicketItem { get; init; } = true;
        public long UnitCents { get; init; } = 1500;
        public long ExtraCents { get; init; }
        public string[] Modifiers { get; init; } = [];
        public string? Comment { get; init; }
        public bool Comp { get; init; }
        public OrderStatus Status { get; init; } = OrderStatus.SentToKitchen;
        public DiscountType? Discount { get; init; }
        public decimal DiscountValue { get; init; }
        public OrderDestination Destination { get; init; } = OrderDestination.EatIn;
        public string? Table { get; init; }
        public bool WithTable { get; init; } = true;
        public string? Waiter { get; init; } = "Sophie";
        public long? OtherLineCents { get; init; }
        public bool ItemStation { get; init; } = true;
    }

    /// <summary>Commande d'une ligne « Plat-xxxxx » sur une station propre au test et son imprimante active (bons comptés par imprimante).</summary>
    private async Task<Line> SeedAsync(LineSpec? spec = null)
    {
        spec ??= new LineSpec();
        var token = Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var table = spec.Table ?? "MX" + token;
        var station = "ST_" + token;
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var printer = new PrinterConfiguration { Name = "P-" + token, IpAddress = "10.0.0.1", AssignedStationIds = [station] };
        db.PrinterConfigurations.Add(printer);
        if (spec.WithTable)
            db.DiningTables.Add(new DiningTable { TableNumber = table, ActiveOrderId = orderId, Status = TableStatus.Occupied, AssignedWaiterName = spec.Waiter });
        var order = new Order
        {
            Id = orderId, TableNumber = table, Status = spec.Status, Destination = spec.Destination,
            GlobalDiscountType = spec.Discount, GlobalDiscountValue = spec.DiscountValue,
        };
        var item = new OrderItem
        {
            OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat-" + token, Quantity = spec.Quantity,
            UnitPrice = Money.FromCents(spec.UnitCents), ModifiersPriceExtra = Money.FromCents(spec.ExtraCents), TaxRatePercent = 10,
            PreparationStationId = spec.ItemStation ? station : null, SelectedModifiers = [.. spec.Modifiers], KitchenComment = spec.Comment,
            IsDispatched = spec.Dispatched, IsComp = spec.Comp,
        };
        order.Items.Add(item);
        if (spec.OtherLineCents is { } other)
            order.Items.Add(new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Autre-" + token, Quantity = 1, UnitPrice = Money.FromCents(other), TaxRatePercent = 10, IsDispatched = spec.Dispatched });
        db.Orders.Add(order);
        Guid? ticketItemId = null;
        if (spec.Dispatched)
        {
            var ticket = new KitchenTicket { OrderId = orderId, TableNumber = table, StationId = station };
            var ti = new KitchenTicketItem { TicketId = ticket.Id, ProductId = item.ProductId, ProductName = item.ProductName, Quantity = spec.Quantity, OrderItemId = spec.LinkTicketItem ? item.Id : null };
            ticket.Items.Add(ti);
            db.KitchenTickets.Add(ticket);
            ticketItemId = ti.Id;
        }
        await db.SaveChangesAsync();
        return new Line(orderId, item.Id, ticketItemId, table, item.ProductName, station, printer.Id);
    }

    private HttpClient Client(string user, string? role = null, string? language = null)
    {
        var client = _factory.CreateClient();
        if (user != "anonymous")
        {
            client.DefaultRequestHeaders.Add("X-Test-Role", role ?? RoleOf(user));
            client.DefaultRequestHeaders.Add("X-Test-Operator-Id", (_users.TryGetValue(user, out var id) ? id : Guid.NewGuid()).ToString());
        }
        if (language is not null) client.DefaultRequestHeaders.Add("Accept-Language", language);
        return client;
    }

    private static string RoleOf(string user) => user switch
    {
        "waiter" => "Waiter",
        "cashier" => "Cashier",
        "kitchen" => "KitchenStaff",
        "admin" => "Admin",
        _ => "FloorManager", // manager, unknown
    };

    private static Task<HttpResponseMessage> PostVoidAsync(HttpClient client, Guid orderId, Guid itemId, int quantity, string? reason) =>
        client.PostAsJsonAsync($"/api/orders/{orderId}/items/{itemId}/void", new { quantity, reason });

    private static Task<HttpResponseMessage> PostVoidAsync(HttpClient client, Line l, int quantity, string? reason) => PostVoidAsync(client, l.OrderId, l.ItemId, quantity, reason);

    private async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Photographie de tout ce qu'une annulation peut toucher.</summary>
    private sealed record State(int? LineQuantity, int LineCount, long TotalCents, int Voids, int Jet, int? TicketCancelled, TicketItemStatus? TicketStatus, int PrintJobs);

    private Task<State> StateAsync(Line l) => ReadAsync(async db =>
    {
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleAsync(o => o.Id == l.OrderId);
        var ti = l.TicketItemId is { } t ? await db.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.Id == t) : null;
        var jet = (await db.JournalEntries.AsNoTracking().Where(j => j.EventType == JournalEventTypes.OrderLineVoided).ToListAsync())
            .Count(j => j.PayloadJson.Contains(l.OrderId.ToString(), StringComparison.Ordinal));
        return new State(
            order.Items.SingleOrDefault(i => i.Id == l.ItemId)?.Quantity, order.Items.Count, order.TotalTtc.AmountInCents,
            await db.OrderLineVoids.AsNoTracking().CountAsync(v => v.OrderId == l.OrderId), jet,
            ti?.CancelledQuantity, ti?.Status, await db.PrintJobs.AsNoTracking().CountAsync(j => j.PrinterId == l.PrinterId));
    });

    private Task<List<OrderLineVoid>> VoidRowsAsync(Line l) => ReadAsync(db => db.OrderLineVoids.AsNoTracking().Where(v => v.OrderId == l.OrderId).ToListAsync());

    private Task<JsonElement> LastJetAsync(Line l) => ReadAsync(async db =>
    {
        var entry = (await db.JournalEntries.AsNoTracking().Where(j => j.EventType == JournalEventTypes.OrderLineVoided).ToListAsync())
            .Where(j => j.PayloadJson.Contains(l.OrderId.ToString(), StringComparison.Ordinal)).OrderBy(j => j.LocalSequence).Last();
        return JsonDocument.Parse(entry.PayloadJson).RootElement.Clone();
    });

    private static HttpStatusCode StatusOf(OrderLineVoidOutcome outcome) => outcome switch
    {
        OrderLineVoidOutcome.Voided or OrderLineVoidOutcome.Corrected => HttpStatusCode.OK,
        OrderLineVoidOutcome.NotFound => HttpStatusCode.NotFound,
        OrderLineVoidOutcome.Forbidden => HttpStatusCode.Forbidden,
        OrderLineVoidOutcome.OrderHasReceipts or OrderLineVoidOutcome.NotModifiable => HttpStatusCode.Conflict,
        _ => HttpStatusCode.BadRequest,
    };

    /// <summary>Appelle la route et vérifie l'effet attendu : rien sur un refus ; retrait, trace, JET, bon et bon d'annulation sur une annulation ; retrait seul sur une correction.</summary>
    private async Task<HttpResponseMessage> AssertVoidAsync(Line l, HttpClient client, int quantity, string? reason, OrderLineVoidOutcome expected)
    {
        var before = await StateAsync(l);
        var resp = await PostVoidAsync(client, l, quantity, reason);
        resp.StatusCode.Should().Be(StatusOf(expected), await resp.Content.ReadAsStringAsync());
        var after = await StateAsync(l);
        switch (expected)
        {
            case OrderLineVoidOutcome.Voided:
                after.Voids.Should().Be(before.Voids + 1);
                after.Jet.Should().Be(before.Jet + 1);
                after.PrintJobs.Should().Be(before.PrintJobs + 1, "un bon d'annulation par annulation");
                after.LineQuantity.Should().Be(before.LineQuantity - quantity == 0 ? null : before.LineQuantity - quantity);
                if (before.TicketCancelled is { } c && l.TicketItemId is not null && after.TicketCancelled != c)
                    after.TicketCancelled.Should().Be(c + quantity);
                break;
            case OrderLineVoidOutcome.Corrected:
                after.Should().Be(before with { LineQuantity = before.LineQuantity - quantity == 0 ? null : before.LineQuantity - quantity, LineCount = after.LineCount, TotalCents = after.TotalCents });
                after.LineCount.Should().Be(before.LineQuantity == quantity ? before.LineCount - 1 : before.LineCount);
                break;
            default:
                after.Should().Be(before, "un refus ne change rien");
                break;
        }
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("success").GetBoolean().Should().Be(StatusOf(expected) == HttpStatusCode.OK);
        if (StatusOf(expected) == HttpStatusCode.OK)
        {
            body.RootElement.GetProperty("totalTtc").GetDecimal().Should().Be(after.TotalCents / 100m);
            var voidId = body.RootElement.GetProperty("voidId");
            if (expected == OrderLineVoidOutcome.Corrected) voidId.ValueKind.Should().Be(JsonValueKind.Null);
            else (await VoidRowsAsync(l)).Select(v => v.Id).Should().Contain(voidId.GetGuid());
        }
        else
        {
            body.RootElement.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace();
        }
        return resp;
    }

    // ---------- Rôle × état de la ligne ----------

    [Theory]
    [InlineData("waiter", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("cashier", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("kitchen", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("manager", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("admin", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("unknown", false, OrderLineVoidOutcome.Corrected)]
    [InlineData("waiter", true, OrderLineVoidOutcome.Forbidden)]
    [InlineData("cashier", true, OrderLineVoidOutcome.Forbidden)]
    [InlineData("kitchen", true, OrderLineVoidOutcome.Forbidden)]
    [InlineData("manager", true, OrderLineVoidOutcome.Voided)]
    [InlineData("admin", true, OrderLineVoidOutcome.Voided)]
    [InlineData("unknown", true, OrderLineVoidOutcome.Forbidden)]
    public async Task Role_x_LineState(string user, bool dispatched, OrderLineVoidOutcome expected)
    {
        var l = await SeedAsync(new LineSpec { Dispatched = dispatched, Status = dispatched ? OrderStatus.SentToKitchen : OrderStatus.Open });
        await AssertVoidAsync(l, Client(user), 1, "erreur", expected);
    }

    [Fact]
    public async Task Role_IsReadFromTheDatabase_NotFromTheTokenClaim()
    {
        var l = await SeedAsync();
        // Jeton « Admin » mais compte serveur en base : refusé.
        await AssertVoidAsync(l, Client("waiter", role: "Admin"), 1, "erreur", OrderLineVoidOutcome.Forbidden);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Route_WithoutAuthentication_Returns401AndChangesNothing(bool dispatched)
    {
        var l = await SeedAsync(new LineSpec { Dispatched = dispatched });
        var before = await StateAsync(l);
        (await PostVoidAsync(Client("anonymous"), l, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await StateAsync(l)).Should().Be(before);
    }

    [Theory]
    [InlineData(true, OrderLineVoidOutcome.Forbidden)]
    [InlineData(false, OrderLineVoidOutcome.Corrected)]
    public async Task Operator_DeactivatedOrDeletedAfterLogin(bool dispatched, OrderLineVoidOutcome expected)
    {
        var inactive = new User { Name = "Ancien manager", Role = UserRole.FloorManager, PinHash = "x", PinSalt = "x", IsActive = false };
        var deleted = new User { Name = "Manager supprimé", Role = UserRole.FloorManager, PinHash = "x", PinSalt = "x" };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.AddRange(inactive, deleted);
            await db.SaveChangesAsync();
            db.Users.Remove(deleted);
            await db.SaveChangesAsync();
        }
        _users["inactive"] = inactive.Id;
        _users["deleted"] = deleted.Id;

        // Compte supprimé : jeton encore valide, opérateur introuvable -> refus d'une annulation, correction acceptée (tout opérateur).
        var l = await SeedAsync(new LineSpec { Dispatched = dispatched });
        await AssertVoidAsync(l, Client("deleted"), 1, "erreur", expected);

        // Compte désactivé : le service ne lit que le rôle (IsActive n'est pas contrôlé), comme les autres actions manager.
        var l2 = await SeedAsync(new LineSpec { Dispatched = dispatched });
        await AssertVoidAsync(l2, Client("inactive"), 1, "erreur", dispatched ? OrderLineVoidOutcome.Voided : OrderLineVoidOutcome.Corrected);
    }

    // ---------- Quantités ----------

    [Theory]
    [InlineData(true, 0, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(true, -1, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(true, int.MinValue, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(true, 1, OrderLineVoidOutcome.Voided)]
    [InlineData(true, 2, OrderLineVoidOutcome.Voided)]
    [InlineData(true, 3, OrderLineVoidOutcome.Voided)]
    [InlineData(true, 4, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(true, int.MaxValue, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(false, 0, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(false, -1, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(false, 1, OrderLineVoidOutcome.Corrected)]
    [InlineData(false, 2, OrderLineVoidOutcome.Corrected)]
    [InlineData(false, 3, OrderLineVoidOutcome.Corrected)]
    [InlineData(false, 4, OrderLineVoidOutcome.InvalidQuantity)]
    [InlineData(false, int.MaxValue, OrderLineVoidOutcome.InvalidQuantity)]
    public async Task Quantity_Bounds(bool dispatched, int quantity, OrderLineVoidOutcome expected)
    {
        var l = await SeedAsync(new LineSpec { Quantity = 3, Dispatched = dispatched, OtherLineCents = 200 });
        await AssertVoidAsync(l, Client("manager"), quantity, "erreur", expected);
        if (expected != OrderLineVoidOutcome.Voided) return;

        var after = await StateAsync(l);
        after.TotalCents.Should().Be((3 - quantity) * 1500 + 200);
        after.TicketCancelled.Should().Be(quantity);
        after.TicketStatus.Should().Be(quantity == 3 ? TicketItemStatus.Cancelled : TicketItemStatus.Pending);
        var row = (await VoidRowsAsync(l)).Should().ContainSingle().Subject;
        row.Quantity.Should().Be(quantity);
        (await LastJetAsync(l)).GetProperty("quantity").GetInt32().Should().Be(quantity);
    }

    [Fact]
    public async Task Quantity_ExceedingIntRangeInJson_IsRejectedByModelBinding()
    {
        var l = await SeedAsync();
        var before = await StateAsync(l);
        var resp = await Client("manager").PostAsync($"/api/orders/{l.OrderId}/items/{l.ItemId}/void",
            new StringContent("{\"quantity\":99999999999,\"reason\":\"x\"}", System.Text.Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await StateAsync(l)).Should().Be(before);
    }

    // ---------- Motifs ----------

    public static TheoryData<string?, OrderLineVoidOutcome, string?> Reasons => new()
    {
        { null, OrderLineVoidOutcome.ReasonRequired, null },
        { "", OrderLineVoidOutcome.ReasonRequired, null },
        { "   ", OrderLineVoidOutcome.ReasonRequired, null },
        { "\n\t \r\n", OrderLineVoidOutcome.ReasonRequired, null },
        { "Erreur de saisie", OrderLineVoidOutcome.Voided, "Erreur de saisie" },
        { "  client parti \n", OrderLineVoidOutcome.Voided, "client parti" },
        { "ligne 1\nligne 2", OrderLineVoidOutcome.Voided, "ligne 1\nligne 2" },
        { "خطأ في الطلب 🍕 « été »", OrderLineVoidOutcome.Voided, "خطأ في الطلب 🍕 « été »" },
        { "<script>alert('x')</script>", OrderLineVoidOutcome.Voided, "<script>alert('x')</script>" },
        { new string('a', 256), OrderLineVoidOutcome.Voided, new string('a', 256) },
        { "  " + new string('b', 256) + "  ", OrderLineVoidOutcome.Voided, new string('b', 256) },
        { new string('a', 257), OrderLineVoidOutcome.ReasonTooLong, null },
        { new string('z', 5000), OrderLineVoidOutcome.ReasonTooLong, null },
        // 128 émojis = 256 unités UTF-16 (la limite se compte comme le maxlength du navigateur) ; 129 = 258.
        { string.Concat(Enumerable.Repeat("🍕", 128)), OrderLineVoidOutcome.Voided, string.Concat(Enumerable.Repeat("🍕", 128)) },
        { string.Concat(Enumerable.Repeat("🍕", 129)), OrderLineVoidOutcome.ReasonTooLong, null },
    };

    [Theory]
    [MemberData(nameof(Reasons))]
    public async Task Reason_OnSentLine(string? reason, OrderLineVoidOutcome expected, string? stored)
    {
        var l = await SeedAsync();
        await AssertVoidAsync(l, Client("manager"), 1, reason, expected);
        if (stored is null) return;
        (await VoidRowsAsync(l)).Should().ContainSingle().Which.Reason.Should().Be(stored, "le motif est enregistré après suppression des espaces autour, sans autre transformation");
        (await LastJetAsync(l)).GetProperty("reason").GetString().Should().Be(stored);
        var doc = await ReadAsync(db => db.PrintJobs.AsNoTracking().SingleAsync(j => j.PrinterId == l.PrinterId));
        RestaurantPos.Infrastructure.Printing.TicketDocumentJson.Deserialize(doc.DocumentJson).Lines.OfType<RestaurantPos.Infrastructure.Printing.TicketText>()
            .Select(t => t.Text).Should().Contain(t => t.EndsWith(stored, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("motif ignoré")]
    public async Task Reason_IsIgnoredForACorrection_EvenBeyondTheLimit(string? reason)
    {
        var l = await SeedAsync(new LineSpec { Dispatched = false, Status = OrderStatus.Open });
        await AssertVoidAsync(l, Client("waiter"), 1, reason, OrderLineVoidOutcome.Corrected);
        var l2 = await SeedAsync(new LineSpec { Dispatched = false, Status = OrderStatus.Open });
        await AssertVoidAsync(l2, Client("waiter"), 1, new string('x', 5000), OrderLineVoidOutcome.Corrected);
    }

    // ---------- États de commande ----------

    [Theory]
    [InlineData(OrderStatus.Open, OrderLineVoidOutcome.Voided)]
    [InlineData(OrderStatus.SentToKitchen, OrderLineVoidOutcome.Voided)]
    [InlineData(OrderStatus.BillRequested, OrderLineVoidOutcome.Voided)]
    [InlineData(OrderStatus.Paid, OrderLineVoidOutcome.NotModifiable)]
    [InlineData(OrderStatus.Cancelled, OrderLineVoidOutcome.NotModifiable)]
    public async Task OrderStatus_x_SentLine(OrderStatus status, OrderLineVoidOutcome expected)
    {
        var l = await SeedAsync(new LineSpec { Status = status });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", expected);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task OrderStatus_ClosedOrder_RefusesAlsoACorrection(OrderStatus status)
    {
        var l = await SeedAsync(new LineSpec { Status = status, Dispatched = false });
        var resp = await AssertVoidAsync(l, Client("waiter"), 1, null, OrderLineVoidOutcome.NotModifiable);
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("order_not_modifiable");
    }

    [Fact]
    public async Task Order_Unknown_OrLineOfAnotherOrder_Returns404()
    {
        var a = await SeedAsync();
        var b = await SeedAsync();
        var beforeA = await StateAsync(a);
        var beforeB = await StateAsync(b);

        (await PostVoidAsync(Client("manager"), Guid.NewGuid(), a.ItemId, 1, "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostVoidAsync(Client("manager"), a.OrderId, b.ItemId, 1, "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostVoidAsync(Client("manager"), a.OrderId, Guid.NewGuid(), 1, "x")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // L'existence prime sur la quantité et le rôle : un 404 même avec une quantité invalide et un serveur.
        (await PostVoidAsync(Client("waiter"), a.OrderId, b.ItemId, 0, null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await StateAsync(a)).Should().Be(beforeA);
        (await StateAsync(b)).Should().Be(beforeB);
    }

    [Fact]
    public async Task HeldOrder_StaysVoidable_AndAVoidedHoldIsNotModifiable()
    {
        var held = await SeedAsync(new LineSpec { Destination = OrderDestination.Takeaway });
        var voidedHold = await SeedAsync(new LineSpec { Destination = OrderDestination.Takeaway });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var storage = scope.ServiceProvider.GetRequiredService<IHeldOrderStorageService>();
            await storage.HoldOrderAsync(await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == held.OrderId), Terminal, _users["manager"], "Client 1");
            var hold = await storage.HoldOrderAsync(await db.Orders.Include(o => o.Items).SingleAsync(o => o.Id == voidedHold.OrderId), Terminal, _users["manager"], "Client 2");
            (await storage.VoidHeldOrderAsync(hold.Id, _users["manager"], "abandon")).Should().BeTrue();
        }

        await AssertVoidAsync(held, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        await AssertVoidAsync(voidedHold, Client("manager"), 1, "erreur", OrderLineVoidOutcome.NotModifiable);
    }

    // ---------- Reçus ----------

    private async Task<(Guid Id, string Number)> PayAsync(Guid orderId, long cents)
    {
        using var scope = _factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>()
            .ProcessPaymentTendersAsync(orderId, Terminal, [new PaymentTenderRequest(PaymentMethod.Cash, cents, cents)]);
        result.IsSuccess.Should().BeTrue();
        var id = (await scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.AsNoTracking().SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber)).Id;
        return (id, result.ReceiptNumber);
    }

    private async Task VoidReceiptAsync(Guid receiptId)
    {
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>().VoidReceiptAsync(receiptId, Terminal, _users["manager"])).IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData("none", OrderLineVoidOutcome.Voided)]
    [InlineData("full", OrderLineVoidOutcome.NotModifiable)]
    [InlineData("partial", OrderLineVoidOutcome.OrderHasReceipts)]
    [InlineData("partial+creditnote", OrderLineVoidOutcome.Voided)]
    [InlineData("full+creditnote", OrderLineVoidOutcome.NotModifiable)]
    [InlineData("two-one-voided", OrderLineVoidOutcome.OrderHasReceipts)]
    [InlineData("two-both-voided", OrderLineVoidOutcome.Voided)]
    public async Task Receipts_x_SentLine(string receipts, OrderLineVoidOutcome expected)
    {
        var l = await SeedAsync(new LineSpec { Quantity = 2 }); // 3 000 c
        var active = new List<string>();
        switch (receipts)
        {
            case "full":
                active.Add((await PayAsync(l.OrderId, 3000)).Number);
                break;
            case "partial":
                active.Add((await PayAsync(l.OrderId, 1000)).Number);
                break;
            case "partial+creditnote":
                await VoidReceiptAsync((await PayAsync(l.OrderId, 1000)).Id);
                break;
            case "full+creditnote":
                await VoidReceiptAsync((await PayAsync(l.OrderId, 3000)).Id); // reçu intégral annulé : commande annulée
                break;
            case "two-one-voided":
                var first = await PayAsync(l.OrderId, 1000);
                active.Add((await PayAsync(l.OrderId, 500)).Number);
                await VoidReceiptAsync(first.Id);
                break;
            case "two-both-voided":
                var r1 = await PayAsync(l.OrderId, 1000);
                var r2 = await PayAsync(l.OrderId, 500);
                await VoidReceiptAsync(r1.Id);
                await VoidReceiptAsync(r2.Id);
                break;
        }

        var resp = await AssertVoidAsync(l, Client("manager"), 1, "erreur", expected);
        if (expected != OrderLineVoidOutcome.OrderHasReceipts) return;
        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("code").GetString().Should().Be("order_has_receipts");
        var message = body.RootElement.GetProperty("message").GetString()!;
        foreach (var n in active) message.Should().Contain(n);
        message.Should().NotContain("-VOID-");
    }

    [Fact]
    public async Task Receipts_UnsentLine_IsAlsoLockedByAnActiveReceipt()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 2, Dispatched = false, Status = OrderStatus.Open });
        await PayAsync(l.OrderId, 1000);
        await AssertVoidAsync(l, Client("waiter"), 1, null, OrderLineVoidOutcome.OrderHasReceipts);
    }

    // ---------- Particularités de la ligne ----------

    [Fact]
    public async Task Feature_ModifiersAndExtraPrice_CopiedToVoidRowAndJet_R9()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 2, UnitCents = 1333, ExtraCents = 251, Modifiers = ["saignant", "sans sel"], Comment = "allergie arachide" });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        var row = (await VoidRowsAsync(l)).Single();
        row.UnitPrice.AmountInCents.Should().Be(1584);
        row.ModifiersSummary.Should().Be("saignant, sans sel");
        row.KitchenComment.Should().Be("allergie arachide");
        row.PreparationStationId.Should().Be(l.Station);
        row.ServerName.Should().Be("Sophie");
        row.OperatorName.Should().Be("Op manager");
        row.OperatorId.Should().Be(_users["manager"]);
        row.TableNumber.Should().Be(l.Table);
        row.ProductName.Should().Be(l.Product);
        (await LastJetAsync(l)).GetProperty("unitPriceCents").GetInt64().Should().Be(1584);
        (await StateAsync(l)).TotalCents.Should().Be(1584);
        var doc = await ReadAsync(db => db.PrintJobs.AsNoTracking().SingleAsync(j => j.PrinterId == l.PrinterId));
        var texts = RestaurantPos.Infrastructure.Printing.TicketDocumentJson.Deserialize(doc.DocumentJson).Lines.OfType<RestaurantPos.Infrastructure.Printing.TicketText>().Select(t => t.Text).ToList();
        texts.Should().Contain("-1  " + l.Product).And.Contain("+ saignant, sans sel").And.Contain(t => t.Contains("allergie arachide", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Feature_CompLine_RecordsListPriceAndLeavesTotalUnchanged()
    {
        // Ambiguïté signalée : une ligne offerte trace son prix catalogue (R9 = UnitPrice + suppléments), pas 0.
        var l = await SeedAsync(new LineSpec { Quantity = 2, Comp = true, OtherLineCents = 700 });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        (await VoidRowsAsync(l)).Single().UnitPrice.AmountInCents.Should().Be(1500);
        (await StateAsync(l)).TotalCents.Should().Be(700);
    }

    [Theory]
    [InlineData(DiscountType.Percentage, 10, 3, 1, 2 * 1333 + 777)]
    [InlineData(DiscountType.Percentage, 33.33, 3, 2, 1333 + 777)]
    [InlineData(DiscountType.Percentage, 12.5, 1, 1, 777)]
    [InlineData(DiscountType.FixedAmount, 5, 1, 1, 777)]
    [InlineData(DiscountType.FixedAmount, 50, 3, 3, 777)]
    public async Task Feature_OrderDiscount_TotalRoundedAfterVoid(DiscountType type, double value, int quantity, int voided, long subtotalAfter)
    {
        var l = await SeedAsync(new LineSpec { Quantity = quantity, UnitCents = 1333, OtherLineCents = 777, Discount = type, DiscountValue = (decimal)value });
        await AssertVoidAsync(l, Client("manager"), voided, "erreur", OrderLineVoidOutcome.Voided);
        var expected = type == DiscountType.Percentage
            ? (long)Math.Round(subtotalAfter * (1m - (decimal)value / 100m), MidpointRounding.AwayFromZero)
            : Math.Max(0, subtotalAfter - (long)Math.Round((decimal)value * 100m, MidpointRounding.AwayFromZero));
        (await StateAsync(l)).TotalCents.Should().Be(expected);
        (await VoidRowsAsync(l)).Single().UnitPrice.AmountInCents.Should().Be(1333, "le prix tracé est avant remise de commande");
    }

    [Fact]
    public async Task Feature_LastLine_EmptyOrderTotalZero_StaysOnTable()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 1, Discount = DiscountType.FixedAmount, DiscountValue = 5 });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        var s = await StateAsync(l);
        s.LineCount.Should().Be(0);
        s.TotalCents.Should().Be(0);
        (await ReadAsync(db => db.DiningTables.AsNoTracking().SingleAsync(t => t.TableNumber == l.Table))).ActiveOrderId.Should().Be(l.OrderId);
    }

    [Fact]
    public async Task Feature_LegacyLineWithoutOrderItemId_VoidsWithoutTouchingTheTicket()
    {
        var l = await SeedAsync(new LineSpec { LinkTicketItem = false });
        await AssertVoidAsync(l, Client("manager"), 2, "erreur", OrderLineVoidOutcome.Voided);
        var s = await StateAsync(l);
        s.TicketCancelled.Should().Be(0);
        s.TicketStatus.Should().Be(TicketItemStatus.Pending);
        (await VoidRowsAsync(l)).Single().TicketId.Should().BeNull();
        (await LastJetAsync(l)).GetProperty("ticketId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData(OrderDestination.EatIn, true, "Sophie", "Sophie")]
    [InlineData(OrderDestination.EatIn, true, null, "")]
    [InlineData(OrderDestination.Takeaway, false, null, "")]
    public async Task Feature_Destination_ServerName(OrderDestination destination, bool withTable, string? waiter, string expectedServer)
    {
        var l = await SeedAsync(new LineSpec { Destination = destination, WithTable = withTable, Waiter = waiter, Table = withTable ? null : "Comptoir" });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        var row = (await VoidRowsAsync(l)).Single();
        row.ServerName.Should().Be(expectedServer);
        row.TableNumber.Should().Be(withTable ? l.Table : "Comptoir");
    }

    [Theory]
    [InlineData(TicketItemStatus.InPrep)]
    [InlineData(TicketItemStatus.Ready)]
    public async Task Feature_TicketItemAlreadyInPreparationOrReady_StillMarked(TicketItemStatus status)
    {
        var l = await SeedAsync(new LineSpec { Quantity = 2 });
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.KitchenTicketItems.SingleAsync(i => i.Id == l.TicketItemId)).Status = status;
            await db.SaveChangesAsync();
        }
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        var s = await StateAsync(l);
        s.TicketCancelled.Should().Be(1);
        s.TicketStatus.Should().Be(status, "une annulation partielle garde l'état de préparation");
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        (await StateAsync(l)).TicketStatus.Should().Be(TicketItemStatus.Cancelled);
    }

    // ---------- Enchaînements ----------

    [Fact]
    public async Task Sequence_VoidTwice_ThenRemainderBeyond_ThenAbsentLine()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 3, OtherLineCents = 100 });
        await AssertVoidAsync(l, Client("manager"), 1, "un", OrderLineVoidOutcome.Voided);
        await AssertVoidAsync(l, Client("manager"), 3, "trop", OrderLineVoidOutcome.InvalidQuantity);
        await AssertVoidAsync(l, Client("admin"), 2, "deux", OrderLineVoidOutcome.Voided);
        var s = await StateAsync(l);
        s.Should().Be(new State(null, 1, 100, 2, 2, 3, TicketItemStatus.Cancelled, 2));
        (await PostVoidAsync(Client("manager"), l, 1, "encore")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StateAsync(l)).Should().Be(s);
        (await VoidRowsAsync(l)).Select(v => (v.Quantity, v.Reason, v.OperatorName)).Should().BeEquivalentTo(new[] { (1, "un", "Op manager"), (2, "deux", "Op admin") });
    }

    [Fact]
    public async Task Sequence_CorrectionThenDispatchThenVoid_AndWaiterCannotCorrectASentLine()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 3, Dispatched = false, Status = OrderStatus.Open });
        await AssertVoidAsync(l, Client("waiter"), 1, null, OrderLineVoidOutcome.Corrected);
        using (var scope = _factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IKitchenRoutingService>().SplitAndRouteOrderAsync(l.OrderId);
            (await scope.ServiceProvider.GetRequiredService<ITableManagementService>().DispatchOrderLinesAsync(l.Table)).Should().BeTrue();
        }
        await AssertVoidAsync(l, Client("waiter"), 1, null, OrderLineVoidOutcome.Forbidden);
        var resp = await AssertVoidAsync(l, Client("manager"), 2, "client parti", OrderLineVoidOutcome.Voided);
        var routed = await ReadAsync(db => db.KitchenTicketItems.AsNoTracking().SingleAsync(i => i.OrderItemId == l.ItemId));
        routed.Quantity.Should().Be(2, "le bon porte la quantité après correction");
        routed.CancelledQuantity.Should().Be(2);
        routed.Status.Should().Be(TicketItemStatus.Cancelled);
        (await VoidRowsAsync(l)).Single().TicketId.Should().Be(routed.TicketId);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sequence_RepeatedIdenticalRequest_IsNotIdempotent_EachCallVoidsAgain()
    {
        // Pas de clé d'idempotence : deux requêtes identiques (double tap côté client) annulent deux fois ; la garde est côté clients.
        var l = await SeedAsync(new LineSpec { Quantity = 2 });
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        await AssertVoidAsync(l, Client("manager"), 1, "erreur", OrderLineVoidOutcome.Voided);
        (await StateAsync(l)).Voids.Should().Be(2);
    }

    // ---------- Impression ----------

    [Fact]
    public async Task Printing_NoPrinterForTheStation_StillVoids()
    {
        var l = await SeedAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.PrinterConfigurations.SingleAsync(p => p.Id == l.PrinterId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        var before = await StateAsync(l);
        (await PostVoidAsync(Client("manager"), l, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await StateAsync(l);
        after.Voids.Should().Be(1);
        after.Jet.Should().Be(1);
        after.PrintJobs.Should().Be(before.PrintJobs, "imprimante inactive : aucun bon, l'annulation reste validée");
    }

    [Theory]
    [InlineData("product")]
    [InlineData("category")]
    public async Task Printing_StationResolvedFromProductOrCategory(string level)
    {
        var token = Random.Shared.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
        var station = "RES_" + token;
        var l = await SeedAsync(new LineSpec { ItemStation = false });
        Guid printerId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var categoryId = "cat-" + token;
            db.Categories.Add(new Category { Id = categoryId, Name = "Cat " + token, PreparationStationId = level == "category" ? station : null });
            var productId = (await db.OrderItems.SingleAsync(i => i.Id == l.ItemId)).ProductId;
            db.Products.Add(new Product { Id = productId, Name = l.Product, CategoryId = categoryId, Price = Money.FromCents(1500), PreparationStationId = level == "product" ? station : null });
            var printer = new PrinterConfiguration { Name = "R-" + token, IpAddress = "10.0.0.2", AssignedStationIds = [station] };
            db.PrinterConfigurations.Add(printer);
            await db.SaveChangesAsync();
            printerId = printer.Id;
        }

        (await PostVoidAsync(Client("manager"), l, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReadAsync(db => db.PrintJobs.AsNoTracking().CountAsync(j => j.PrinterId == printerId))).Should().Be(1);
        (await StateAsync(l)).PrintJobs.Should().Be(0, "l'imprimante de la station du test n'est pas celle résolue");
    }

    [Fact]
    public async Task Printing_TwoPrintersOnTheStation_EachGetsOneSlip()
    {
        var l = await SeedAsync();
        Guid second;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = new PrinterConfiguration { Name = "Bis", IpAddress = "10.0.0.3", AssignedStationIds = [l.Station] };
            db.PrinterConfigurations.Add(p);
            await db.SaveChangesAsync();
            second = p.Id;
        }
        (await PostVoidAsync(Client("manager"), l, 1, "erreur")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StateAsync(l)).PrintJobs.Should().Be(1);
        (await ReadAsync(db => db.PrintJobs.AsNoTracking().CountAsync(j => j.PrinterId == second))).Should().Be(1);
    }

    // ---------- Messages localisés ----------

    public static TheoryData<string, string> LocalizedRefusals()
    {
        var data = new TheoryData<string, string>();
        foreach (var code in new[] { "not_found", "forbidden", "reason_required", "reason_too_long", "quantity", "receipts", "not_modifiable" })
            foreach (var lang in new[] { "fr", "en", "ar" })
                data.Add(code, lang);
        return data;
    }

    [Theory]
    [MemberData(nameof(LocalizedRefusals))]
    public async Task Refusal_MessagesAreLocalizedFromAcceptLanguage(string code, string lang)
    {
        var l = await SeedAsync(new LineSpec { Quantity = 2, Status = code == "not_modifiable" ? OrderStatus.Paid : OrderStatus.SentToKitchen });
        string? receipt = code == "receipts" ? (await PayAsync(l.OrderId, 1000)).Number : null;
        var (user, quantity, reason, itemId, key) = code switch
        {
            "not_found" => ("manager", 1, "x", Guid.NewGuid(), "errors.order_not_found"),
            "forbidden" => ("waiter", 1, "x", l.ItemId, "errors.authorization_insufficient"),
            "reason_required" => ("manager", 1, " ", l.ItemId, "errors.line_void_reason_required"),
            "reason_too_long" => ("manager", 1, new string('r', 257), l.ItemId, "errors.line_void_reason_too_long"),
            "quantity" => ("manager", 3, "x", l.ItemId, "errors.line_void_quantity_invalid"),
            "receipts" => ("manager", 1, "x", l.ItemId, "errors.order_has_receipts"),
            _ => ("manager", 1, "x", l.ItemId, "errors.order_not_modifiable"),
        };
        var before = await StateAsync(l);

        var resp = await PostVoidAsync(Client(user, language: lang), l.OrderId, itemId, quantity, reason);

        using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var message = body.RootElement.GetProperty("message").GetString();
        var culture = new CultureInfo(lang);
        message.Should().Be(Texts.Get(culture, key, ("receipts", receipt), ("max", 256)));
        Texts.Keys(culture).Should().Contain(key, "la clé existe dans la langue (pas de repli silencieux)");
        if (lang != "en") message.Should().NotBe(Texts.Get(CultureInfo.InvariantCulture, key, ("receipts", receipt), ("max", 256)));
        if (code == "receipts") message.Should().Contain(receipt);
        if (code == "reason_too_long") message.Should().Contain("256");
        (await StateAsync(l)).Should().Be(before);
    }

    [Fact]
    public async Task Refusal_FrenchMessages_AreExact()
    {
        var l = await SeedAsync(new LineSpec { Quantity = 1 });
        async Task<string> Msg(HttpClient c, int q, string? r) =>
            JsonDocument.Parse(await (await PostVoidAsync(c, l, q, r)).Content.ReadAsStringAsync()).RootElement.GetProperty("message").GetString()!;
        (await Msg(Client("manager", language: "fr"), 1, null)).Should().Be("Le motif est obligatoire pour annuler une ligne envoyée en cuisine.");
        (await Msg(Client("manager", language: "fr"), 1, new string('r', 300))).Should().Be("Le motif ne peut pas dépasser 256 caractères.");
        (await Msg(Client("manager", language: "fr"), 2, "x")).Should().Be("La quantité à retirer doit être comprise entre 1 et celle de la ligne.");
        (await Msg(Client("waiter", language: "fr"), 1, "x")).Should().Be("Autorisation insuffisante : code PIN superviseur ou gérant requis.");
    }
}
