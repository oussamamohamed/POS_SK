import re

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'r') as f:
    content = f.read()

# Completely re-write TriggerHapticFeedback body
trigger = """    public void TriggerHapticFeedback(Contracts.HapticFeedbackType feedbackType)
    {
        try
        {
#if MAUI_UI
            if (Microsoft.Maui.Devices.HapticFeedback.Default.IsSupported)
            {
                var mauiType = feedbackType switch
                {
                    Contracts.HapticFeedbackType.LongPress => Microsoft.Maui.Devices.HapticFeedbackType.LongPress,
                    _ => Microsoft.Maui.Devices.HapticFeedbackType.Click
                };
                Microsoft.Maui.Devices.HapticFeedback.Default.Perform(mauiType);
            }
#endif
        }
        catch
        {
            // Ignore haptic failures
        }
    }"""
content = re.sub(r'public void TriggerHapticFeedback.*?catch\s*\{\s*// Ignore haptic failures\s*\}', trigger, content, flags=re.DOTALL)
# clean up stray closing brace since my re.sub missed the closing brace of the method
content = re.sub(r'public void TriggerHapticFeedback.*?public Task<bool> EnsureLocalNetworkPermissionsAsync', trigger + "\n\n    public Task<bool> EnsureLocalNetworkPermissionsAsync", content, flags=re.DOTALL)

with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'w') as f:
    f.write(content)

