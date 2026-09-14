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

        Routing.RegisterRoute("modifiers", typeof(ModifiersPopupPage));
        Routing.RegisterRoute("checkout", typeof(CheckoutPage));
        Routing.RegisterRoute("split", typeof(SplitBillPage));
        Routing.RegisterRoute("admin", typeof(AdminShellPage));
        Routing.RegisterRoute("fiscal", typeof(FiscalPage));

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

        var fiscalContent = new ShellContent
        {
            Title = "Fiscalité NF525",
            Route = "fiscal",
            ContentTemplate = new DataTemplate(typeof(FiscalPage))
        };

        Items.Add(pinContent);
        Items.Add(floorContent);
        Items.Add(posContent);
        Items.Add(kdsContent);
        Items.Add(fiscalContent);

        CurrentItem = pinContent;
    }
}
#endif
