import re

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'r') as f:
    content = f.read()

# I will add Haptic feedback calls in OpenModifiersModal and also ensure multiple tap gest can open it.
# Wait, let's keep the existing logic and add Haptics when tapping the product.
haptic_code = """
            tap.Tapped += (s, e) =>
            {
                #if MAUI_UI
                Microsoft.Maui.Devices.HapticFeedback.Default.Perform(Microsoft.Maui.Devices.HapticFeedbackType.Click);
                #endif
                if (_vm.HasModifiers(prod))
"""
content = re.sub(r'tap\.Tapped \+= \(s,\s*e\) =>\s*\{\s*if \(_vm\.HasModifiers\(prod\)\)', haptic_code, content)

with open('src/RestaurantPos.Client.Maui/Views/PosTerminalPage.cs', 'w') as f:
    f.write(content)
