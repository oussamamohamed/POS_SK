using RestaurantPos.Client.Maui.Models;

namespace RestaurantPos.Client.Maui.Contracts;

public enum HapticFeedbackType
{
    LightTap = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}

public interface IPlatformEnvironmentService
{
    DevicePlatformProfile GetCurrentDeviceProfile();
    string GetSecureDatabasePath(string databaseName);
    void TriggerHapticFeedback(HapticFeedbackType feedbackType);
    Task<bool> EnsureLocalNetworkPermissionsAsync();
    void PreventScreenSleep(bool keepAwake);
}
