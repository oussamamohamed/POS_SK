#if MAUI_UI
using Microsoft.Maui.Controls;
using RestaurantPos.Client.Maui.Views;
using RestaurantPos.Client.Maui.Views.Admin;

namespace RestaurantPos.Client.Maui;

/// <summary>
/// Shell de navigation principal de l'application POS.
/// Gere le routage entre les differentes vues (PIN, Salle, Caisse, KDS, Admin).
/// </summary>
public class AppShell : Shell
{
    public AppShell()
    {
        FlyoutBehavior = FlyoutBehavior.Disabled;
        Shell.SetNavBarIsVisible(this, false);

        // Routes secondaires / modales
        Routing.RegisterRoute("modifiers", typeof(ModifiersPopupPage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("split", typeof(SplitBillPage));

        var pinContent = new ShellContent
        {
            Title = "PIN",
            Route = "pin",
            ContentTemplate = new DataTemplate(typeof(PinLockPage))
        };

        var floorContent = new ShellContent
        {
            Title = "Plan de Salle",
            Route = "floor",
            ContentTemplate = new DataTemplate(typeof(FloorPlanPage))
        };

        var posContent = new ShellContent
        {
            Title = "Caisse",
            Route = "pos",
            ContentTemplate = new DataTemplate(typeof(PosTerminalPage))
        };

        var kdsContent = new ShellContent
        {
            Title = "Cuisine KDS",
            Route = "kds",
            ContentTemplate = new DataTemplate(typeof(KitchenKdsPage))
        };

        var adminContent = new ShellContent
        {
            Title = "Administration",
            Route = "admin",
            ContentTemplate = new DataTemplate(typeof(AdminShellPage))
        };

        Items.Add(pinContent);
        Items.Add(floorContent);
        Items.Add(posContent);
        Items.Add(kdsContent);
        Items.Add(adminContent);

        CurrentItem = pinContent;
    }
}
#endif
