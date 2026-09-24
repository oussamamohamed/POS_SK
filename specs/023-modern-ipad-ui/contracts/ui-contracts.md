# UI Services & Handlers Contracts

## 1. IPlatformEnvironmentService (Enhancements)
Enhance the existing abstracted environment service to guarantee tactile requirements.

```csharp
namespace RestaurantPos.Client.Maui.Contracts;

public interface IPlatformEnvironmentService
{
    // Existing methods...
    DevicePlatformProfile GetCurrentDeviceProfile();
    
    // NEW METHODS for Modern iPad UI
    
    /// <summary>
    /// Executes a low-latency haptic pulse via the Taptic Engine.
    /// </summary>
    void TriggerHapticFeedback(HapticFeedbackType type = HapticFeedbackType.LightTap);
    
    /// <summary>
    /// Sets the app theme dynamically at runtime.
    /// </summary>
    void SetApplicationTheme(AppTheme theme);
}
```

## 2. Custom Keypad Interface Contract
The contract between the Custom Keypad Component and its hosting ViewModel.

```csharp
public interface INumericKeypadReceiver
{
    void OnDigitPressed(int digit);
    void OnBackspacePressed();
    void OnClearPressed();
    void OnDecimalSeparatorPressed();
    void OnEnterPressed();
}
```

## 3. Info.plist Constants (iOS Specific)
The following contracts must be guaranteed in the iOS deployment target descriptor:

```xml
<key>UIRequiresFullScreen</key>
<true/>
<key>UISupportedInterfaceOrientations</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
</array>
<key>UISupportedInterfaceOrientations~ipad</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
</array>
```
