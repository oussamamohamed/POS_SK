using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class KitchenEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public KitchenEndpointsTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<KitchenTicket> CreateTestTicketAsync(TicketStatus status = TicketStatus.Pending)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var ticket = new KitchenTicket
        {
            Id = UuidV7.NewGuid(),
            OrderId = Guid.NewGuid(),
            TableNumber = "T01",
            StationId = "HOT_KITCHEN",
            Status = status,
            DispatchedAtUtc = DateTimeOffset.UtcNow,
            Items = new List<KitchenTicketItem>
            {
                new()
                {
                    Id = UuidV7.NewGuid(),
                    ProductId = Guid.NewGuid(),
                    ProductName = "Steak Frites",
                    Quantity = 2
                }
            }
        };

        db.KitchenTickets.Add(ticket);
        await db.SaveChangesAsync();
        return ticket;
    }

    [Fact]
    public async Task GetTickets_AllowsAnonymous_ReturnsList()
    {
        var client = _factory.CreateClient();
        await CreateTestTicketAsync();

        var response = await client.GetAsync("/api/kds/tickets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tickets = await response.Content.ReadFromJsonAsync<List<JsonElement>>();
        tickets.Should().NotBeNull();
        tickets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BumpTicket_AsWaiter_ReturnsForbidden()
    {
        var ticket = await CreateTestTicketAsync();
        var waiterClient = CreateClientWithRole("Waiter");

        var response = await waiterClient.PostAsync($"/api/kds/tickets/{ticket.Id}/bump", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BumpTicket_AsKitchenStaff_AdvancesStatus()
    {
        var ticket = await CreateTestTicketAsync(TicketStatus.Pending);
        var kitchenClient = CreateClientWithRole("KitchenStaff");

        // Pending -> InPreparation
        var resp1 = await kitchenClient.PostAsync($"/api/kds/tickets/{ticket.Id}/bump", null);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await resp1.Content.ReadFromJsonAsync<JsonElement>();
        body1.GetProperty("status").GetInt32().Should().Be((int)TicketStatus.InPreparation);

        // InPreparation -> Ready
        var resp2 = await kitchenClient.PostAsync($"/api/kds/tickets/{ticket.Id}/bump", null);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await resp2.Content.ReadFromJsonAsync<JsonElement>();
        body2.GetProperty("status").GetInt32().Should().Be((int)TicketStatus.Ready);

        // Ready -> Served
        var resp3 = await kitchenClient.PostAsync($"/api/kds/tickets/{ticket.Id}/bump", null);
        resp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var body3 = await resp3.Content.ReadFromJsonAsync<JsonElement>();
        body3.GetProperty("status").GetInt32().Should().Be((int)TicketStatus.Served);
    }

    [Fact]
    public async Task BumpTicket_WithNonExistentId_ReturnsNotFound()
    {
        var kitchenClient = CreateClientWithRole("KitchenStaff");

        var response = await kitchenClient.PostAsync($"/api/kds/tickets/{Guid.NewGuid()}/bump", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
