using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// No-op fake for <see cref="IPlatformEnvironmentService"/>.
/// Records all haptic feedback calls in <see cref="HapticCalls"/> for assertion.
/// </summary>
public sealed class FakePlatformEnvironmentService : IPlatformEnvironmentService
{
    /// <summary>All haptic feedback calls received, in order.</summary>
    public List<HapticFeedbackType> HapticCalls { get; } = [];

    public void TriggerHapticFeedback(HapticFeedbackType feedbackType)
        => HapticCalls.Add(feedbackType);

    public DevicePlatformProfile GetCurrentDeviceProfile() =>
        new()
        {
            DeviceId = "SIM-TEST-DEVICE",
            Platform = PlatformType.Windows,
            Idiom = DeviceIdiomType.Desktop,
            ScreenClass = ScreenClassType.LargeCounterAIO,
            ScreenWidthDip = 1920,
            ScreenHeightDip = 1080,
            DisplayDensity = 1.0,
            OsVersion = "11.0",
            AppVersion = "1.0.0-sim"
        };


    public string GetSecureDatabasePath(string databaseName)
        => $"/tmp/{databaseName}.db";

    public Task<bool> EnsureLocalNetworkPermissionsAsync()
        => Task.FromResult(true);

    public void PreventScreenSleep(bool keepAwake) { }
}
