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
        // Routes nommees pour la navigation programmatique
        Routing.RegisterRoute("pin",        typeof(PinLockPage));
        Routing.RegisterRoute("floor",      typeof(FloorPlanPage));
        Routing.RegisterRoute("pos",        typeof(PosTerminalPage));
        Routing.RegisterRoute("modifiers",  typeof(ModifiersPopupPage));
        Routing.RegisterRoute("checkout",   typeof(CheckoutPage));
        Routing.RegisterRoute("split",      typeof(SplitBillPage));
        Routing.RegisterRoute("kds",        typeof(KitchenKdsPage));
        Routing.RegisterRoute("admin",      typeof(AdminShellPage));

        // Page de demarrage : ecran de verrouillage PIN
        CurrentItem = new ShellContent
        {
            Route = "pinroot",
            ContentTemplate = new DataTemplate(typeof(PinLockPage))
        };
    }
}
#endif
