using System;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Services;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// Deterministic fake for <see cref="IKitchenSignalRClient"/> enabling in-memory testing
/// of KDS push events without a real network connection or SignalR hub.
/// </summary>
public class FakeKitchenSignalRClient : IKitchenSignalRClient
{
    public event Action<KitchenTicketDto>? OnNewTicketReceived;
    public event Action<Guid, TicketStatus>? OnTicketStatusChanged;
    public event Action<Guid, TicketStatus>? OnTicketRecalled;

    public bool IsConnected { get; private set; }
    public string? ConnectedStationId { get; private set; }

    public Task ConnectAsync(string hubUrl, string stationId, string? accessToken = null)
    {
        IsConnected = true;
        ConnectedStationId = stationId;
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        ConnectedStationId = null;
        return Task.CompletedTask;
    }

    public Task BumpTicketAsync(Guid ticketId)
    {
        return Task.CompletedTask;
    }

    public Task RecallTicketAsync(Guid ticketId)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulates a server push event containing a new ticket.
    /// </summary>
    public void RaiseNewTicket(KitchenTicketDto ticket)
    {
        OnNewTicketReceived?.Invoke(ticket);
    }

    /// <summary>
    /// Simulates a server push event indicating a ticket's status has changed.
    /// </summary>
    public void RaiseTicketStatusChanged(Guid ticketId, TicketStatus newStatus)
    {
        OnTicketStatusChanged?.Invoke(ticketId, newStatus);
    }

    /// <summary>
    /// Simulates a server push event indicating a ticket has been recalled.
    /// </summary>
    public void RaiseTicketRecalled(Guid ticketId, TicketStatus restoredStatus)
    {
        OnTicketRecalled?.Invoke(ticketId, restoredStatus);
    }
}
