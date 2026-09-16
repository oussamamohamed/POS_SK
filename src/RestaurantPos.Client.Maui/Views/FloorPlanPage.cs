#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Controls;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Plan de salle interactif conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Grille de tables tactiles avec fiches Inset Grouped, badges capsules de statut et modale Sheet Apple.
/// </summary>
public class FloorPlanPage : ContentPage
{
    private readonly FloorPlanViewModel _vm;

    public FloorPlanPage(FloorPlanViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        Title = "Plan de Salle";
        BackgroundColor = AppleHigTheme.SystemBackground;
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetNavBarIsVisible(this, false);
    }

    private void Build()
    {
        // 1. Barre de navigation supérieure globale
        var topBar = new GlobalHeaderView(PosActiveViewTab.Floor);

        // 2. Sous-barre : Section active + Bouton Nouvelle Table + Légende des statuts Apple
        var subHeader = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding = new Thickness(24, 14),
            BackgroundColor = AppleHigTheme.SystemBackground
        };

        var titleStack = new HorizontalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    TextColor = AppleHigTheme.LabelPrimary,
                    FontSize = AppleHigTheme.Title2,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                }.Also(l => l.SetBinding(Label.TextProperty, nameof(FloorPlanViewModel.ActiveSection))),
                new Button
                {
                    Text = "➕ Nouvelle Table",
                    BackgroundColor = AppleHigTheme.SystemBlue,
                    TextColor = Colors.White,
                    FontSize = AppleHigTheme.Subheadline,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 40,
                    MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
                    CornerRadius = 20,
                    Padding = new Thickness(16, 0),
                    Command = new Command(async () =>
                    {
                        var newTableNum = $"T{_vm.Tables.Count + 1:D2}";
                        _vm.Tables.Add(new DiningTable { TableNumber = newTableNum, Capacity = 4, Status = TableStatus.Free });
                        await DisplayAlert("Table Créée", $"Nouvelle table {newTableNum} ajoutée avec succès au plan de salle.", "OK");
                    })
                }
            }
        };
        Grid.SetColumn(titleStack, 0);

        var legend = new HorizontalStackLayout
        {
            Spacing = 16,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                MakeLegendItem(AppleHigTheme.SystemGreen, "Libre"),
                MakeLegendItem(AppleHigTheme.SystemOrange, "Occupée"),
                MakeLegendItem(AppleHigTheme.SystemRed, "Addition"),
                MakeLegendItem(AppleHigTheme.SystemIndigo, "Encaissée")
            }
        };
        Grid.SetColumn(legend, 1);

        subHeader.Add(titleStack);
        subHeader.Add(legend);

        // 3. Grille des tables (Cartes Inset Grouped)
        var tablesGrid = new CollectionView
        {
            ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 16,
                VerticalItemSpacing = 16
            },
            ItemTemplate = new DataTemplate(BuildTableCard),
            Margin = new Thickness(24, 8, 24, 24)
        };
        tablesGrid.SetBinding(CollectionView.ItemsSourceProperty, nameof(FloorPlanViewModel.Tables));

        // 4. Modale Sheet "Ouvrir table"
        var openTablePopup = BuildOpenTablePopup();
        openTablePopup.SetBinding(IsVisibleProperty, nameof(FloorPlanViewModel.IsTablePromptOpen));

        var mainLayout = new Grid
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
                AddToGrid(subHeader, 1),
                AddToGrid(tablesGrid, 2)
            }
        };

        // Overlay global
        Content = new Grid
        {
            Children =
            {
                mainLayout,
                openTablePopup
            }
        };
    }

    private View BuildTableCard()
    {
        var border = new Border
        {
            Padding = new Thickness(16),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusLarge) },
            StrokeThickness = 1,
            Stroke = AppleHigTheme.Separator,
            HeightRequest = 120,
            BackgroundColor = AppleHigTheme.SecondarySystemBackground
        };
        border.BindingContextChanged += (s, _) => ConfigureTableCard((BindableObject)s!);
        return border;
    }

    private void ConfigureTableCard(BindableObject bindable)
    {
        if (bindable is not Border border) return;
        if (border.BindingContext is not DiningTable table) return;

        var statusColor = table.Status switch
        {
            TableStatus.Free => AppleHigTheme.SystemGreen,
            TableStatus.Occupied => AppleHigTheme.SystemOrange,
            TableStatus.BillRequested => AppleHigTheme.SystemRed,
            _ => AppleHigTheme.SystemIndigo
        };

        var statusText = table.Status switch
        {
            TableStatus.Free => "Libre",
            TableStatus.Occupied => $"{table.CoversCount} couverts",
            TableStatus.BillRequested => "Addition",
            _ => "Encaissée"
        };

        border.BackgroundColor = AppleHigTheme.SecondarySystemBackground;
        border.Stroke = Color.FromRgba(statusColor.Red, statusColor.Green, statusColor.Blue, 0.4);

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
                        new Label
                        {
                            Text = table.TableNumber,
                            TextColor = AppleHigTheme.LabelPrimary,
                            FontSize = AppleHigTheme.Title3,
                            FontAttributes = FontAttributes.Bold,
                            VerticalOptions = LayoutOptions.Center
                        }.Also(l => Grid.SetColumn(l, 0)),
                        new Border
                        {
                            Padding = new Thickness(8, 2),
                            BackgroundColor = Color.FromRgba(statusColor.Red, statusColor.Green, statusColor.Blue, 0.18),
                            Stroke = Color.FromRgba(statusColor.Red, statusColor.Green, statusColor.Blue, 0.5),
                            StrokeThickness = 1,
                            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusPill) },
                            VerticalOptions = LayoutOptions.Center,
                            Content = new Label
                            {
                                Text = statusText,
                                TextColor = statusColor,
                                FontSize = AppleHigTheme.Caption1,
                                FontAttributes = FontAttributes.Bold
                            }
                        }.Also(b => Grid.SetColumn(b, 1))
                    }
                },
                new BoxView
                {
                    HeightRequest = 2,
                    CornerRadius = 1,
                    Color = Color.FromRgba(statusColor.Red, statusColor.Green, statusColor.Blue, 0.3)
                },
                new Label
                {
                    Text = table.AssignedWaiterName is not null ? $"👤 {table.AssignedWaiterName}" : "",
                    TextColor = AppleHigTheme.LabelSecondary,
                    FontSize = AppleHigTheme.Footnote
                }
            }
        };

        var tapGesture = new TapGestureRecognizer();
        tapGesture.Command = new Command(async () =>
        {
            await _vm.SelectTableAsync(table);
            if (table.Status != TableStatus.Free)
            {
                await Shell.Current.GoToAsync($"//pos?table={table.TableNumber}");
            }
        });
        border.GestureRecognizers.Clear();
        border.GestureRecognizers.Add(tapGesture);
    }

    private Grid BuildOpenTablePopup()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#AA000000"),
            IsVisible = false
        };
        overlay.SetBinding(IsVisibleProperty, nameof(FloorPlanViewModel.IsTablePromptOpen));

        var sheetCard = new Border
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            Stroke = AppleHigTheme.Separator,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(AppleHigTheme.CornerRadiusExtraLarge) },
            Padding = new Thickness(28, 20),
            WidthRequest = 400,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    AppleHigTheme.CreateSheetGrabber(),
                    new Label
                    {
                        Text = "Ouvrir la table",
                        TextColor = AppleHigTheme.LabelPrimary,
                        FontSize = AppleHigTheme.Title2,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "Nombre de couverts :",
                        TextColor = AppleHigTheme.LabelSecondary,
                        FontSize = AppleHigTheme.Body,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    BuildCoversSelector(),
                    AppleHigTheme.CreatePillButton("✔ Confirmer", AppleHigTheme.SystemGreen, Colors.White, new Command(async () =>
                    {
                        await _vm.ConfirmOpenTableAsync();
                        if (_vm.SelectedTable is not null)
                        {
                            await Shell.Current.GoToAsync($"//pos?table={_vm.SelectedTable.TableNumber}");
                        }
                    }), height: 50, fontSize: 17),
                    new Button
                    {
                        Text = "Annuler",
                        BackgroundColor = Colors.Transparent,
                        TextColor = AppleHigTheme.LabelSecondary,
                        HeightRequest = 44,
                        FontSize = AppleHigTheme.Headline,
                        Command = _vm.CancelTablePromptCommand
                    }
                }
            }
        };

        overlay.Children.Add(sheetCard);
        return overlay;
    }

    private Grid BuildCoversSelector()
    {
        var coversLabel = new Label
        {
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = AppleHigTheme.LargeTitle,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        coversLabel.SetBinding(Label.TextProperty, nameof(FloorPlanViewModel.CoversToOpen));

        var minusBtn = new Button
        {
            Text = "−",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 26,
            WidthRequest = 56,
            HeightRequest = 56,
            CornerRadius = 28,
            BorderColor = AppleHigTheme.Separator,
            BorderWidth = 1,
            Command = new Command(() => { if (_vm.CoversToOpen > 1) _vm.CoversToOpen--; })
        };

        var plusBtn = new Button
        {
            Text = "+",
            BackgroundColor = AppleHigTheme.SystemBlue,
            TextColor = Colors.White,
            FontSize = 26,
            WidthRequest = 56,
            HeightRequest = 56,
            CornerRadius = 28,
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

    private static HorizontalStackLayout MakeLegendItem(Color color, string label) =>
        new()
        {
            Spacing = 7,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new BoxView
                {
                    WidthRequest = 10,
                    HeightRequest = 10,
                    CornerRadius = 5,
                    Color = color,
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = label,
                    TextColor = AppleHigTheme.LabelSecondary,
                    FontSize = AppleHigTheme.Footnote,
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
