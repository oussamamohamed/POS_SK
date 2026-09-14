using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class StaffAdminViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<User> _staffMembers = [];

    [ObservableProperty]
    private User? _selectedStaff;

    [ObservableProperty]
    private string _newStaffName = string.Empty;

    /// <summary>Alias court pour la page View.</summary>
    public string NewMemberName
    {
        get => NewStaffName;
        set => NewStaffName = value;
    }

    [ObservableProperty]
    private UserRole _newStaffRole = UserRole.Waiter;

    [ObservableProperty]
    private string _newStaffPin = string.Empty;

    /// <summary>Alias court pour la page View.</summary>
    public string NewMemberPin
    {
        get => NewStaffPin;
        set => NewStaffPin = value;
    }

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>Controle la visibilite du formulaire de creation dans la page admin.</summary>
    [ObservableProperty]
    private bool _isFormVisible;

    private readonly IStaffManagementService? _staffService;
    private readonly IPlatformEnvironmentService _environmentService;

    public StaffAdminViewModel(IPlatformEnvironmentService environmentService)
        : this(null, environmentService)
    {
    }

    public StaffAdminViewModel(
        IStaffManagementService? staffService,
        IPlatformEnvironmentService environmentService)
    {
        _staffService = staffService;
        _environmentService = environmentService;
    }

    [RelayCommand]
    public async Task LoadStaffAsync()
    {
        IsLoading = true;
        try
        {
            StaffMembers.Clear();
            if (_staffService != null)
            {
                var list = await _staffService.GetAllStaffAsync(includeInactive: true);
                foreach (var user in list)
                {
                    StaffMembers.Add(user);
                }
            }

            if (StaffMembers.Count == 0)
            {
                StaffMembers.Add(new User { Name = "Alexandre Dupont", Role = UserRole.Waiter, IsActive = true, PinHash = "1234", PinSalt = "salt" });
                StaffMembers.Add(new User { Name = "Chef Michel", Role = UserRole.KitchenStaff, IsActive = true, PinHash = "5678", PinSalt = "salt" });
                StaffMembers.Add(new User { Name = "Directrice Sophie", Role = UserRole.FloorManager, IsActive = true, PinHash = "9999", PinSalt = "salt" });
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ShowCreateForm()
    {
        NewStaffName = string.Empty;
        NewStaffPin = string.Empty;
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
        IsFormVisible = true;
    }

    [RelayCommand]
    public void HideForm()
    {
        IsFormVisible = false;
        NewStaffName = string.Empty;
        NewStaffPin = string.Empty;
    }

    [RelayCommand]
    public async Task CreateStaffAsync()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(NewStaffName))
        {
            ErrorMessage = "Le nom de l'employe est requis.";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(NewStaffPin) || NewStaffPin.Length < 4)
        {
            ErrorMessage = "Le code PIN doit comporter 4 à 6 chiffres.";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
            return;
        }

        try
        {
            User user;
            if (_staffService != null)
            {
                user = await _staffService.CreateStaffMemberAsync(NewStaffName, NewStaffRole, NewStaffPin);
            }
            else
            {
                user = new User
                {
                    Name = NewStaffName,
                    Role = NewStaffRole,
                    PinHash = NewStaffPin,
                    PinSalt = "salt",
                    IsActive = true
                };
            }

            StaffMembers.Add(user);
            SelectedStaff = user;
            NewStaffName = string.Empty;
            NewStaffPin = string.Empty;
            IsFormVisible = false;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
            StatusMessage = $"Operateur '{user.Name}' cree avec succes.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
        }
    }

    [RelayCommand]
    public async Task DeactivateStaffAsync(User user)
    {
        if (_staffService != null)
        {
            await _staffService.DeactivateStaffMemberAsync(user.Id);
        }
        user.IsActive = false;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        StatusMessage = $"Operateur '{user.Name}' desactive.";
        await LoadStaffAsync();
    }
}
