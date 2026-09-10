using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class LayoutAdminViewModel : ObservableObject
{
    private readonly ITerminalLayoutService _layoutService;
    private readonly IPlatformEnvironmentService _environmentService;

    [ObservableProperty]
    private ObservableCollection<TerminalLayoutProfile> _profiles = [];

    [ObservableProperty]
    private TerminalLayoutProfile? _selectedProfile;

    [ObservableProperty]
    private string _profileName = string.Empty;

    [ObservableProperty]
    private int _gridColumnCount = 4;

    [ObservableProperty]
    private string _defaultLandingView = "SalesTerminal";

    [ObservableProperty]
    private bool _isDefault;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public LayoutAdminViewModel(
        ITerminalLayoutService layoutService,
        IPlatformEnvironmentService environmentService)
    {
        _layoutService = layoutService;
        _environmentService = environmentService;
    }

    [RelayCommand]
    public async Task LoadProfilesAsync()
    {
        IsLoading = true;
        try
        {
            var list = await _layoutService.GetAllProfilesAsync();
            Profiles.Clear();
            foreach (var p in list)
            {
                Profiles.Add(p);
            }

            if (SelectedProfile is null && Profiles.Count > 0)
            {
                SelectProfile(Profiles[0]);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void SelectProfile(TerminalLayoutProfile profile)
    {
        SelectedProfile = profile;
        ProfileName = profile.ProfileName;
        GridColumnCount = profile.GridColumnCount;
        DefaultLandingView = profile.DefaultLandingView;
        IsDefault = profile.IsDefault;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public async Task SaveCurrentProfileAsync()
    {
        if (string.IsNullOrWhiteSpace(ProfileName)) return;

        var profile = SelectedProfile ?? new TerminalLayoutProfile
        {
            ProfileName = ProfileName
        };

        profile.ProfileName = ProfileName;
        profile.GridColumnCount = GridColumnCount;
        profile.DefaultLandingView = DefaultLandingView;
        profile.IsDefault = IsDefault;

        var saved = await _layoutService.SaveProfileAsync(profile);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
        StatusMessage = $"Profil '{saved.ProfileName}' enregistré avec succès.";
        await LoadProfilesAsync();
    }
}
