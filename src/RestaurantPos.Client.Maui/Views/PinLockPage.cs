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
    private readonly IServiceProvider _serviceProvider;
    private Label? _testStatusLabel;

    public PinLockPage(PinLockViewModel vm, IServiceProvider serviceProvider)
    {
        _vm = vm;
        _serviceProvider = serviceProvider;
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
            Text = "⌫ Effacer",
            BackgroundColor = Color.FromArgb("#0F172A"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 16,
            HeightRequest = 52,
            CornerRadius = 12,
            Command = new Command(() => _vm.DeleteDigit())
        };

        _testStatusLabel = new Label
        {
            TextColor = Color.FromArgb("#38BDF8"),
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        var autoTestBtn = new Button
        {
            Text = "⚡ Lancer Tests Auto Simulateur",
            BackgroundColor = Color.FromArgb("#1E1B4B"),
            TextColor = Color.FromArgb("#818CF8"),
            BorderColor = Color.FromArgb("#4338CA"),
            BorderWidth = 1,
            FontSize = 13,
            HeightRequest = 42,
            CornerRadius = 10,
            Command = new Command(() =>
            {
                Task.Run(async () =>
                {
                    await Services.SimulatorAutoTestRunner.RunAllTestsAsync(_serviceProvider);
                });
            })
        };

        Services.SimulatorAutoTestRunner.StatusChanged += (msg) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_testStatusLabel != null)
                {
                    _testStatusLabel.Text = msg;
                    _testStatusLabel.IsVisible = true;
                }
            });
        };

        var card = new VerticalStackLayout
        {
            Spacing = 16,
            HorizontalOptions = LayoutOptions.Center,
            Children =
            {
                new VerticalStackLayout
                {
                    Spacing = 6,
                    HorizontalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = "🍽️ Restaurant POS",
                            TextColor = Colors.White,
                            FontSize = 26,
                            FontAttributes = FontAttributes.Bold,
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = "Saisissez votre code PIN",
                            TextColor = Color.FromArgb("#94A3B8"),
                            FontSize = 16,
                            HorizontalOptions = LayoutOptions.Center
                        }
                    }
                },
                _testStatusLabel,
                pinDotsRow,
                errorLabel,
                numPad,
                clearBtn,
                autoTestBtn
            }
        };

        Content = new Grid
        {
            BackgroundColor = Color.FromArgb("#0A0F1D"),
            Padding = new Thickness(24),
            Children =
            {
                new Frame
                {
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    CornerRadius = 24,
                    Padding = new Thickness(36, 32),
                    WidthRequest = 380,
                    HasShadow = false,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Content = card
                }
            }
        };

        // Navigation auto apres validation
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PinLockViewModel.IsAuthenticated) && _vm.IsAuthenticated)
            {
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        await Shell.Current.GoToAsync("//floor");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Navigation error: {ex}");
                    }
                });
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
