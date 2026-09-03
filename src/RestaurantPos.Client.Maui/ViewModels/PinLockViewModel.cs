using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class PinLockViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly IOperatorAuthenticationService? _authService;

    [ObservableProperty]
    private string _pinInput = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private string? _currentOperatorName;

    [ObservableProperty]
    private UserRole? _currentOperatorRole;

    public PinLockViewModel(
        IPlatformEnvironmentService environmentService,
        IOperatorAuthenticationService? authService = null)
    {
        _environmentService = environmentService;
        _authService = authService;
    }

    [RelayCommand]
    public async Task AppendDigitAsync(string digit)
    {
        if (PinInput.Length < 6)
        {
            PinInput += digit;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);

            if (PinInput.Length >= 4)
            {
                await ValidatePinAsync().ConfigureAwait(false);
            }
        }
    }

    [RelayCommand]
    public void ClearPin()
    {
        PinInput = string.Empty;
        ErrorMessage = string.Empty;
    }

    [RelayCommand]
    public void DeleteDigit()
    {
        if (PinInput.Length > 0)
        {
            PinInput = PinInput[..^1];
        }
    }

    public async Task<bool> ValidatePinAsync()
    {
        if (_authService is not null)
        {
            var result = await _authService.AuthenticatePinAsync(PinInput).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                IsAuthenticated = true;
                CurrentOperatorName = result.OperatorName;
                CurrentOperatorRole = result.Role;
                ErrorMessage = string.Empty;
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
                return true;
            }
        }
        else
        {
            // Default demo/fallback offline PIN validation
            if (PinInput == "1234" || PinInput == "0000")
            {
                IsAuthenticated = true;
                CurrentOperatorName = "Alexandre Dupont";
                CurrentOperatorRole = UserRole.Waiter;
                ErrorMessage = string.Empty;
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
                return true;
            }
        }

        ErrorMessage = "Code PIN invalide";
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
        PinInput = string.Empty;
        return false;
    }
}
