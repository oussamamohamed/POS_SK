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
    Layout = 3
}

public partial class AdminHubViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;

    [ObservableProperty]
    private AdminSection _currentSection = AdminSection.Catalog;

    [ObservableProperty]
    private bool _isManagerAuthenticated;

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
