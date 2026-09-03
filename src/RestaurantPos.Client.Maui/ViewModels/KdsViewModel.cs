using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Services;
using RestaurantPos.Domain.Entities;
#if MAUI_UI
using Microsoft.Maui.ApplicationModel;
#endif
namespace RestaurantPos.Client.Maui.ViewModels;

public class KdsTicketItemViewModel : ObservableObject
{
    public required KitchenTicketDto Ticket { get; set; }

    private string _elapsedTimeText = "00:00";
    public string ElapsedTimeText
    {
        get => _elapsedTimeText;
        set => SetProperty(ref _elapsedTimeText, value);
    }

    private string _urgencyColorHex = "#10B981"; // Green default
    public string UrgencyColorHex
    {
        get => _urgencyColorHex;
        set => SetProperty(ref _urgencyColorHex, value);
    }

    public void UpdateTimer()
    {
        var elapsed = DateTimeOffset.UtcNow - Ticket.DispatchedAtUtc;
        ElapsedTimeText = $"{(int)elapsed.TotalMinutes:D2}:{elapsed.Seconds:D2}";

        if (elapsed.TotalMinutes >= 20)
        {
            UrgencyColorHex = "#EF4444"; // Red (Critical)
        }
        else if (elapsed.TotalMinutes >= 10)
        {
            UrgencyColorHex = "#F59E0B"; // Amber (Warning)
        }
        else
        {
            UrgencyColorHex = "#10B981"; // Green (Normal)
        }
    }
}

public partial class KdsViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly IKitchenRoutingService? _routingService;
    private readonly IKitchenSignalRClient? _signalRClient;

    [ObservableProperty]
    private string _currentStationId = "STATION-ALL";

    public ObservableCollection<KdsTicketItemViewModel> PendingTickets { get; } = [];
    public ObservableCollection<KdsTicketItemViewModel> InPrepTickets { get; } = [];
    public ObservableCollection<KdsTicketItemViewModel> ReadyTickets { get; } = [];
    public ObservableCollection<KdsTicketItemViewModel> ServedTickets { get; } = [];

    public KdsViewModel(
        IPlatformEnvironmentService environmentService,
        IKitchenRoutingService? routingService = null,
        IKitchenSignalRClient? signalRClient = null)
    {
        _environmentService = environmentService;
        _routingService = routingService;
        _signalRClient = signalRClient;

        if (_signalRClient != null)
        {
            _signalRClient.OnNewTicketReceived += HandleNewTicketReceived;
            _signalRClient.OnTicketStatusChanged += HandleTicketStatusChanged;
            _signalRClient.OnTicketRecalled += HandleTicketStatusChanged; // Reuse for recall
            _ = ConnectSignalRAsync();
        }
    }

    private async Task ConnectSignalRAsync()
    {
        if (_signalRClient != null)
        {
            try
            {
                var hubUrl = "http://localhost:5247";
                await _signalRClient.ConnectAsync(hubUrl, CurrentStationId);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR KDS Connection failed: {ex.Message}");
            }
        }
    }

    private void HandleNewTicketReceived(KitchenTicketDto ticket)
    {
#if MAUI_UI
        MainThread.BeginInvokeOnMainThread(() => AddIncomingTicket(ticket));
#else
        AddIncomingTicket(ticket);
#endif
    }

    private void HandleTicketStatusChanged(Guid ticketId, TicketStatus newStatus)
    {
        var action = () =>
        {
            var ticketItem = PendingTickets.Concat(InPrepTickets).Concat(ReadyTickets).Concat(ServedTickets)
                                           .FirstOrDefault(t => t.Ticket.TicketId == ticketId);
            if (ticketItem != null)
            {
                ticketItem.Ticket = ticketItem.Ticket with { Status = newStatus };
                MoveTicketToCorrectList(ticketItem);
            }
        };

#if MAUI_UI
        MainThread.BeginInvokeOnMainThread(action);
#else
        action();
#endif
    }

    private void MoveTicketToCorrectList(KdsTicketItemViewModel ticketItem)
    {
        PendingTickets.Remove(ticketItem);
        InPrepTickets.Remove(ticketItem);
        ReadyTickets.Remove(ticketItem);
        ServedTickets.Remove(ticketItem);

        switch (ticketItem.Ticket.Status)
        {
            case TicketStatus.Pending:
                PendingTickets.Add(ticketItem);
                break;
            case TicketStatus.InPreparation:
                InPrepTickets.Add(ticketItem);
                break;
            case TicketStatus.Ready:
                ReadyTickets.Add(ticketItem);
                break;
            case TicketStatus.Served:
                ServedTickets.Add(ticketItem);
                break;
        }
    }

    public void AddIncomingTicket(KitchenTicketDto ticket)
    {
        if (CurrentStationId != "STATION-ALL" && ticket.StationId != CurrentStationId)
        {
            return;
        }

        var itemVm = new KdsTicketItemViewModel { Ticket = ticket };
        itemVm.UpdateTimer();
        PendingTickets.Add(itemVm);

        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
    }

    [RelayCommand]
    public async Task BumpTicketAsync(KdsTicketItemViewModel ticketItem)
    {
        var currentStatus = ticketItem.Ticket.Status;

        if (_routingService is not null)
        {
            var updated = await _routingService.BumpTicketStateAsync(ticketItem.Ticket.TicketId).ConfigureAwait(false);
            if (updated is not null)
            {
                ticketItem.Ticket = updated;
            }
        }
        else if (_signalRClient is not null)
        {
            await _signalRClient.BumpTicketAsync(ticketItem.Ticket.TicketId);
            // Wait for SignalR to update status via event
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
            return;
        }
        else
        {
            var nextStatus = currentStatus switch
            {
                TicketStatus.Pending => TicketStatus.InPreparation,
                TicketStatus.InPreparation => TicketStatus.Ready,
                TicketStatus.Ready => TicketStatus.Served,
                _ => currentStatus
            };

            ticketItem.Ticket = ticketItem.Ticket with { Status = nextStatus };
        }

        MoveTicketToCorrectList(ticketItem);

        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public async Task RecallTicketAsync(KdsTicketItemViewModel ticketItem)
    {
        if (_routingService is not null)
        {
            var recalled = await _routingService.RecallTicketAsync(ticketItem.Ticket.TicketId).ConfigureAwait(false);
            if (recalled is not null)
            {
                ticketItem.Ticket = recalled;
            }
        }
        else if (_signalRClient is not null)
        {
            await _signalRClient.RecallTicketAsync(ticketItem.Ticket.TicketId);
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Warning);
            return;
        }
        else
        {
            ticketItem.Ticket = ticketItem.Ticket with { Status = TicketStatus.Pending };
        }

        MoveTicketToCorrectList(ticketItem);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Warning);
    }

    public void RefreshTimers()
    {
        foreach (var t in PendingTickets.Concat(InPrepTickets).Concat(ReadyTickets))
        {
            t.UpdateTimer();
        }
    }
}
