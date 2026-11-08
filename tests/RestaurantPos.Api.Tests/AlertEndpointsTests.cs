using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class AlertEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public AlertEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient Client(string role, string? operatorId = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        if (operatorId is not null) client.DefaultRequestHeaders.Add("X-Test-Operator-Id", operatorId);
        return client;
    }

    /// <summary>Ligne de commande déjà encaissée sur le poste appairé puis annulée par avoir : <paramref name="count"/> avoirs du même serveur.</summary>
    private async Task SeedCreditsAsync(string terminal, Guid server, int count)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 0; i < count; i++)
        {
            var order = new Order { OperatorId = server, Status = OrderStatus.Cancelled, TableNumber = "A" };
            order.Items.Add(new OrderItem { OrderId = order.Id, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(1_000), TaxRatePercent = 10m });
            db.Orders.Add(order);
            db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = terminal, ReceiptNumber = Guid.NewGuid().ToString("N")[..8], OrderId = order.Id, TotalTtcAmount = Money.FromCents(-1_000), VoidedReceiptId = Guid.NewGuid() });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task AllRoutes_AreManagerOnly()
    {
        var waiter = Client("Waiter");
        (await waiter.GetAsync("/api/alerts")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync("/api/alerts/rules")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PutAsJsonAsync("/api/alerts/rules/voids_count", new { threshold = 1, enabled = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsJsonAsync($"/api/alerts/{Guid.NewGuid()}/resolve", new { note = "x" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync("/api/alerts/settings")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Rules_AreListedDisabledByDefault_AndEditable()
    {
        var admin = Client("Admin");
        var rules = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts/rules");
        rules!.Select(r => r.GetProperty("type").GetString()).Should().Equal("cash_variance", "voids_count", "discount_percent", "comps_total");

        (await admin.PutAsJsonAsync("/api/alerts/rules/comps_total", new { threshold = 7_000, enabled = true })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PutAsJsonAsync("/api/alerts/rules/inconnue", new { threshold = 1, enabled = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PutAsJsonAsync("/api/alerts/rules/voids_count", new { threshold = -1, enabled = true })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var again = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts/rules");
        again!.Single(r => r.GetProperty("type").GetString() == "comps_total").GetProperty("threshold").GetInt64().Should().Be(7_000);
        await admin.PutAsJsonAsync("/api/alerts/rules/comps_total", new { threshold = 5_000, enabled = false });
    }

    [Fact]
    public async Task APaidSale_EvaluatesTheThresholds_OneAlertOnly_ResolvedWithANote()
    {
        var operatorId = Guid.NewGuid();
        var admin = Client("Admin", operatorId.ToString());
        var device = await DeviceTestHelper.PairAsync(_factory, admin, "Caisse alertes");
        await SeedCreditsAsync(device.TerminalId, operatorId, 4);
        await admin.PutAsJsonAsync("/api/alerts/rules/voids_count", new { threshold = 3, enabled = true });

        // Une vente encaissée sur ce poste déclenche l'évaluation ; la relancer ne duplique rien.
        for (var i = 0; i < 2; i++)
        {
            var orderId = Guid.NewGuid();
            using (var scope = _factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Orders.Add(new Order { Id = orderId, TableNumber = "S" + i, Status = OrderStatus.Open, Items = { new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(1_000), TaxRatePercent = 10m } } });
                await db.SaveChangesAsync();
            }
            (await admin.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(orderId, null, 1_000, [new SyncTenderDto(PaymentMethod.Cash, 1_000, 1_000)], null))).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var open = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts?state=open");
        var mine = open!.Where(a => a.GetProperty("terminalId").GetString() == device.TerminalId).ToList();
        mine.Should().ContainSingle();
        mine[0].GetProperty("ruleType").GetString().Should().Be("voids_count");
        var id = mine[0].GetProperty("id").GetGuid();

        (await admin.PostAsJsonAsync($"/api/alerts/{id}/resolve", new { note = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await admin.PostAsJsonAsync($"/api/alerts/{id}/resolve", new { note = "Erreurs de saisie vérifiées" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var resolved = await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts?state=resolved");
        resolved!.Should().Contain(a => a.GetProperty("id").GetGuid() == id);
        (await admin.GetFromJsonAsync<List<JsonElement>>("/api/alerts?state=open"))!.Should().NotContain(a => a.GetProperty("id").GetGuid() == id);
        (await admin.GetAsync("/api/alerts?state=bidon")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await admin.PutAsJsonAsync("/api/alerts/rules/voids_count", new { threshold = 3, enabled = false });
    }

    private sealed class ThrowingAlerts : IAlertService
    {
        public Task<int> EvaluateAsync(CancellationToken ct = default) => throw new InvalidOperationException("base d'alertes indisponible");
        public Task<bool> EvaluateCashCountAsync(Guid sessionId, CancellationToken ct = default) => throw new InvalidOperationException("indisponible");
        public bool TakeChanged() => false;
        public Task<IReadOnlyList<AlertDto>> ListAsync(string? state, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AlertDto?> ResolveAsync(Guid id, string note, Guid operatorId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AlertRuleDto>> RulesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AlertRuleDto?> SetRuleAsync(string type, long threshold, bool enabled, Guid? operatorId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AlertSettings> GetSettingsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AlertSettings> UpdateSettingsAsync(AlertSettings settings, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task APaymentIsNeverCancelled_ByAFailingAlertEvaluation()
    {
        var factory = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IAlertService, ThrowingAlerts>()));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "Admin");
        await DeviceTestHelper.PairAsync(factory, client, "Caisse panne alertes");
        var orderId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Orders.Add(new Order { Id = orderId, TableNumber = "NB", Status = OrderStatus.Open, Items = { new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(1_000), TaxRatePercent = 10m } } });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(orderId, null, 1_000, [new SyncTenderDto(PaymentMethod.Cash, 1_000, 1_000)], null));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Any(r => r.OrderId == orderId).Should().BeTrue();
    }
}
