using Microsoft.AspNetCore.SignalR.Client;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using System;
using System.Threading.Tasks;

namespace RestaurantPos.Client.Maui.Services;

public interface IKitchenSignalRClient
{
    event Action<KitchenTicketDto>? OnNewTicketReceived;
    event Action<Guid, TicketStatus>? OnTicketStatusChanged;
    event Action<Guid, TicketStatus>? OnTicketRecalled;

    Task ConnectAsync(string hubUrl, string stationId, string? accessToken = null);
    Task DisconnectAsync();
    Task BumpTicketAsync(Guid ticketId);
    Task RecallTicketAsync(Guid ticketId);
}

public class KitchenSignalRClient : IKitchenSignalRClient, IAsyncDisposable
{
    private HubConnection? _hubConnection;

    public event Action<KitchenTicketDto>? OnNewTicketReceived;
    public event Action<Guid, TicketStatus>? OnTicketStatusChanged;
    public event Action<Guid, TicketStatus>? OnTicketRecalled;

    public async Task ConnectAsync(string hubUrl, string stationId, string? accessToken = null)
    {
        if (_hubConnection != null)
        {
            await DisconnectAsync();
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{hubUrl}/hubs/pos", options =>
            {
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    options.AccessTokenProvider = () => Task.FromResult<string?>(accessToken);
                }
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<KitchenTicketDto>("OnNewTicketReceived", ticket =>
        {
            if (ticket.StationId == stationId || string.IsNullOrEmpty(stationId))
            {
                OnNewTicketReceived?.Invoke(ticket);
            }
        });

        _hubConnection.On<Guid, TicketStatus>("OnTicketStatusChanged", (ticketId, status) =>
        {
            OnTicketStatusChanged?.Invoke(ticketId, status);
        });

        _hubConnection.On<Guid, TicketStatus>("OnTicketRecalled", (ticketId, status) =>
        {
            OnTicketRecalled?.Invoke(ticketId, status);
        });

        await _hubConnection.StartAsync();
    }

    public async Task DisconnectAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }
    }

    public async Task BumpTicketAsync(Guid ticketId)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.InvokeAsync("BumpTicket", ticketId);
        }
    }

    public async Task RecallTicketAsync(Guid ticketId)
    {
        if (_hubConnection != null)
        {
            // Assuming we add RecallTicket to PosHub in the future, or we just invoke BumpTicket back to Pending
            // For now, let's invoke a local action for the UI
            OnTicketRecalled?.Invoke(ticketId, TicketStatus.Pending);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
