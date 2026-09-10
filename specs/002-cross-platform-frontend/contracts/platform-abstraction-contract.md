# Contract: Cross-Platform Device & Environment Abstractions

**Feature**: `002-cross-platform-frontend`
**Target Platforms**: iOS/iPadOS 17+, Android 10+ (API 29+), Windows 10/11 (WinUI 3)

## 1. Platform Service Abstraction Interface

```csharp
namespace RestaurantPos.Client.Maui.Contracts;

public interface IPlatformEnvironmentService
{
    DevicePlatformProfile GetCurrentDeviceProfile();
    
    string GetSecureDatabasePath(string databaseName);
    
    void TriggerHapticFeedback(HapticFeedbackType feedbackType);
    
    Task<bool> EnsureLocalNetworkPermissionsAsync();
    
    void PreventScreenSleep(bool keepAwake);
}

public enum HapticFeedbackType
{
    LightTap = 0,
    Success = 1,
    Warning = 2,
    Error = 3
}
```

---

## 2. Platform Manifest Declarations

### iOS / iPadOS (`Platforms/iOS/Info.plist`)
```xml
<key>NSLocalNetworkUsageDescription</key>
<string>POS terminal needs local network access to communicate with receipt printers and the central server.</string>
<key>NSBonjourServices</key>
<array>
    <string>_pos-server._tcp</string>
    <string>_printer._tcp</string>
</array>
<key>UISupportedInterfaceOrientations~ipad</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
    <string>UIInterfaceOrientationPortrait</string>
</array>
```

### Android (`Platforms/Android/AndroidManifest.xml`)
```xml
<uses-permission android:name="android.permission.INTERNET" />
<uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
<uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
<uses-permission android:name="android.permission.CHANGE_WIFI_MULTICAST_STATE" />
<uses-permission android:name="android.permission.NEARBY_WIFI_DEVICES" />
<uses-feature android:name="android.hardware.touchscreen" android:required="true" />
```

### Windows (`Platforms/Windows/Package.appxmanifest`)
```xml
<Capabilities>
    <Capability Name="internetClient" />
    <Capability Name="privateNetworkClientServer" />
</Capabilities>
```
