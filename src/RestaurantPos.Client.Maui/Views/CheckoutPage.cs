#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page d'encaissement multi-moyens de paiement.
/// Gere CB, Especes (avec rendu monnaie), Tickets Restaurant, Chambre.
/// </summary>
public class CheckoutPage : ContentPage
{
    private readonly CheckoutViewModel _vm;

    public CheckoutPage(CheckoutViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Encaissement";
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(320) }
            },
            Padding = new Thickness(16),
            ColumnSpacing = 16
        };

        ((Grid)Content).Add(BuildPaymentMethodsPanel(), 0, 0);
        ((Grid)Content).Add(BuildSummaryPanel(), 1, 0);
    }

    // ---- Panneau gauche : Moyens de paiement ----
    private View BuildPaymentMethodsPanel()
    {
        var panel = new VerticalStackLayout { Spacing = 16 };

        // Titre
        panel.Add(new Label
        {
            Text = "💳 Choisir le moyen de paiement",
            TextColor = Colors.White,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        });

        // Boutons de methode de paiement
        var methodsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnSpacing = 12,
            RowSpacing = 12
        };

        var methods = new[]
        {
            (PaymentMethod.CreditCard, "💳 Carte Bancaire", "#3B82F6"),
            (PaymentMethod.Cash, "💵 Especes", "#10B981"),
            (PaymentMethod.MealVoucher, "🍽 Ticket Restaurant", "#F59E0B"),
            (PaymentMethod.RoomCharge, "🏨 Chambre Hotel", "#8B5CF6")
        };

        for (int i = 0; i < methods.Length; i++)
        {
            var (method, label, color) = methods[i];
            var btn = new Button
            {
                Text = label,
                HeightRequest = 70,
                CornerRadius = 12,
                FontSize = 16,
                Command = _vm.SelectPaymentMethodCommand,
                CommandParameter = method
            };
            btn.SetBinding(BackgroundColorProperty, new Binding(
                nameof(CheckoutViewModel.SelectedMethod),
                converter: new MethodColorConverter(method, color)));
            btn.SetBinding(Button.TextColorProperty, Colors.White);
            Grid.SetRow(btn, i / 2);
            Grid.SetColumn(btn, i % 2);
            methodsGrid.Add(btn);
        }
        panel.Add(methodsGrid);

        // Coupures especes rapides
        panel.Add(new Label
        {
            Text = "Coupures rapides :",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var billsGrid = new HorizontalStackLayout { Spacing = 10 };
        foreach (var bill in new[] { 5, 10, 20, 50, 100 })
        {
            var b = new Button
            {
                Text = $"{bill} €",
                BackgroundColor = Color.FromArgb("#164E32"),
                TextColor = Color.FromArgb("#10B981"),
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = 52,
                WidthRequest = 70,
                CornerRadius = 10,
                Command = _vm.AddCashFastBillCommand,
                CommandParameter = (long)(bill * 100)
            };
            billsGrid.Add(b);
        }
        panel.Add(billsGrid);

        // Tenders appliques
        panel.Add(new Label
        {
            Text = "Reglements enregistres :",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 14,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var tenderList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new HorizontalStackLayout
                {
                    Padding = new Thickness(12, 8),
                    Spacing = 12
                };
                var methodLabel = new Label { TextColor = Color.FromArgb("#94A3B8"), FontSize = 14, HorizontalOptions = LayoutOptions.Start };
                methodLabel.SetBinding(Label.TextProperty, "DisplayText");
                row.Add(methodLabel);
                return new Frame
                {
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    CornerRadius = 8,
                    HasShadow = false,
                    Padding = 0,
                    Margin = new Thickness(0, 2),
                    Content = row
                };
            })
        };
        tenderList.SetBinding(CollectionView.ItemsSourceProperty, nameof(CheckoutViewModel.AppliedTenders));
        panel.Add(tenderList);

        return panel;
    }

    // ---- Panneau droit : Resume et confirmation ----
    private View BuildSummaryPanel()
    {
        var panel = new VerticalStackLayout
        {
            Spacing = 14,
            BackgroundColor = Color.FromArgb("#1E293B"),
            Padding = new Thickness(20)
        };

        // Total a payer
        panel.Add(new Label
        {
            Text = "TOTAL A PAYER",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 13
        });
        var totalAmount = new Label
        {
            FontSize = 42,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
        totalAmount.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.TotalDueCents),
            stringFormat: "{0:F2} €",
            converter: new CentsToEurosConverter());
        panel.Add(totalAmount);

        // Separateur
        panel.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") });

        // Reste a payer
        panel.Add(new Label
        {
            Text = "RESTE DU",
            TextColor = Color.FromArgb("#EF4444"),
            FontSize = 13
        });
        var remainingLabel = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#EF4444")
        };
        remainingLabel.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.RemainingBalanceCents),
            converter: new CentsToEurosConverter(), stringFormat: "{0:F2} €");
        panel.Add(remainingLabel);

        // Rendu monnaie
        var changeRow = new VerticalStackLayout { Spacing = 4 };
        changeRow.Add(new Label { Text = "RENDU MONNAIE", TextColor = Color.FromArgb("#10B981"), FontSize = 13 });
        var changeLabel = new Label
        {
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#10B981")
        };
        changeLabel.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.ChangeDueCents),
            converter: new CentsToEurosConverter(), stringFormat: "{0:F2} €");
        changeRow.Add(changeLabel);
        panel.Add(changeRow);

        panel.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") });

        // Bouton Finaliser
        var finalizeBtn = new Button
        {
            Text = "✔ Finaliser l'Encaissement",
            BackgroundColor = Color.FromArgb("#7C3AED"),
            TextColor = Colors.White,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 64,
            CornerRadius = 14,
            Command = _vm.FinalizeCheckoutCommand
        };
        panel.Add(finalizeBtn);

        // Bouton Split Note
        var splitBtn = new Button
        {
            Text = "👥 Partager la Note",
            BackgroundColor = Color.FromArgb("#1E40AF"),
            TextColor = Colors.White,
            FontSize = 16,
            HeightRequest = 52,
            CornerRadius = 12,
            Command = new Command(async () => await Shell.Current.GoToAsync("split"))
        };
        panel.Add(splitBtn);

        // Bouton Retour
        var backBtn = new Button
        {
            Text = "← Retour Caisse",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 15,
            HeightRequest = 44,
            CornerRadius = 10,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };
        panel.Add(backBtn);

        // Navigation auto apres paiement complet
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(CheckoutViewModel.IsCompleted) && _vm.IsCompleted)
            {
                await DisplayAlert(
                    "✅ Paiement accepte",
                    $"Ticket N° {_vm.ReceiptNumber}\nMontant encaisse. Bonne journee !",
                    "OK");
                await Shell.Current.GoToAsync("//floor");
            }
        };

        return panel;
    }

    private class CentsToEurosConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is long cents ? cents / 100.0m : 0m;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class MethodColorConverter : IValueConverter
    {
        private readonly PaymentMethod _target;
        private readonly string _activeColor;
        public MethodColorConverter(PaymentMethod target, string activeColor)
        {
            _target = target;
            _activeColor = activeColor;
        }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is PaymentMethod m && m == _target
                ? Color.FromArgb(_activeColor)
                : Color.FromArgb("#334155");
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
