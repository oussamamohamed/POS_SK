#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page de partage de note a parts egales entre plusieurs convives.
/// Chaque convive peut regler sa part independamment.
/// </summary>
public class SplitBillPage : ContentPage
{
    private readonly SplitBillViewModel _vm;

    public SplitBillPage(SplitBillViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Partager la Note";
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        var panel = new VerticalStackLayout
        {
            Spacing = 20,
            Padding = new Thickness(32)
        };

        // Titre
        panel.Add(new Label
        {
            Text = "👥 Partage de Note",
            TextColor = Colors.White,
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        });

        // Montant total
        var totalLabel = new Label
        {
            FontSize = 18,
            TextColor = Color.FromArgb("#94A3B8"),
            HorizontalOptions = LayoutOptions.Center
        };
        totalLabel.SetBinding(Label.TextProperty, nameof(SplitBillViewModel.TotalOrderAmountCents),
            stringFormat: "Montant total : {0:F2} €",
            converter: new CentsToEurosConverter());
        panel.Add(totalLabel);

        // Selecteur nombre de convives
        panel.Add(BuildGuestSelector());

        // Separateur
        panel.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") });

        // Liste des parts
        var partsList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Padding = new Thickness(16, 12),
                    BackgroundColor = Color.FromArgb("#1E293B")
                };

                var partLabel = new Label
                {
                    FontSize = 16,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                partLabel.SetBinding(Label.TextProperty, "DisplayText");

                var payBtn = new Button
                {
                    Text = "Regler",
                    BackgroundColor = Color.FromArgb("#059669"),
                    TextColor = Colors.White,
                    FontSize = 14,
                    HeightRequest = 40,
                    CornerRadius = 8,
                    WidthRequest = 90
                };
                payBtn.SetBinding(IsEnabledProperty, new Binding("IsPaid",
                    converter: new BoolInverter()));

                Grid.SetColumn(payBtn, 1);

                row.Add(partLabel);
                row.Add(payBtn);

                return new Frame
                {
                    Padding = 0,
                    Margin = new Thickness(0, 4),
                    CornerRadius = 12,
                    HasShadow = false,
                    Content = row
                };
            })
        };
        partsList.SetBinding(CollectionView.ItemsSourceProperty, nameof(SplitBillViewModel.Partitions));
        panel.Add(partsList);

        // Boutons action
        var backBtn = new Button
        {
            Text = "← Retour Encaissement",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 15,
            HeightRequest = 48,
            CornerRadius = 12,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };
        panel.Add(backBtn);

        Content = new ScrollView { Content = panel };
    }

    private View BuildGuestSelector()
    {
        var guestsLabel = new Label
        {
            FontSize = 48,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center
        };
        guestsLabel.SetBinding(Label.TextProperty, nameof(SplitBillViewModel.GuestsCount));

        var minusBtn = new Button
        {
            Text = "−",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontSize = 32,
            WidthRequest = 64,
            HeightRequest = 64,
            CornerRadius = 32,
            Command = _vm.DecreaseGuestsCommand
        };

        var plusBtn = new Button
        {
            Text = "+",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontSize = 32,
            WidthRequest = 64,
            HeightRequest = 64,
            CornerRadius = 32,
            Command = _vm.IncreaseGuestsCommand
        };

        return new VerticalStackLayout
        {
            Spacing = 8,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Nombre de convives",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 14,
                    HorizontalOptions = LayoutOptions.Center
                },
                new HorizontalStackLayout
                {
                    Spacing = 24,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { minusBtn, guestsLabel, plusBtn }
                }
            }
        };
    }

    private class CentsToEurosConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is long cents ? cents / 100.0m : 0m;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class BoolInverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is bool b ? !b : true;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
