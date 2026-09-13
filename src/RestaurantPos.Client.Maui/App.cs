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
public class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider _serviceProvider;

    public App(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        try
        {
            using var scope = serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Persistence.LocalAppDbContext>();
            db.Database.EnsureCreated();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Local DB creation error: {ex}");
        }

        MainPage = serviceProvider.GetRequiredService<AppShell>();
    }

    protected override void OnStart()
    {
        base.OnStart();

        var args = Environment.GetCommandLineArgs();
        var hasArg = args != null && args.Any(a => a.Equals("--auto-test", StringComparison.OrdinalIgnoreCase));
        var autoTestEnv = Environment.GetEnvironmentVariable("POS_AUTO_TEST");

        if (hasArg || autoTestEnv == "1" || autoTestEnv?.Equals("true", StringComparison.OrdinalIgnoreCase) == true)
        {
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                await Services.SimulatorAutoTestRunner.RunAllTestsAsync(_serviceProvider);
            });
        }
    }
}
#endif
