#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Plan de salle interactif — grille de tables avec statuts colores
/// et navigation vers le terminal POS.
/// </summary>
public class FloorPlanPage : ContentPage
{
    private readonly FloorPlanViewModel _vm;

    public FloorPlanPage(FloorPlanViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Plan de Salle";
        BackgroundColor = Color.FromArgb("#0F172A");
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, true);
    }

    private void Build()
    {
        // Barre du haut
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding = new Thickness(20, 12),
            BackgroundColor = Color.FromArgb("#1E293B")
        };

        var titleLabel = new Label
        {
            TextColor = Colors.White,
            FontSize = 22,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        titleLabel.SetBinding(Label.TextProperty, nameof(FloorPlanViewModel.ActiveSection));
        Grid.SetColumn(titleLabel, 0);

        var adminBtn = new Button
        {
            Text = "⚙ Back-Office",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 14,
            HeightRequest = 40,
            CornerRadius = 8,
            VerticalOptions = LayoutOptions.Center,
            Command = new Command(async () => await Shell.Current.GoToAsync("admin"))
        };
        Grid.SetColumn(adminBtn, 1);

        header.Add(titleLabel);
        header.Add(adminBtn);

        // Legende des statuts
        var legend = new HorizontalStackLayout
        {
            Spacing = 20,
            Padding = new Thickness(20, 8),
            BackgroundColor = Color.FromArgb("#0F172A"),
            Children =
            {
                MakeLegendItem("#10B981", "Libre"),
                MakeLegendItem("#F59E0B", "Occupee"),
                MakeLegendItem("#EF4444", "Addition"),
                MakeLegendItem("#6366F1", "Encaissee")
            }
        };

        // Grille des tables
        var tablesGrid = new CollectionView
        {
            ItemsLayout = new GridItemsLayout(3, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 12,
                VerticalItemSpacing = 12
            },
            ItemTemplate = new DataTemplate(BuildTableCard),
            Margin = new Thickness(16)
        };
        tablesGrid.SetBinding(CollectionView.ItemsSourceProperty, nameof(FloorPlanViewModel.Tables));

        // Popup "Ouvrir table"
        var openTablePopup = BuildOpenTablePopup();
        openTablePopup.SetBinding(IsVisibleProperty, nameof(FloorPlanViewModel.IsTablePromptOpen));

        // Bouton KDS Cuisine
        var kdsBtn = new Button
        {
            Text = "👨‍🍳 Ecran Cuisine (KDS)",
            BackgroundColor = Color.FromArgb("#1D4ED8"),
            TextColor = Colors.White,
            FontSize = 16,
            HeightRequest = 52,
            CornerRadius = 10,
            Margin = new Thickness(16, 0, 16, 16),
            Command = new Command(async () => await Shell.Current.GoToAsync("kds"))
        };

        Content = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children =
            {
                AddToGrid(header, 0),
                AddToGrid(legend, 1),
                AddToGrid(new ScrollView { Content = tablesGrid }, 2),
                AddToGrid(kdsBtn, 3)
            }
        };

        // Popup par-dessus tout
        var overlay = new Grid
        {
            Children =
            {
                Content!,
                openTablePopup
            }
        };
        Content = overlay;
    }

    private static View BuildTableCard() => new Frame
    {
        Padding = new Thickness(12),
        CornerRadius = 14,
        HasShadow = false
    };

    private void ConfigureTableCard(BindableObject bindable)
    {
        if (bindable is not Frame frame) return;
        if (frame.BindingContext is not DiningTable table) return;

        frame.BackgroundColor = table.Status switch
        {
            TableStatus.Free => Color.FromArgb("#064E3B"),
            TableStatus.Occupied => Color.FromArgb("#78350F"),
            TableStatus.BillRequested => Color.FromArgb("#7F1D1D"),
            _ => Color.FromArgb("#1E1B4B")
        };

        var statusColor = table.Status switch
        {
            TableStatus.Free => "#10B981",
            TableStatus.Occupied => "#F59E0B",
            TableStatus.BillRequested => "#EF4444",
            _ => "#6366F1"
        };

        frame.Content = new VerticalStackLayout
        {
            Spacing = 6,
            Children =
            {
                new Label
                {
                    Text = table.TableNumber,
                    TextColor = Colors.White,
                    FontSize = 20,
                    FontAttributes = FontAttributes.Bold
                },
                new BoxView
                {
                    HeightRequest = 3,
                    CornerRadius = 2,
                    Color = Color.FromArgb(statusColor)
                },
                new Label
                {
                    Text = table.Status switch
                    {
                        TableStatus.Free => "Libre",
                        TableStatus.Occupied => $"{table.CoversCount} couverts",
                        TableStatus.BillRequested => "Addition",
                        _ => "Encaissee"
                    },
                    TextColor = Color.FromArgb(statusColor),
                    FontSize = 13
                },
                new Label
                {
                    Text = table.AssignedWaiterName is not null ? $"👤 {table.AssignedWaiterName}" : "",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 12
                }
            }
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Command = new Command(async () =>
        {
            await _vm.SelectTableAsync(table);
            if (table.Status != TableStatus.Free)
            {
                await Shell.Current.GoToAsync($"pos?table={table.TableNumber}");
            }
        });
        frame.GestureRecognizers.Clear();
        frame.GestureRecognizers.Add(tapGesture);
    }

    private VerticalStackLayout BuildOpenTablePopup()
    {
        var popup = new Grid
        {
            BackgroundColor = Color.FromArgb("BB000000"),
            IsVisible = false
        };
        popup.SetBinding(IsVisibleProperty, nameof(FloorPlanViewModel.IsTablePromptOpen));

        var card = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            CornerRadius = 20,
            Padding = new Thickness(32),
            WidthRequest = 380,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = new VerticalStackLayout
            {
                Spacing = 20,
                Children =
                {
                    new Label
                    {
                        Text = "Ouvrir la table",
                        TextColor = Colors.White,
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold
                    },
                    new Label
                    {
                        Text = "Nombre de couverts :",
                        TextColor = Color.FromArgb("#94A3B8"),
                        FontSize = 16
                    },
                    BuildCoversSelector(),
                    new Button
                    {
                        Text = "✔ Confirmer",
                        BackgroundColor = Color.FromArgb("#10B981"),
                        TextColor = Colors.White,
                        HeightRequest = 52,
                        CornerRadius = 12,
                        FontSize = 18,
                        Command = _vm.ConfirmOpenTableCommand
                    },
                    new Button
                    {
                        Text = "Annuler",
                        BackgroundColor = Color.FromArgb("#334155"),
                        TextColor = Color.FromArgb("#94A3B8"),
                        HeightRequest = 44,
                        CornerRadius = 10,
                        FontSize = 16,
                        Command = _vm.CancelTablePromptCommand
                    }
                }
            }
        };

        var wrapper = new VerticalStackLayout
        {
            IsVisible = false,
            BackgroundColor = Color.FromArgb("BB000000"),
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { card }
        };
        wrapper.SetBinding(IsVisibleProperty, nameof(FloorPlanViewModel.IsTablePromptOpen));
        return wrapper;
    }

    private Grid BuildCoversSelector()
    {
        var coversLabel = new Label
        {
            TextColor = Colors.White,
            FontSize = 40,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center
        };
        coversLabel.SetBinding(Label.TextProperty, nameof(FloorPlanViewModel.CoversToOpen));

        var minusBtn = new Button
        {
            Text = "−",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontSize = 28,
            WidthRequest = 60,
            HeightRequest = 60,
            CornerRadius = 30,
            Command = new Command(() => { if (_vm.CoversToOpen > 1) _vm.CoversToOpen--; })
        };

        var plusBtn = new Button
        {
            Text = "+",
            BackgroundColor = Color.FromArgb("#3B82F6"),
            TextColor = Colors.White,
            FontSize = 28,
            WidthRequest = 60,
            HeightRequest = 60,
            CornerRadius = 30,
            Command = new Command(() => _vm.CoversToOpen++)
        };

        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                AddToGridCol(minusBtn, 0),
                AddToGridCol(coversLabel, 1),
                AddToGridCol(plusBtn, 2)
            }
        };
    }

    private static HorizontalStackLayout MakeLegendItem(string colorHex, string label) =>
        new()
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new BoxView
                {
                    WidthRequest = 12,
                    HeightRequest = 12,
                    CornerRadius = 6,
                    Color = Color.FromArgb(colorHex),
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = label,
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 13,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    private static T AddToGridCol<T>(T view, int col) where T : View
    {
        Grid.SetColumn(view, col);
        return view;
    }
}
#endif
