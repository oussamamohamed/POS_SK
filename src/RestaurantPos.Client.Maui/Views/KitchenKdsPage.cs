#if MAUI_UI
using System;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Controls;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Écran Cuisine KDS (Kitchen Display System) conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// 3 colonnes : En Attente | En Préparation | Prêt à Servir.
/// Fiches Inset Grouped, badges capsules d'urgence, retour haptique tactile et gros boutons BUMP.
/// </summary>
public class KitchenKdsPage : ContentPage
{
    private readonly KdsViewModel _vm;
    private System.Timers.Timer? _clockTimer;

    public KitchenKdsPage(KdsViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = AppleHigTheme.SystemBackground;
        Shell.SetNavBarIsVisible(this, false);
        Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.SetUseSafeArea(this, true);
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.SeedInitialTicketsIfEmpty();
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
        var topBar = new GlobalHeaderView(PosActiveViewTab.Kds);

        // Subheader KDS : Titre et filtres stations façon Apple iPadOS
        var header = new Grid
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Padding = new Thickness(20, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var titleLabel = new Label
        {
            Text = "👨‍🍳 Écran de Cuisine KDS (Temps Réel)",
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.Title3,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };

        var filterPills = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                MakeStationFilterBtn("Toutes Stations", true),
                MakeStationFilterBtn("Chaud / Grill", false),
                MakeStationFilterBtn("Froid / Entrées", false),
                MakeStationFilterBtn("Bar & Boissons", false)
            }
        };

        Grid.SetColumn(filterPills, 1);
        header.Add(titleLabel);
        header.Add(filterPills);

        // 3 Colonnes KDS
        var columns = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 12,
            Padding = new Thickness(12)
        };

        columns.Add(BuildKdsColumn("⏳ En Attente", AppleHigTheme.SystemOrange, _vm.PendingTickets), 0, 0);
        columns.Add(BuildKdsColumn("🔥 En Préparation", AppleHigTheme.SystemBlue, _vm.InPrepTickets), 1, 0);
        columns.Add(BuildKdsColumn("✅ Prêt à Servir", AppleHigTheme.SystemGreen, _vm.ReadyTickets), 2, 0);

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(topBar, 0),
                AddToGrid(header, 1),
                AddToGrid(columns, 2)
            }
        };
    }

    private static Button MakeStationFilterBtn(string text, bool isActive)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = isActive ? AppleHigTheme.SystemBlue : AppleHigTheme.TertiarySystemBackground,
            TextColor = isActive ? Colors.White : AppleHigTheme.LabelSecondary,
            FontSize = 13,
            FontAttributes = isActive ? FontAttributes.Bold : FontAttributes.None,
            HeightRequest = 34,
            CornerRadius = 17,
            Padding = new Thickness(14, 0),
            Command = new Command(() => AppleHigTheme.PerformHapticClick())
        };
    }

    private View BuildKdsColumn(string title, Color headerColor, System.Collections.ObjectModel.ObservableCollection<KdsTicketItemViewModel> items)
    {
        var colHeader = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Padding = new Thickness(14, 8),
            Margin = new Thickness(0, 0, 0, 8),
            Content = new Label
            {
                Text = title,
                TextColor = headerColor,
                FontSize = AppleHigTheme.Headline,
                FontAttributes = FontAttributes.Bold,
                HorizontalOptions = LayoutOptions.Center
            }
        };

        var list = new CollectionView
        {
            ItemsSource = items,
            ItemTemplate = new DataTemplate(() => BuildTicketCard(headerColor))
        };

        return new Grid
        {
            BackgroundColor = AppleHigTheme.SystemBackground,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(colHeader, 0),
                AddToGrid(list, 1)
            }
        };
    }

    private View BuildTicketCard(Color accentColor)
    {
        var border = new Border
        {
            Margin = new Thickness(0, 4, 0, 10),
            Padding = new Thickness(16),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusLarge) },
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            BackgroundColor = AppleHigTheme.SecondarySystemBackground
        };

        var tableLabel = new Label
        {
            FontSize = AppleHigTheme.Title2,
            FontAttributes = FontAttributes.Bold,
            TextColor = AppleHigTheme.LabelPrimary
        };
        tableLabel.SetBinding(Label.TextProperty, new Binding("Ticket.TableNumber"));

        var timerLabel = new Label
        {
            FontSize = AppleHigTheme.Subheadline,
            FontAttributes = FontAttributes.Bold
        };
        timerLabel.SetBinding(Label.TextProperty, nameof(KdsTicketItemViewModel.ElapsedTimeText));
        timerLabel.SetBinding(Label.TextColorProperty, new Binding(
            nameof(KdsTicketItemViewModel.UrgencyColorHex),
            converter: new HexToColorConverter()));

        var urgencyBar = new BoxView { HeightRequest = 3, CornerRadius = 1.5f };
        urgencyBar.SetBinding(BoxView.ColorProperty, new Binding(
            nameof(KdsTicketItemViewModel.UrgencyColorHex),
            converter: new HexToColorConverter()));

        var itemsList = new VerticalStackLayout { Spacing = 6, Margin = new Thickness(0, 6) };
        itemsList.SetBinding(BindableLayout.ItemsSourceProperty, "Ticket.Items");
        BindableLayout.SetItemTemplate(itemsList, new DataTemplate(() =>
        {
            var row = new HorizontalStackLayout { Spacing = 10 };
            var qty = new Label
            {
                FontSize = AppleHigTheme.Headline,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppleHigTheme.SystemOrange,
                VerticalOptions = LayoutOptions.Center
            };
            qty.SetBinding(Label.TextProperty, new Binding("Quantity", stringFormat: "{0}x"));

            var name = new Label
            {
                FontSize = AppleHigTheme.Headline,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppleHigTheme.LabelPrimary,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            };
            name.SetBinding(Label.TextProperty, new Binding("ProductName"));

            row.Add(qty);
            row.Add(name);
            return row;
        }));

        var bumpBtn = new Button
        {
            Text = "BUMP ▶",
            BackgroundColor = accentColor,
            TextColor = Colors.White,
            FontSize = AppleHigTheme.Headline,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 50,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = 12,
            Margin = new Thickness(0, 8, 0, 0)
        };
        bumpBtn.Clicked += (s, e) =>
        {
            AppleHigTheme.PerformHapticSuccess();
            if (bumpBtn.BindingContext is KdsTicketItemViewModel vmItem)
            {
                _vm.BumpTicketCommand.Execute(vmItem);
            }
        };

        var recallBtn = new Button
        {
            Text = "↩ Rappel",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelSecondary,
            FontSize = AppleHigTheme.Footnote,
            HeightRequest = 38,
            CornerRadius = 8
        };
        recallBtn.Clicked += (s, e) =>
        {
            AppleHigTheme.PerformHapticClick();
            if (recallBtn.BindingContext is KdsTicketItemViewModel vmItem)
            {
                _vm.RecallTicketCommand.Execute(vmItem);
            }
        };

        border.Content = new VerticalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    },
                    Children =
                    {
                        tableLabel,
                        timerLabel.Also(l => Grid.SetColumn(l, 1))
                    }
                },
                urgencyBar,
                itemsList,
                bumpBtn,
                recallBtn
            }
        };

        return border;
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
