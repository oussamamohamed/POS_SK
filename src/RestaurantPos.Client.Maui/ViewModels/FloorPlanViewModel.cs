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

public partial class FloorPlanViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ITableManagementService? _tableService;
    private readonly ITableSignalRClient? _signalRClient;

    [ObservableProperty]
    private DiningTable? _selectedTable;

    [ObservableProperty]
    private bool _isTablePromptOpen;

    [ObservableProperty]
    private int _coversToOpen = 2;

    [ObservableProperty]
    private string _activeSection = "Salle Principale";

    public ObservableCollection<DiningTable> Tables { get; } = [];

    public FloorPlanViewModel(
        IPlatformEnvironmentService environmentService,
        ITableManagementService? tableService = null,
        ITableSignalRClient? signalRClient = null)
    {
        _environmentService = environmentService;
        _tableService = tableService;
        _signalRClient = signalRClient;

        LoadDefaultTables();

        if (_signalRClient != null)
        {
            _signalRClient.OnTableStatusChanged += HandleTableStatusChanged;
            _ = ConnectSignalRAsync();
        }
    }

    private async Task ConnectSignalRAsync()
    {
        if (_signalRClient != null)
        {
            try
            {
                // Typical local dev URL, change depending on device/emulator
                var hubUrl = "http://localhost:5247";
                await _signalRClient.ConnectAsync(hubUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SignalR Connection failed: {ex.Message}");
            }
        }
    }

    private void HandleTableStatusChanged(string tableNumber, TableStatus newStatus)
    {
        var table = Tables.FirstOrDefault(t => t.TableNumber == tableNumber);
        if (table != null)
        {
            var updateAction = () =>
            {
                table.Status = newStatus;
                // Force UI update if needed, ObservableCollection only detects adds/removes
                // With ObservableObject/ObservableProperty we usually need to notify property changed.
                // But replacing the item in the collection works best for MAUI.
                var index = Tables.IndexOf(table);
                Tables[index] = table;
            };

#if MAUI_UI
            MainThread.BeginInvokeOnMainThread(updateAction);
#else
            updateAction();
#endif
        }
    }

    [RelayCommand]
    public async Task SelectTableAsync(DiningTable table)
    {
        SelectedTable = table;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);

        if (table.Status == TableStatus.Free)
        {
            CoversToOpen = table.Capacity;
            IsTablePromptOpen = true;
        }
        else
        {
            IsTablePromptOpen = false;
        }

        await Task.CompletedTask;
    }

    [RelayCommand]
    public async Task ConfirmOpenTableAsync()
    {
        if (SelectedTable is not null)
        {
            if (_tableService is not null)
            {
                var dto = await _tableService.OpenTableAsync(
                    SelectedTable.TableNumber,
                    CoversToOpen,
                    Guid.NewGuid(),
                    "Alexandre Dupont"
                ).ConfigureAwait(false);

                SelectedTable.Status = dto.Status;
                SelectedTable.CoversCount = dto.CoversCount;
                SelectedTable.ActiveOrderId = dto.ActiveOrderId;
            }
            else
            {
                SelectedTable.Status = TableStatus.Occupied;
                SelectedTable.CoversCount = CoversToOpen;
                SelectedTable.ActiveOrderId = Guid.NewGuid();
            }

            IsTablePromptOpen = false;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);

            if (_signalRClient != null)
            {
                _ = _signalRClient.UpdateTableStatusAsync(SelectedTable.TableNumber, SelectedTable.Status);
            }
        }
    }

    [RelayCommand]
    public void CancelTablePrompt()
    {
        IsTablePromptOpen = false;
    }

    [RelayCommand]
    public void SetTableBillRequested(DiningTable table)
    {
        table.Status = TableStatus.BillRequested;
        var index = Tables.IndexOf(table);
        Tables[index] = table; // Trigger UI update

        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);

        if (_signalRClient != null)
        {
            _ = _signalRClient.UpdateTableStatusAsync(table.TableNumber, table.Status);
        }
    }

    private void LoadDefaultTables()
    {
        Tables.Add(new DiningTable { TableNumber = "T01", Capacity = 2, PositionX = 40, PositionY = 40, Status = TableStatus.Occupied, CoversCount = 2, AssignedWaiterName = "Alexandre" });
        Tables.Add(new DiningTable { TableNumber = "T02", Capacity = 4, PositionX = 180, PositionY = 40, Status = TableStatus.Free });
        Tables.Add(new DiningTable { TableNumber = "T03", Capacity = 6, PositionX = 340, PositionY = 40, Status = TableStatus.BillRequested, CoversCount = 5, AssignedWaiterName = "Sophie" });
        Tables.Add(new DiningTable { TableNumber = "T04", Capacity = 4, PositionX = 40, PositionY = 180, Status = TableStatus.Free });
        Tables.Add(new DiningTable { TableNumber = "T05", Capacity = 8, PositionX = 180, PositionY = 180, Status = TableStatus.Free });
        Tables.Add(new DiningTable { TableNumber = "Bar 01", Capacity = 2, PositionX = 340, PositionY = 180, Status = TableStatus.Free });
    }
}
