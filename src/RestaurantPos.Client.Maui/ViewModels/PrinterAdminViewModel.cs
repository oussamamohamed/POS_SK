using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class PrinterAdminViewModel : ObservableObject
{
    private readonly IPrinterConfigurationService _printerService;
    private readonly ICrossPlatformDiscoveryService _discoveryService;
    private readonly IPlatformEnvironmentService _environmentService;

    [ObservableProperty]
    private ObservableCollection<PrinterConfiguration> _printers = [];

    [ObservableProperty]
    private PrinterConfiguration? _selectedPrinter;

    [ObservableProperty]
    private string _newPrinterName = string.Empty;

    [ObservableProperty]
    private string _newPrinterIp = string.Empty;

    [ObservableProperty]
    private int _newPrinterPort = 9100;

    [ObservableProperty]
    private int _newPrinterPaperWidth = 80;

    [ObservableProperty]
    private bool _newPrinterOpenDrawer;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public PrinterAdminViewModel(
        IPrinterConfigurationService printerService,
        ICrossPlatformDiscoveryService discoveryService,
        IPlatformEnvironmentService environmentService)
    {
        _printerService = printerService;
        _discoveryService = discoveryService;
        _environmentService = environmentService;
    }

    [RelayCommand]
    public async Task LoadPrintersAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _printerService.GetAllPrintersAsync();
            Printers.Clear();
            foreach (var p in list)
            {
                Printers.Add(p);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task DiscoverPrintersAsync()
    {
        IsLoading = true;
        StatusMessage = "Recherche d'imprimantes sur le réseau local...";
        try
        {
            var discovered = await _discoveryService.DiscoverPeripheralsAsync(TimeSpan.FromSeconds(3));
            StatusMessage = $"{discovered.Count} périphérique(s) trouvé(s).";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Erreur découverte mDNS: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task RegisterPrinterAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(NewPrinterName) || string.IsNullOrWhiteSpace(NewPrinterIp))
        {
            ErrorMessage = "Le nom et l'adresse IP sont requis.";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
            return;
        }

        var req = new PrinterRegistrationRequest(
            NewPrinterName,
            NewPrinterIp,
            NewPrinterPort,
            NewPrinterPaperWidth,
            NewPrinterOpenDrawer,
            ["RECEIPT", "HOT_KITCHEN"]
        );

        try
        {
            var printer = await _printerService.RegisterPrinterAsync(req);
            Printers.Add(printer);
            SelectedPrinter = printer;
            NewPrinterName = string.Empty;
            NewPrinterIp = string.Empty;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
            StatusMessage = $"Imprimante '{printer.Name}' enregistrée.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
        }
    }

    [RelayCommand]
    public async Task TestPrintAsync(PrinterConfiguration printer)
    {
        IsLoading = true;
        StatusMessage = $"Envoi du test à {printer.Name}...";
        try
        {
            var result = await _printerService.SendTestPrintAsync(printer.Id);
            if (result.Success)
            {
                StatusMessage = $"Succès: {result.Message}";
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
            }
            else
            {
                ErrorMessage = result.Message;
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }
}
