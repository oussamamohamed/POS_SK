using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Api.Hubs;

public interface IPosHubClient : IKitchenHubClient
{
    Task OnTableStatusChanged(string tableNumber, TableStatus newStatus);
}

public class PosHub : Hub<IPosHubClient>
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    [Authorize]
    public async Task UpdateTableStatus(string tableNumber, TableStatus newStatus)
    {
        await Clients.Others.OnTableStatusChanged(tableNumber, newStatus);
    }

    [Authorize]
    public async Task DispatchKitchenTicket(KitchenTicketDto ticket)
    {
        await Clients.All.OnNewTicketReceived(ticket);
    }

    [Authorize]
    public async Task BumpTicket(Guid ticketId)
    {
        await Clients.All.OnTicketStatusChanged(ticketId, TicketStatus.InPreparation);
    }
}
