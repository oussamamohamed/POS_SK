using Microsoft.AspNetCore.SignalR.Client;
using RestaurantPos.Domain.Entities;
using System;
using System.Threading.Tasks;

namespace RestaurantPos.Client.Maui.Services;

public interface ITableSignalRClient
{
    event Action<string, TableStatus>? OnTableStatusChanged;

    Task ConnectAsync(string hubUrl);
    Task DisconnectAsync();
    Task UpdateTableStatusAsync(string tableNumber, TableStatus newStatus);
}

public class TableSignalRClient : ITableSignalRClient, IAsyncDisposable
{
    private HubConnection? _hubConnection;

    public event Action<string, TableStatus>? OnTableStatusChanged;

    public async Task ConnectAsync(string hubUrl)
    {
        if (_hubConnection != null)
        {
            await DisconnectAsync();
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl($"{hubUrl}/hubs/pos")
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<string, TableStatus>("OnTableStatusChanged", (tableNumber, status) =>
        {
            OnTableStatusChanged?.Invoke(tableNumber, status);
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

    public async Task UpdateTableStatusAsync(string tableNumber, TableStatus newStatus)
    {
        if (_hubConnection != null)
        {
            await _hubConnection.InvokeAsync("UpdateTableStatus", tableNumber, newStatus);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        GC.SuppressFinalize(this);
    }
}
