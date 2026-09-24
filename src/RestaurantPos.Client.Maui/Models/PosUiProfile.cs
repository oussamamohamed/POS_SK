namespace RestaurantPos.Client.Maui.Models;

public enum HandednessMode
{
    RightHanded = 0,
    LeftHanded = 1
}

public class PosUiProfile
{
    public HandednessMode HandednessMode { get; set; } = HandednessMode.RightHanded;
    #if MAUI_UI
    public Microsoft.Maui.ApplicationModel.AppTheme ThemePreference { get; set; } = Microsoft.Maui.ApplicationModel.AppTheme.Unspecified;
#endif
}
