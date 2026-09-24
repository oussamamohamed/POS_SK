import re

def wrap_maui(filepath):
    try:
        with open(filepath, 'r') as f:
            content = f.read()
        if '#if MAUI_UI' not in content:
            content = "#if MAUI_UI\n" + content + "\n#endif"
            with open(filepath, 'w') as f:
                f.write(content)
    except FileNotFoundError:
        pass

wrap_maui('src/RestaurantPos.Client.Maui/Views/Components/NumericKeypad.cs')

def fix_enum_and_contract():
    with open('src/RestaurantPos.Client.Maui/Contracts/IPlatformEnvironmentService.cs', 'r') as f:
        content = f.read()
    if 'void SetApplicationTheme' in content:
        content = content.replace("void SetApplicationTheme(Microsoft.Maui.ApplicationModel.AppTheme theme);", """#if MAUI_UI\n    void SetApplicationTheme(Microsoft.Maui.ApplicationModel.AppTheme theme);\n#endif""")
    with open('src/RestaurantPos.Client.Maui/Contracts/IPlatformEnvironmentService.cs', 'w') as f:
        f.write(content)

fix_enum_and_contract()

def fix_profile():
    with open('src/RestaurantPos.Client.Maui/Models/PosUiProfile.cs', 'r') as f:
        content = f.read()
    if 'Microsoft.Maui.ApplicationModel.AppTheme' in content:
        content = content.replace("public Microsoft.Maui.ApplicationModel.AppTheme ThemePreference { get; set; } = Microsoft.Maui.ApplicationModel.AppTheme.System;", """#if MAUI_UI\n    public Microsoft.Maui.ApplicationModel.AppTheme ThemePreference { get; set; } = Microsoft.Maui.ApplicationModel.AppTheme.System;\n#endif""")
    with open('src/RestaurantPos.Client.Maui/Models/PosUiProfile.cs', 'w') as f:
        f.write(content)
        
fix_profile()

def fix_service():
    with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'r') as f:
        content = f.read()
    content = content.replace("public void SetApplicationTheme(Microsoft.Maui.ApplicationModel.AppTheme theme)", "#if MAUI_UI\n    public void SetApplicationTheme(Microsoft.Maui.ApplicationModel.AppTheme theme)")
    content = content.replace("            Application.Current.UserAppTheme = theme;\n        }\n    }", "            Application.Current.UserAppTheme = theme;\n        }\n    }\n#endif")
    content = content.replace("Microsoft.Maui.Devices.HapticFeedback.Default.IsSupported", "#if MAUI_UI\n            if (Microsoft.Maui.Devices.HapticFeedback.Default.IsSupported)")
    content = content.replace("Microsoft.Maui.Devices.HapticFeedback.Default.Perform(mauiType);\n            }", "Microsoft.Maui.Devices.HapticFeedback.Default.Perform(mauiType);\n            }\n#endif")
    with open('src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs', 'w') as f:
        f.write(content)

fix_service()

