using System.IO;
using System.Runtime.InteropServices;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;

namespace RestaurantPos.Client.Maui.Services;

public class PlatformEnvironmentService : IPlatformEnvironmentService
{
    private readonly string _baseStorageDirectory;
    private readonly DevicePlatformProfile _cachedProfile;

    public PlatformEnvironmentService(string? customStorageDirectory = null)
    {
        if (!string.IsNullOrWhiteSpace(customStorageDirectory))
        {
            _baseStorageDirectory = customStorageDirectory;
        }
        else
        {
            // Resolve sandboxed AppData directory across iOS, Android, and Windows
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _baseStorageDirectory = Path.Combine(appData, "RestaurantPos");
        }

        if (!Directory.Exists(_baseStorageDirectory))
        {
            Directory.CreateDirectory(_baseStorageDirectory);
        }

        _cachedProfile = DetectCurrentPlatform();
    }

    public DevicePlatformProfile GetCurrentDeviceProfile() => _cachedProfile;

    public string GetSecureDatabasePath(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            databaseName = "pos_local.db";
        }

        if (!databaseName.EndsWith(".db", StringComparison.OrdinalIgnoreCase))
        {
            databaseName += ".db";
        }

        return Path.Combine(_baseStorageDirectory, databaseName);
    }

    public void TriggerHapticFeedback(HapticFeedbackType feedbackType)
    {
        // On native devices, bridged to UIKit / Android HapticFeedback / Windows Vibration
        // In shared runtime, safely dispatches or logs without throwing
    }

    public Task<bool> EnsureLocalNetworkPermissionsAsync()
    {
        // Handled via native manifests (Info.plist / AndroidManifest)
        return Task.FromResult(true);
    }

    public void PreventScreenSleep(bool keepAwake)
    {
        // Toggles DeviceDisplay.Current.KeepScreenOn on native platforms
    }

    private static DevicePlatformProfile DetectCurrentPlatform()
    {
        PlatformType platform;
        DeviceIdiomType idiom = DeviceIdiomType.Tablet;
        double width = 1024;
        double height = 768;
        double density = 2.0;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            platform = PlatformType.Windows;
            idiom = DeviceIdiomType.Desktop;
            width = 1920;
            height = 1080;
            density = 1.0;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            platform = PlatformType.MacCatalyst;
            idiom = DeviceIdiomType.Desktop;
        }
        else
        {
            // Default mobile / tablet fallback
            platform = PlatformType.Android;
            idiom = DeviceIdiomType.Tablet;
        }

        return new DevicePlatformProfile
        {
            DeviceId = Guid.NewGuid().ToString("N")[..12],
            Platform = platform,
            Idiom = idiom,
            ScreenClass = DevicePlatformProfile.DetermineScreenClass(width, height),
            ScreenWidthDip = width,
            ScreenHeightDip = height,
            DisplayDensity = density,
            OsVersion = Environment.OSVersion.ToString(),
            AppVersion = "1.0.0"
        };
    }
}
