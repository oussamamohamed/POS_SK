#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page de partage de note conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Stepper tactile Apple, fiches de convives Inset Grouped et boutons de règlement ergonomiques.
/// </summary>
public class SplitBillPage : ContentPage
{
    private readonly SplitBillViewModel _vm;

    public SplitBillPage(SplitBillViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Partager la Note";
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        var panel = new VerticalStackLayout
        {
            Spacing = 20,
            Padding = new Thickness(32, 24),
            MaximumWidthRequest = 640,
            HorizontalOptions = LayoutOptions.Center
        };

        // 1. Titre (Vérifié par Apple Vision OCR : "Partage", "Addition")
        panel.Add(new Label
        {
            Text = "👥 Partage de l'Addition",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title1,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        });

        // 2. Montant total (Vérifié par OCR : "Montant")
        var totalLabel = new Label
        {
            FontSize = AppleHigTheme.Title3,
            TextColor = AppleHigTheme.LabelSecondary,
            HorizontalOptions = LayoutOptions.Center
        };
        totalLabel.SetBinding(Label.TextProperty, nameof(SplitBillViewModel.TotalOrderAmountCents),
            stringFormat: "Montant total : {0:F2} €",
            converter: new CentsToEurosConverter());
        panel.Add(totalLabel);

        // 3. Sélecteur de convives Apple Stepper (Vérifié par OCR : "convives")
        panel.Add(BuildGuestSelector());

        panel.Add(new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator });

        // 4. Liste des parts (Fiches Inset Grouped)
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
                    Padding = new Thickness(18, 14),
                    BackgroundColor = AppleHigTheme.SecondarySystemBackground
                };

                var partLabel = new Label
                {
                    FontSize = AppleHigTheme.Headline,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = AppleHigTheme.LabelPrimary,
                    VerticalOptions = LayoutOptions.Center
                };
                partLabel.SetBinding(Label.TextProperty, "DisplayText");

                var payBtn = new Button
                {
                    Text = "Régler",
                    BackgroundColor = AppleHigTheme.SystemGreen,
                    TextColor = Colors.White,
                    FontSize = AppleHigTheme.Subheadline,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 42,
                    MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
                    CornerRadius = 10,
                    WidthRequest = 96
                };
                payBtn.SetBinding(IsEnabledProperty, new Binding("IsPaid",
                    converter: new BoolInverter()));

                Grid.SetColumn(payBtn, 1);

                row.Add(partLabel);
                row.Add(payBtn);

                return new Border
                {
                    Padding = 0,
                    Margin = new Thickness(0, 4),
                    BackgroundColor = AppleHigTheme.SecondarySystemBackground,
                    Stroke = AppleHigTheme.Separator,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusMedium) },
                    Content = row
                };
            })
        };
        partsList.SetBinding(CollectionView.ItemsSourceProperty, nameof(SplitBillViewModel.Partitions));
        panel.Add(partsList);

        // 5. Bouton retour
        var backBtn = new Button
        {
            Text = "← Retour Encaissement",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Headline,
            HeightRequest = 48,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
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
            FontSize = AppleHigTheme.LargeTitle,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppleHigTheme.LabelPrimary,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        guestsLabel.SetBinding(Label.TextProperty, nameof(SplitBillViewModel.GuestsCount));

        var minusBtn = new Button
        {
            Text = "−",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 28,
            WidthRequest = 60,
            HeightRequest = 60,
            CornerRadius = 30,
            BorderColor = AppleHigTheme.Separator,
            BorderWidth = 1,
            Command = _vm.DecreaseGuestsCommand
        };

        var plusBtn = new Button
        {
            Text = "+",
            BackgroundColor = AppleHigTheme.SystemBlue,
            TextColor = Colors.White,
            FontSize = 28,
            WidthRequest = 60,
            HeightRequest = 60,
            CornerRadius = 30,
            Command = _vm.IncreaseGuestsCommand
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "Nombre de convives :",
                    TextColor = AppleHigTheme.LabelSecondary,
                    FontSize = AppleHigTheme.Subheadline,
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
