import re

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'r') as f:
    content = f.read()

# Add SetApplicationTheme
if 'void SetApplicationTheme' not in content:
    theme_method = """    public void SetApplicationTheme(Microsoft.Maui.ApplicationModel.AppTheme theme)
    {
        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = theme;
        }
    }

    private static DevicePlatformProfile"""
    content = content.replace("    private static DevicePlatformProfile", theme_method)

# Update TriggerHapticFeedback
haptic_impl = """    public void TriggerHapticFeedback(Contracts.HapticFeedbackType feedbackType)
    {
        try
        {
            if (Microsoft.Maui.Devices.HapticFeedback.Default.IsSupported)
            {
                var mauiType = feedbackType switch
                {
                    Contracts.HapticFeedbackType.LongPress => Microsoft.Maui.Devices.HapticFeedbackType.LongPress,
                    _ => Microsoft.Maui.Devices.HapticFeedbackType.Click
                };
                Microsoft.Maui.Devices.HapticFeedback.Default.Perform(mauiType);
            }
        }
        catch
        {
            // Ignore haptic failures
        }
    }"""
content = re.sub(r'public void TriggerHapticFeedback.*?\{.*?\}', haptic_impl, content, flags=re.DOTALL)

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'w') as f:
    f.write(content)
