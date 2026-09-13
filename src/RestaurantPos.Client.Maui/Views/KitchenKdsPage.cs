#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Ecran Cuisine KDS (Kitchen Display System).
/// 3 colonnes : En Attente | En Preparation | Pret.
/// Chronometre par ticket avec code couleur urgence.
/// </summary>
public class KitchenKdsPage : ContentPage
{
    private readonly KdsViewModel _vm;
    private System.Timers.Timer? _clockTimer;

    public KitchenKdsPage(KdsViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("#0A0F1A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _clockTimer = new System.Timers.Timer(30_000);
        _clockTimer.Elapsed += (_, _) =>
            MainThread.BeginInvokeOnMainThread(() => _vm.RefreshTimers());
        _clockTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _clockTimer?.Stop();
        _clockTimer?.Dispose();
        _clockTimer = null;
    }

    private void Build()
    {
        // Header KDS
        var header = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = new Thickness(16, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var titleLabel = new Label
        {
            Text = "👨‍🍳 Ecran Cuisine",
            TextColor = Colors.White,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };

        var stationLabel = new Label
        {
            TextColor = Color.FromArgb("#F59E0B"),
            FontSize = 14,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        stationLabel.SetBinding(Label.TextProperty, nameof(KdsViewModel.CurrentStationId));

        var backBtn = new Button
        {
            Text = "← Salle",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 14,
            HeightRequest = 36,
            CornerRadius = 8,
            Command = new Command(async () => await Shell.Current.GoToAsync("//floor"))
        };

        Grid.SetColumn(stationLabel, 1);
        Grid.SetColumn(backBtn, 2);
        header.Add(titleLabel);
        header.Add(stationLabel);
        header.Add(backBtn);

        // Colonnes KDS
        var columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 8,
            Padding = new Thickness(8)
        };

        columns.Add(BuildKdsColumn("⏳ En Attente", "#F59E0B", _vm.PendingTickets), 0, 0);
        columns.Add(BuildKdsColumn("🔥 En Preparation", "#3B82F6", _vm.InPrepTickets), 1, 0);
        columns.Add(BuildKdsColumn("✅ Pret a Servir", "#10B981", _vm.ReadyTickets), 2, 0);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(header, 0),
                AddToGrid(columns, 1)
            }
        };
    }

    private View BuildKdsColumn(string title, string colorHex, System.Collections.ObjectModel.ObservableCollection<KdsTicketItemViewModel> items)
    {
        var colHeader = new Label
        {
            Text = title,
            TextColor = Color.FromArgb(colorHex),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(8, 10),
            BackgroundColor = Color.FromArgb("#0F172A")
        };

        var list = new CollectionView
        {
            ItemsSource = items,
            ItemTemplate = new DataTemplate(() => BuildTicketCard(colorHex))
        };

        return new Grid
        {
            BackgroundColor = Color.FromArgb("#111827"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(colHeader, 0),
                AddToGrid(new ScrollView { Content = list }, 1)
            }
        };
    }

    private View BuildTicketCard(string accentColor)
    {
        var frame = new Frame
        {
            Margin = new Thickness(6, 4),
            Padding = new Thickness(12),
            CornerRadius = 12,
            HasShadow = false,
            BackgroundColor = Color.FromArgb("#1E293B")
        };

        var tableLabel = new Label
        {
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White
        };
        tableLabel.SetBinding(Label.TextProperty,
            new Binding($"{nameof(KdsTicketItemViewModel.Ticket)}.{nameof(KitchenTicketDto.TableNumber)}"));

        var timerLabel = new Label { FontSize = 14, FontAttributes = FontAttributes.Bold };
        timerLabel.SetBinding(Label.TextProperty, nameof(KdsTicketItemViewModel.ElapsedTimeText));
        timerLabel.SetBinding(Label.TextColorProperty, new Binding(
            nameof(KdsTicketItemViewModel.UrgencyColorHex),
            converter: new HexToColorConverter()));

        var urgencyBar = new BoxView { HeightRequest = 4, CornerRadius = 2 };
        urgencyBar.SetBinding(BoxView.ColorProperty, new Binding(
            nameof(KdsTicketItemViewModel.UrgencyColorHex),
            converter: new HexToColorConverter()));

        var itemsList = new VerticalStackLayout { Spacing = 4 };
        // Items du ticket seront ajoutes dynamiquement via binding dans une version enrichie

        var bumpBtn = new Button
        {
            Text = "BUMP ▶",
            BackgroundColor = Color.FromArgb(accentColor),
            TextColor = Colors.White,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 52,
            CornerRadius = 10,
            Margin = new Thickness(0, 8, 0, 0),
            Command = _vm.BumpTicketCommand
        };
        bumpBtn.SetBinding(Button.CommandParameterProperty, new Binding("."));

        var recallBtn = new Button
        {
            Text = "↩ Rappel",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 13,
            HeightRequest = 36,
            CornerRadius = 8,
            Command = _vm.RecallTicketCommand
        };
        recallBtn.SetBinding(Button.CommandParameterProperty, new Binding("."));

        frame.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new HorizontalStackLayout
                {
                    Children = { tableLabel, new BoxView { Width = GridLength.Star.Value }, timerLabel }
                },
                urgencyBar,
                itemsList,
                bumpBtn,
                recallBtn
            }
        };

        return frame;
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    private class HexToColorConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => v is string hex ? Color.FromArgb(hex) : Colors.White;
        public object ConvertBack(object? v, Type t, object? p, System.Globalization.CultureInfo c)
            => throw new NotImplementedException();
    }
}
#endif
