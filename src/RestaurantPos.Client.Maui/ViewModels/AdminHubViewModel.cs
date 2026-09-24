using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.ViewModels;

public enum AdminSection
{
    Catalog = 0,
    Staff = 1,
    Printers = 2,
    Layout = 3,
    NetworkSync = 4,
    Dashboard = 5,
    HappyHour = 6
}

public partial class AdminHubViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;

    [ObservableProperty]
    private AdminSection _currentSection = AdminSection.Catalog;

    [ObservableProperty]
    private bool _isManagerAuthenticated = true;

    [ObservableProperty]
    private string _masterServerUrl = "http://127.0.0.1:5000";

    [ObservableProperty]
    private string _syncStatus = "Connecté au Réseau (Edge)";

    [ObservableProperty]
    private int _outboxCount = 0;

    [ObservableProperty]
    private int _syncedCount = 32;

    [ObservableProperty]
    private string _kpiSalesTtc = "1 248,50 €";

    [ObservableProperty]
    private string _kpiSalesHt = "HT : 1 125,00 €";

    [ObservableProperty]
    private string _kpiAvgCover = "24,97 €";

    [ObservableProperty]
    private string _kpiTotalCovers = "50 couverts servis";

    [ObservableProperty]
    private string _kpiAvgOrder = "41,62 €";

    [ObservableProperty]
    private string _kpiTotalOrders = "30 commande(s)";

    [ObservableProperty]
    private string _selectedGridPreset = "4x4";

    [ObservableProperty]
    private string _activeHappyHourSchedule = "Afterwork (17h00 - 20h00) — Actif (-20%)";

    public CatalogAdminViewModel CatalogVm { get; }
    public StaffAdminViewModel StaffVm { get; }
    public PrinterAdminViewModel PrinterVm { get; }
    public LayoutAdminViewModel LayoutVm { get; }

    public AdminHubViewModel(
        IPlatformEnvironmentService environmentService,
        CatalogAdminViewModel catalogVm,
        StaffAdminViewModel staffVm,
        PrinterAdminViewModel printerVm,
        LayoutAdminViewModel layoutVm)
    {
        _environmentService = environmentService;
        CatalogVm = catalogVm;
        StaffVm = staffVm;
        PrinterVm = printerVm;
        LayoutVm = layoutVm;
    }

    [RelayCommand]
    public void SwitchSection(AdminSection section)
    {
        CurrentSection = section;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }
}
