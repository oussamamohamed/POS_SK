#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Ecran de verrouillage et d'authentification par code PIN.
/// Interface tactile plein ecran avec pave numerique 3x4.
/// </summary>
public class PinLockPage : ContentPage
{
    private readonly PinLockViewModel _vm;

    public PinLockPage(PinLockViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        // Indicateur de saisie PIN (pastilles)
        var pinDotsRow = new HorizontalStackLayout
        {
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center
        };
        for (int i = 0; i < 4; i++)
        {
            var dot = new BoxView
            {
                WidthRequest = 20,
                HeightRequest = 20,
                CornerRadius = 10,
                Color = Color.FromArgb("#334155")
            };
            dot.SetBinding(BoxView.ColorProperty, new Binding(
                nameof(PinLockViewModel.PinInput),
                converter: new PinDotColorConverter(i)));
            pinDotsRow.Add(dot);
        }

        // Message d'erreur
        var errorLabel = new Label
        {
            TextColor = Color.FromArgb("#EF4444"),
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Center
        };
        errorLabel.SetBinding(Label.TextProperty, nameof(PinLockViewModel.ErrorMessage));
        errorLabel.SetBinding(Label.IsVisibleProperty, new Binding(
            nameof(PinLockViewModel.ErrorMessage),
            converter: new StringToBoolConverter()));

        // Grille numerique 3x4
        var numPad = BuildNumPad();

        // Bouton effacer
        var clearBtn = new Button
        {
            Text = "⌫",
            BackgroundColor = Color.FromArgb("#1E293B"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 28,
            HeightRequest = 70,
            CornerRadius = 12,
            Command = new Command(() => _vm.DeleteDigit())
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Padding = new Thickness(40),
            RowSpacing = 24,
            Children =
            {
                // Logo & Titre
                AddToGrid(new VerticalStackLayout
                {
                    Spacing = 8,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.End,
                    Children =
                    {
                        new Label
                        {
                            Text = "🍽️ RestaurantPos",
                            TextColor = Colors.White,
                            FontSize = 32,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Saisissez votre code PIN",
                            TextColor = Color.FromArgb("#94A3B8"),
                            FontSize = 18,
                            HorizontalOptions = LayoutOptions.Center
                        }
                    }
                }, 0),
                AddToGrid(pinDotsRow, 1),
                AddToGrid(errorLabel, 2),
                AddToGrid(numPad, 3),
                AddToGrid(clearBtn, 4)
            }
        };

        // Navigation auto apres validation
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(PinLockViewModel.IsAuthenticated) && _vm.IsAuthenticated)
            {
                await Shell.Current.GoToAsync("//floor");
            }
        };
    }

    private Grid BuildNumPad()
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 12,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        string[] digits = ["1","2","3","4","5","6","7","8","9","*","0","#"];
        for (int i = 0; i < digits.Length; i++)
        {
            int row = i / 3;
            int col = i % 3;
            string digit = digits[i];

            if (digit == "*" || digit == "#") continue;

            var btn = new Button
            {
                Text = digit,
                BackgroundColor = Color.FromArgb("#1E293B"),
                TextColor = Colors.White,
                FontSize = 32,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = 80,
                CornerRadius = 16,
                Command = new Command(async () => await _vm.AppendDigitAsync(digit))
            };

            btn.Pressed += (_, _) => btn.BackgroundColor = Color.FromArgb("#3B82F6");
            btn.Released += (_, _) => btn.BackgroundColor = Color.FromArgb("#1E293B");

            Grid.SetRow(btn, row);
            Grid.SetColumn(btn, col);
            grid.Add(btn);
        }

        return grid;
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    // Convertisseurs locaux
    private class PinDotColorConverter : IValueConverter
    {
        private readonly int _index;
        public PinDotColorConverter(int index) { _index = index; }

        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is string pin && pin.Length > _index
                ? Color.FromArgb("#3B82F6")
                : Color.FromArgb("#334155");

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class StringToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is string s && !string.IsNullOrEmpty(s);

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}
#endif
