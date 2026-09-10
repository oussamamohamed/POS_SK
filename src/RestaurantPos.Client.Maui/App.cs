#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Client.Maui.Views;

namespace RestaurantPos.Client.Maui;

/// <summary>
/// Point d'entree de l'application MAUI RestaurantPos.
/// Initialise le Shell de navigation et demarre sur la page PIN.
/// </summary>
public class App : Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        MainPage = serviceProvider.GetRequiredService<AppShell>();
    }
}
#endif
