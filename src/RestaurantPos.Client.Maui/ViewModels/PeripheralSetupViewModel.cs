using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class PeripheralSetupViewModel : ObservableObject
{
    private readonly ICrossPlatformDiscoveryService _discoveryService;
    private readonly IPrinterClient _printerClient;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _testPrintStatus = string.Empty;

    [ObservableProperty]
    private DiscoveredPeripheral? _selectedPrinter;

    public ObservableCollection<DiscoveredPeripheral> DiscoveredPrinters { get; } = [];

    public PeripheralSetupViewModel(
        ICrossPlatformDiscoveryService discoveryService,
        IPrinterClient printerClient)
    {
        _discoveryService = discoveryService;
        _printerClient = printerClient;
    }

    [RelayCommand]
    public async Task ScanNetworkAsync()
    {
        IsScanning = true;
        DiscoveredPrinters.Clear();

        try
        {
            var results = await _discoveryService.DiscoverPeripheralsAsync(TimeSpan.FromSeconds(3));
            foreach (var peripheral in results)
            {
                DiscoveredPrinters.Add(peripheral);
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task TestPrintAsync(DiscoveredPeripheral printer)
    {
        SelectedPrinter = printer;
        TestPrintStatus = "Envoi du test d'impression...";

        byte[] testTicket = [0x1B, 0x40, 0x54, 0x45, 0x53, 0x54, 0x0A, 0x1D, 0x56, 0x42, 0x00];
        bool success = await _printerClient.PrintRawEscPosAsync(printer.IpAddress, printer.Port, testTicket);

        TestPrintStatus = success ? "Test réussi !" : "Échec d'impression";
    }
}
