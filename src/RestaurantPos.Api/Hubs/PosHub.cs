using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using System;
using System.Threading.Tasks;

namespace RestaurantPos.Api.Hubs;

public interface IPosHubClient : IKitchenHubClient
{
    Task OnTableStatusChanged(string tableNumber, TableStatus newStatus);
}

public class PosHub : Hub<IPosHubClient>
{
    // Clients can call these methods to broadcast to others
    public async Task UpdateTableStatus(string tableNumber, TableStatus newStatus)
    {
        await Clients.Others.OnTableStatusChanged(tableNumber, newStatus);
    }

    public async Task DispatchKitchenTicket(KitchenTicketDto ticket)
    {
        await Clients.All.OnNewTicketReceived(ticket);
    }

    public async Task BumpTicket(Guid ticketId)
    {
        // Typically, bumping a ticket changes it to InPreparation or Ready
        // Let's assume InPreparation for simplicity when bumped from pending
        await Clients.All.OnTicketStatusChanged(ticketId, TicketStatus.InPreparation);
    }
}
