#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Écran de verrouillage et d'authentification par code PIN conforme aux Apple Human Interface Guidelines (HIG).
/// Interface tactile iPad épurée inspirée du Lock Screen iOS, avec touches numériques circulaires/squircles
/// et pastilles PIN élégantes.
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
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    private void Build()
    {
        // 1. Indicateur de saisie PIN (Pastilles style Apple Lock Screen)
        var pinDotsRow = new HorizontalStackLayout
        {
            Spacing = 20,
            HorizontalOptions = LayoutOptions.Center
        };
        for (int i = 0; i < 4; i++)
        {
            var dot = new BoxView
            {
                WidthRequest = 18,
                HeightRequest = 18,
                CornerRadius = 9,
                Color = AppleHigTheme.QuaternarySystemFill
            };
            dot.SetBinding(BoxView.ColorProperty, new Binding(
                nameof(PinLockViewModel.PinInput),
                converter: new PinDotColorConverter(i)));
            pinDotsRow.Add(dot);
        }

        // 2. Message d'erreur
        var errorLabel = new Label
        {
            TextColor = AppleHigTheme.SystemRed,
            FontSize = AppleHigTheme.Subheadline,
            HorizontalOptions = LayoutOptions.Center
        };
        errorLabel.SetBinding(Label.TextProperty, nameof(PinLockViewModel.ErrorMessage));
        errorLabel.SetBinding(Label.IsVisibleProperty, new Binding(
            nameof(PinLockViewModel.ErrorMessage),
            converter: new StringToBoolConverter()));

        // 3. Pavé numérique tactile Apple HIG (3x4)
        var numPad = BuildNumPad();

        // 4. Touche Effacer Apple style
        var clearBtn = new Button
        {
            Text = "⌫ Effacer",
            BackgroundColor = Colors.Transparent,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Headline,
            HeightRequest = 48,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = (int)AppleHigTheme.CornerRadiusMedium,
            Command = new Command(() => _vm.DeleteDigit())
        };

        _testStatusLabel = new Label
        {
            TextColor = AppleHigTheme.SystemTeal,
            FontSize = AppleHigTheme.Footnote,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        var autoTestBtn = new Button
        {
            Text = "⚡ Lancer Tests Auto Simulateur",
            BackgroundColor = Color.FromRgba(94, 92, 230, 30),
            TextColor = AppleHigTheme.SystemIndigo,
            BorderColor = Color.FromRgba(94, 92, 230, 80),
            BorderWidth = 1,
            FontSize = AppleHigTheme.Footnote,
            HeightRequest = 42,
            CornerRadius = (int)AppleHigTheme.CornerRadiusSmall,
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

        // Carte centrale Apple Inset Grouped
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
                        new HorizontalStackLayout
                        {
                            Spacing = 10,
                            HorizontalOptions = LayoutOptions.Center,
                            Children =
                            {
                                new Border
                                {
                                    Padding = 0,
                                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusSmall) },
                                    Stroke = AppleHigTheme.SystemBlue,
                                    StrokeThickness = 1.5,
                                    WidthRequest = 40,
                                    HeightRequest = 40,
                                    BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                                    VerticalOptions = LayoutOptions.Center,
                                    Content = new Label
                                    {
                                        Text = "⚡",
                                        TextColor = AppleHigTheme.SystemBlue,
                                        FontSize = 22,
                                        HorizontalOptions = LayoutOptions.Center,
                                        VerticalOptions = LayoutOptions.Center
                                    }
                                },
                                new VerticalStackLayout
                                {
                                    Spacing = 0,
                                    VerticalOptions = LayoutOptions.Center,
                                    Children =
                                    {
                                        new Label
                                        {
                                            Text = "Restaurant POS",
                                            TextColor = AppleHigTheme.LabelPrimary,
                                            FontSize = AppleHigTheme.Title2,
                                            FontAttributes = FontAttributes.Bold
                                        },
                                        new Label
                                        {
                                            Text = "AGY Edition iPad",
                                            TextColor = AppleHigTheme.LabelSecondary,
                                            FontSize = AppleHigTheme.Caption1
                                        }
                                    }
                                }
                            }
                        },
                        new Label
                        {
                            Text = "Saisissez votre code PIN",
                            TextColor = AppleHigTheme.LabelSecondary,
                            FontSize = AppleHigTheme.Subheadline,
                            HorizontalOptions = LayoutOptions.Center,
                            Margin = new Thickness(0, 4, 0, 0)
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
            BackgroundColor = AppleHigTheme.SystemBackground,
            Padding = new Thickness(24),
            Children =
            {
                new Border
                {
                    BackgroundColor = AppleHigTheme.SecondarySystemBackground,
                    Stroke = AppleHigTheme.Separator,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(24) },
                    Padding = new Thickness(36, 28),
                    WidthRequest = 400,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Content = card
                }
            }
        };

        // Navigation automatique après validation réussie
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
            ColumnSpacing = 14,
            RowSpacing = 14,
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
                BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                TextColor = AppleHigTheme.LabelPrimary,
                FontSize = AppleHigTheme.Title1,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = 76,
                CornerRadius = 18,
                BorderColor = AppleHigTheme.Separator,
                BorderWidth = 1,
                Command = new Command(async () => await _vm.AppendDigitAsync(digit))
            };

            btn.Pressed += (_, _) => btn.BackgroundColor = AppleHigTheme.SystemBlue;
            btn.Released += (_, _) => btn.BackgroundColor = AppleHigTheme.TertiarySystemBackground;

            Grid.SetRow(btn, row);
            Grid.SetColumn(btn, col);
            grid.Add(btn);
        }

        return grid;
    }

    // Convertisseurs locaux
    private class PinDotColorConverter : IValueConverter
    {
        private readonly int _index;
        public PinDotColorConverter(int index) { _index = index; }

        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is string pin && pin.Length > _index
                ? AppleHigTheme.SystemBlue
                : AppleHigTheme.QuaternarySystemFill;

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
