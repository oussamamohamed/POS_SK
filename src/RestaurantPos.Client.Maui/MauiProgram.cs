using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Client.Maui.Services;
using RestaurantPos.Client.Maui.ViewModels;
#if MAUI_UI
using Microsoft.Maui.Controls;
using RestaurantPos.Client.Maui.Views;
using RestaurantPos.Client.Maui.Views.Admin;
#endif

namespace RestaurantPos.Client.Maui;

public static class MauiProgram
{
    public static IServiceCollection ConfigureServices(IServiceCollection? services = null)
    {
        services ??= new ServiceCollection();

        // Platform & Core Services
        services.AddSingleton<IPlatformEnvironmentService, PlatformEnvironmentService>();
        services.AddSingleton<ICrossPlatformDiscoveryService, CrossPlatformDiscoveryService>();
        services.AddSingleton<IPrinterClient, NetworkPrinterClient>();
        services.AddSingleton<ILocalJournalService, LocalJournalService>();
        services.AddSingleton<ILocalSyncWorker, LocalSyncWorker>();
        services.AddSingleton<IKitchenSignalRClient, KitchenSignalRClient>();
        services.AddSingleton<ITableSignalRClient, TableSignalRClient>();

        // Local SQLite Persistence
        services.AddDbContext<LocalAppDbContext>();

        // ViewModels
        services.AddTransient<PinLockViewModel>();
        services.AddTransient<PosTerminalViewModel>();
        services.AddTransient<FloorPlanViewModel>();
        services.AddTransient<KdsViewModel>();
        services.AddTransient<CheckoutViewModel>();
        services.AddTransient<SplitBillViewModel>();
        services.AddTransient<ModifiersViewModel>();
        services.AddTransient<PeripheralSetupViewModel>();
        services.AddTransient<CatalogAdminViewModel>();
        services.AddTransient<StaffAdminViewModel>();
        services.AddTransient<PrinterAdminViewModel>();
        services.AddTransient<LayoutAdminViewModel>();
        services.AddTransient<AdminHubViewModel>(sp => new AdminHubViewModel(
            sp.GetRequiredService<IPlatformEnvironmentService>(),
            sp.GetRequiredService<CatalogAdminViewModel>(),
            sp.GetRequiredService<StaffAdminViewModel>(),
            sp.GetRequiredService<PrinterAdminViewModel>(),
            sp.GetRequiredService<LayoutAdminViewModel>()
        ));

#if MAUI_UI
        // Pages MAUI
        services.AddTransient<AppShell>();
        services.AddTransient<PinLockPage>();
        services.AddTransient<FloorPlanPage>();
        services.AddTransient<PosTerminalPage>();
        services.AddTransient<ModifiersPopupPage>();
        services.AddTransient<CheckoutPage>();
        services.AddTransient<SplitBillPage>();
        services.AddTransient<KitchenKdsPage>();

        // Pages Admin
        services.AddTransient<CatalogAdminPage>();
        services.AddTransient<StaffAdminPage>();
        services.AddTransient<AdminShellPage>();

        // Application MAUI
        services.AddSingleton<App>();
#endif

        return services;
    }

#if MAUI_UI
    /// <summary>
    /// Point d'entree MAUI — configure le builder et enregistre les services.
    /// </summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        ConfigureServices(builder.Services);

        return builder.Build();
    }
#endif
}
