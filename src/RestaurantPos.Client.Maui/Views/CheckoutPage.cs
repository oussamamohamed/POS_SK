#if MAUI_UI
using System;
using System.Collections.Generic;
using System.Linq;
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
/// Présentation Inset Grouped, grandes tuiles tactiles de paiement, résumé clair du reste dû,
/// rendu monnaie et retour haptique tactile.
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
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, true);
        Build();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        var tableNumber = string.Empty;
        if (query.TryGetValue("tableNumber", out var tnObj) && tnObj != null)
        {
            tableNumber = tnObj.ToString() ?? string.Empty;
        }

        if (query.TryGetValue("totalCents", out var tc) && long.TryParse(tc?.ToString(), out var cents))
        {
            var orderId = query.TryGetValue("orderId", out var oid) && Guid.TryParse(oid?.ToString(), out var g) ? g : Guid.NewGuid();
            _vm.Initialize(orderId, cents, tableNumber);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.PropertyChanged += OnCheckoutViewModelPropertyChanged;
        if (_vm.TotalDueCents == 0)
        {
            var posVm = Handler?.MauiContext?.Services.GetService<PosTerminalViewModel>();
            if (posVm != null && posVm.TotalTtc.AmountInCents > 0)
            {
                _vm.Initialize(posVm.ActiveOrder.Id, posVm.TotalTtc.AmountInCents, posVm.ActiveTable);
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _vm.PropertyChanged -= OnCheckoutViewModelPropertyChanged;
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
            Command = new Command(async () =>
            {
                AppleHigTheme.PerformHapticClick();
                await Shell.Current.GoToAsync("..");
            })
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
                Command = new Command(() =>
                {
                    AppleHigTheme.PerformHapticClick();
                    _vm.SelectPaymentMethodCommand.Execute(method);
                })
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
                Command = new Command(() =>
                {
                    AppleHigTheme.PerformHapticClick();
                    _vm.AddCashFastBillCommand.Execute((long)(bill * 100));
                })
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

        var tenderList = new VerticalStackLayout { Spacing = 4 };
        BindableLayout.SetItemTemplate(tenderList, new DataTemplate(() =>
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
        }));
        tenderList.SetBinding(BindableLayout.ItemsSourceProperty, nameof(CheckoutViewModel.AppliedTenders));
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

        // Rendu monnaie
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
            Command = new Command(() =>
            {
                AppleHigTheme.PerformHapticSuccess();
                _vm.FinalizeCheckoutCommand.Execute(null);
            })
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
            Command = new Command(async () =>
            {
                AppleHigTheme.PerformHapticClick();
                await Shell.Current.GoToAsync("split");
            })
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
            Command = new Command(async () =>
            {
                AppleHigTheme.PerformHapticClick();
                await Shell.Current.GoToAsync("..");
            })
        };
        panel.Add(backBtn);

        return card;
    }

    private async void OnCheckoutViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CheckoutViewModel.IsCompleted) && _vm.IsCompleted)
        {
            var floorVm = Handler?.MauiContext?.Services.GetService<FloorPlanViewModel>();
            var posVm = Handler?.MauiContext?.Services.GetService<PosTerminalViewModel>();
            var targetTable = !string.IsNullOrWhiteSpace(_vm.TableNumber)
                ? _vm.TableNumber
                : (posVm?.ActiveTable ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(targetTable))
            {
                floorVm?.SetTableStatus(targetTable, TableStatus.Free);
                posVm?.ClearTableOrder(targetTable);
            }
            else
            {
                posVm?.ClearCart();
            }

            if (Environment.GetEnvironmentVariable("POS_AUTO_TEST") != "1")
            {
                await DisplayAlert(
                    "✅ Paiement accepté",
                    $"Ticket N° {_vm.ReceiptNumber}\nMontant encaissé ({_vm.TotalDueCents / 100.0:F2} €). Bonne journée !",
                    "OK");
            }

            // Check if returning to split bill
            var splitVm = Handler?.MauiContext?.Services.GetService<SplitBillViewModel>();
            if (splitVm != null && splitVm.Partitions.Any(p => !p.IsPaid && p.AmountCents == _vm.TotalDueCents))
            {
                var unpaidPart = splitVm.Partitions.FirstOrDefault(p => !p.IsPaid && p.AmountCents == _vm.TotalDueCents);
                if (unpaidPart != null)
                {
                    unpaidPart.IsPaid = true;
                    await Shell.Current.GoToAsync("..");
                    return;
                }
            }

            await Shell.Current.GoToAsync("//floor");
        }
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
            => v is PaymentMethod m && m == _target ? _activeColor : AppleHigTheme.TertiarySystemBackground;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }

    private class CentsToEurosConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is long cents ? cents / 100.0m : 0.0m;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
