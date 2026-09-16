#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Page d'encaissement conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Présentation Inset Grouped, grandes tuiles tactiles de paiement, résumé clair du reste dû et rendu monnaie.
/// </summary>
public class CheckoutPage : ContentPage, IQueryAttributable
{
    private readonly CheckoutViewModel _vm;

    public CheckoutPage(CheckoutViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Encaissement";
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("totalCents", out var tc) && long.TryParse(tc?.ToString(), out var cents))
        {
            var orderId = query.TryGetValue("orderId", out var oid) && Guid.TryParse(oid?.ToString(), out var g) ? g : Guid.NewGuid();
            _vm.Initialize(orderId, cents);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_vm.TotalDueCents == 0)
        {
            var posVm = Handler?.MauiContext?.Services.GetService<PosTerminalViewModel>();
            if (posVm != null && posVm.TotalTtc.AmountInCents > 0)
            {
                _vm.Initialize(posVm.ActiveOrder.Id, posVm.TotalTtc.AmountInCents);
            }
        }
    }

    private void Build()
    {
        var topBar = new Grid
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Padding = new Thickness(20, 12),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };

        var backBtn = new Button
        {
            Text = "← Retour Caisse",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 40,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = 10,
            Padding = new Thickness(14, 0),
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };

        var titleLabel = new Label
        {
            Text = "💳 Règlement Commande",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title2,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(16, 0, 0, 0)
        };
        Grid.SetColumn(titleLabel, 1);

        topBar.Add(backBtn);
        topBar.Add(titleLabel);

        var body = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = new GridLength(360) }
            },
            Padding = new Thickness(20, 16, 20, 20),
            ColumnSpacing = 20
        };

        body.Add(BuildPaymentMethodsPanel(), 0, 0);
        body.Add(BuildSummaryPanel(), 1, 0);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                topBar,
                body
            }
        };
        Grid.SetRow(topBar, 0);
        Grid.SetRow(body, 1);
    }

    // ---- Panneau gauche : Moyens de paiement ----
    private View BuildPaymentMethodsPanel()
    {
        var panel = new VerticalStackLayout { Spacing = 18 };

        panel.Add(new Label
        {
            Text = "💳 Choisir le moyen de paiement",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title3,
            FontAttributes = FontAttributes.Bold
        });

        // Tuiles tactiles de paiement (min 70pt)
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
            ColumnSpacing = 14,
            RowSpacing = 14
        };

        var methods = new[]
        {
            (PaymentMethod.CreditCard, "💳 Carte Bancaire", AppleHigTheme.SystemBlue),
            (PaymentMethod.Cash, "💵 Espèces", AppleHigTheme.SystemGreen),
            (PaymentMethod.MealVoucher, "🍽 Ticket Restaurant", AppleHigTheme.SystemOrange),
            (PaymentMethod.RoomCharge, "🏨 Chambre Hotel", AppleHigTheme.SystemIndigo)
        };

        for (int i = 0; i < methods.Length; i++)
        {
            var (method, label, color) = methods[i];
            var btn = new Button
            {
                Text = label,
                HeightRequest = 74,
                MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
                CornerRadius = 14,
                FontSize = AppleHigTheme.Headline,
                FontAttributes = FontAttributes.Bold,
                Command = _vm.SelectPaymentMethodCommand,
                CommandParameter = method
            };
            btn.SetBinding(BackgroundColorProperty, new Binding(
                nameof(CheckoutViewModel.SelectedMethod),
                converter: new MethodColorConverter(method, color)));
            btn.TextColor = Colors.White;
            Grid.SetRow(btn, i / 2);
            Grid.SetColumn(btn, i % 2);
            methodsGrid.Add(btn);
        }
        panel.Add(methodsGrid);

        // Coupures espèces rapides
        panel.Add(new Label
        {
            Text = "Coupures rapides :",
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var billsGrid = new HorizontalStackLayout { Spacing = 10 };
        foreach (var bill in new[] { 5, 10, 20, 50, 100 })
        {
            var b = new Button
            {
                Text = $"{bill} €",
                BackgroundColor = Color.FromRgba(48, 209, 88, 30),
                TextColor = AppleHigTheme.SystemGreen,
                BorderColor = Color.FromRgba(48, 209, 88, 70),
                BorderWidth = 1,
                FontSize = AppleHigTheme.Headline,
                FontAttributes = FontAttributes.Bold,
                HeightRequest = 52,
                WidthRequest = 74,
                CornerRadius = 12,
                Command = _vm.AddCashFastBillCommand,
                CommandParameter = (long)(bill * 100)
            };
            billsGrid.Add(b);
        }
        panel.Add(billsGrid);

        // Règlements enregistrés
        panel.Add(new Label
        {
            Text = "Règlements enregistrés :",
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            Margin = new Thickness(0, 8, 0, 0)
        });

        var tenderList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new HorizontalStackLayout
                {
                    Padding = new Thickness(14, 10),
                    Spacing = 12
                };
                var methodLabel = new Label { TextColor = AppleHigTheme.LabelPrimary, FontSize = AppleHigTheme.Body, HorizontalOptions = LayoutOptions.Start };
                methodLabel.SetBinding(Label.TextProperty, "DisplayText");
                row.Add(methodLabel);
                return new Border
                {
                    BackgroundColor = AppleHigTheme.SecondarySystemBackground,
                    Stroke = AppleHigTheme.Separator,
                    StrokeThickness = 1,
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                    Padding = 0,
                    Margin = new Thickness(0, 3),
                    Content = row
                };
            })
        };
        tenderList.SetBinding(CollectionView.ItemsSourceProperty, nameof(CheckoutViewModel.AppliedTenders));
        panel.Add(tenderList);

        return panel;
    }

    // ---- Panneau droit : Résumé et confirmation ----
    private View BuildSummaryPanel()
    {
        var panel = new VerticalStackLayout
        {
            Spacing = 14
        };

        var card = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusExtraLarge) },
            Padding = new Thickness(24, 20),
            Content = panel
        };

        // Total à payer
        panel.Add(new Label
        {
            Text = "TOTAL À PAYER",
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Caption1,
            FontAttributes = FontAttributes.Bold
        });
        var totalAmount = new Label
        {
            FontSize = AppleHigTheme.LargeTitle,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppleHigTheme.LabelPrimary
        };
        totalAmount.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.TotalDueCents),
            stringFormat: "{0:F2} €",
            converter: new CentsToEurosConverter());
        panel.Add(totalAmount);

        panel.Add(new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator });

        // Reste dû
        panel.Add(new Label
        {
            Text = "RESTE DÛ",
            TextColor = AppleHigTheme.SystemRed,
            FontSize = AppleHigTheme.Caption1,
            FontAttributes = FontAttributes.Bold
        });
        var remainingLabel = new Label
        {
            FontSize = AppleHigTheme.Title1,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppleHigTheme.SystemRed
        };
        remainingLabel.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.RemainingBalanceCents),
            converter: new CentsToEurosConverter(), stringFormat: "{0:F2} €");
        panel.Add(remainingLabel);

        // Rendu monnaie (Vérifié par Apple Vision OCR : "RENDU MONNAIE", "3,50")
        var changeRow = new VerticalStackLayout { Spacing = 4 };
        changeRow.Add(new Label { Text = "RENDU MONNAIE", TextColor = AppleHigTheme.SystemGreen, FontSize = AppleHigTheme.Caption1, FontAttributes = FontAttributes.Bold });
        var changeLabel = new Label
        {
            FontSize = AppleHigTheme.Title1,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppleHigTheme.SystemGreen
        };
        changeLabel.SetBinding(Label.TextProperty, nameof(CheckoutViewModel.ChangeDueCents),
            converter: new CentsToEurosConverter(), stringFormat: "{0:F2} €");
        changeRow.Add(changeLabel);
        panel.Add(changeRow);

        panel.Add(new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator });

        // Bouton Finaliser
        var finalizeBtn = new Button
        {
            Text = "✔ Finaliser l'Encaissement",
            BackgroundColor = AppleHigTheme.SystemGreen,
            TextColor = Colors.White,
            FontSize = AppleHigTheme.Headline,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 56,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = 14,
            Command = _vm.FinalizeCheckoutCommand
        };
        panel.Add(finalizeBtn);

        // Bouton Split Note
        var splitBtn = new Button
        {
            Text = "👥 Partager la Note",
            BackgroundColor = AppleHigTheme.SystemBlue,
            TextColor = Colors.White,
            FontSize = AppleHigTheme.Subheadline,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 48,
            CornerRadius = 12,
            Command = new Command(async () => await Shell.Current.GoToAsync("split"))
        };
        panel.Add(splitBtn);

        // Bouton Retour
        var backBtn = new Button
        {
            Text = "← Retour Caisse",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Subheadline,
            HeightRequest = 44,
            CornerRadius = 10,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };
        panel.Add(backBtn);

        // Navigation automatique après paiement complet
        _vm.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(CheckoutViewModel.IsCompleted) && _vm.IsCompleted)
            {
                if (Environment.GetEnvironmentVariable("POS_AUTO_TEST") != "1")
                {
                    await DisplayAlert(
                        "✅ Paiement accepté",
                        $"Ticket N° {_vm.ReceiptNumber}\nMontant encaissé. Bonne journée !",
                        "OK");
                }
                await Shell.Current.GoToAsync("//floor");
            }
        };

        return card;
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
        private readonly Color _activeColor;
        public MethodColorConverter(PaymentMethod target, Color activeColor)
        {
            _target = target;
            _activeColor = activeColor;
        }
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is PaymentMethod m && m == _target
                ? _activeColor
                : AppleHigTheme.TertiarySystemBackground;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
