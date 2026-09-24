using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Api.Hubs;

public class KitchenHub : Hub<IKitchenHubClient>
{
    private readonly IKitchenRoutingService _routingService;

    public KitchenHub(IKitchenRoutingService routingService)
    {
        _routingService = routingService;
    }

    public async Task JoinStationGroup(string stationId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Station_{stationId}").ConfigureAwait(false);
    }

    public async Task LeaveStationGroup(string stationId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"Station_{stationId}").ConfigureAwait(false);
    }

    public async Task DispatchOrderToKitchen(Guid orderId)
    {
        var tickets = await _routingService.SplitAndRouteOrderAsync(orderId).ConfigureAwait(false);

        foreach (var ticket in tickets)
        {
            await Clients.Group($"Station_{ticket.StationId}").OnNewTicketReceived(ticket).ConfigureAwait(false);
            await Clients.Group("Station_All").OnNewTicketReceived(ticket).ConfigureAwait(false);
        }
    }

    public async Task BumpTicket(Guid ticketId)
    {
        var updated = await _routingService.BumpTicketStateAsync(ticketId).ConfigureAwait(false);
        if (updated is not null)
        {
            await Clients.All.OnTicketStatusChanged(updated.TicketId, updated.Status).ConfigureAwait(false);
        }
    }

    public async Task RecallTicket(Guid ticketId)
    {
        var recalled = await _routingService.RecallTicketAsync(ticketId).ConfigureAwait(false);
        if (recalled is not null)
        {
            await Clients.All.OnTicketRecalled(recalled.TicketId, recalled.Status).ConfigureAwait(false);
        }
    }
}
