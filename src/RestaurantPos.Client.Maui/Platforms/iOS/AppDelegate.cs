using System.Diagnostics.CodeAnalysis;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Hosting;

namespace RestaurantPos.Client.Maui;

[Register("AppDelegate")]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "Standard iOS AppDelegate class name")]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
