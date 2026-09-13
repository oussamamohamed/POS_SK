#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Terminal de caisse tactile principal.
/// Disposition 3 colonnes : Categories | Grille Articles | Panier.
/// </summary>
public class PosTerminalPage : ContentPage
{
    private readonly PosTerminalViewModel _vm;

    public PosTerminalPage(PosTerminalViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // Recuperer le parametre de table de la navigation
        if (Shell.Current?.CurrentState.Location.OriginalString.Contains("table=") == true)
        {
            // Table deja configuree par FloorPlanPage via LoadActiveTableOrderAsync
        }
    }

    private void Build()
    {
        Content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(220) },  // Colonne categ
                new ColumnDefinition { Width = GridLength.Star },       // Grille articles
                new ColumnDefinition { Width = new GridLength(360) }   // Panier
            },
            ColumnSpacing = 0
        };

        ((Grid)Content).Add(BuildCategoryPanel(), 0, 0);
        ((Grid)Content).Add(BuildProductGrid(), 1, 0);
        ((Grid)Content).Add(BuildCartPanel(), 2, 0);
    }

    // ---- PANNEAU CATEGORIES (colonne gauche) ----
    private View BuildCategoryPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            }
        };

        // Header
        var header = new Label
        {
            Text = "🍽 Menu",
            TextColor = Colors.White,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            Padding = new Thickness(16, 20)
        };
        Grid.SetRow(header, 0);

        // Liste des categories
        var catList = new CollectionView
        {
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var frame = new Frame
                {
                    Padding = new Thickness(12, 14),
                    Margin = new Thickness(8, 4),
                    CornerRadius = 10,
                    HasShadow = false
                };

                var label = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                };
                label.SetBinding(Label.TextProperty, "Name");

                frame.Content = label;

                var tapGesture = new TapGestureRecognizer();
                tapGesture.SetBinding(TapGestureRecognizer.CommandProperty,
                    new Binding(nameof(PosTerminalViewModel.SelectCategoryCommand),
                    source: _vm));
                tapGesture.SetBinding(TapGestureRecognizer.CommandParameterProperty,
                    new Binding("."));
                frame.GestureRecognizers.Add(tapGesture);

                return frame;
            })
        };
        catList.SetBinding(CollectionView.ItemsSourceProperty, nameof(PosTerminalViewModel.Categories));
        catList.SetBinding(CollectionView.SelectedItemProperty, nameof(PosTerminalViewModel.SelectedCategory));
        Grid.SetRow(catList, 1);

        // Bouton retour salle
        var backBtn = new Button
        {
            Text = "← Salle",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 14,
            HeightRequest = 48,
            CornerRadius = 0,
            Command = new Command(async () => await Shell.Current.GoToAsync("//floor"))
        };

        panel.Add(header);
        panel.Add(catList);
        panel.Add(new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            },
            Children =
            {
                AddToGrid(catList, 0),
                AddToGrid(backBtn, 1)
            }
        });
        Grid.SetRow(panel.Children[2], 1);

        return panel;
    }

    // ---- GRILLE ARTICLES (colonne centrale) ----
    private View BuildProductGrid()
    {
        var wrapper = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            }
        };

        // Barre table + info
        var tableBar = new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(16, 10),
            BackgroundColor = Color.FromArgb("#1E293B"),
            Children =
            {
                new Label
                {
                    Text = "Table :",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 14,
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    TextColor = Colors.White,
                    FontSize = 16,
                    FontAttributes = FontAttributes.Bold,
                    VerticalOptions = LayoutOptions.Center
                }.Also(l => l.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.ActiveTable)))
            }
        };
        Grid.SetRow(tableBar, 0);

        // Grille d'articles
        var productsGrid = new CollectionView
        {
            ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 8,
                VerticalItemSpacing = 8
            },
            ItemTemplate = new DataTemplate(() =>
            {
                var frame = new Frame
                {
                    Padding = new Thickness(10),
                    CornerRadius = 8,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#1E293B")
                };

                var name = new Label
                {
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 2
                };
                name.SetBinding(Label.TextProperty, "Name");

                var price = new Label
                {
                    FontSize = 16,
                    TextColor = Color.FromArgb("#10B981"),
                    FontAttributes = FontAttributes.Bold
                };
                price.SetBinding(Label.TextProperty, "Price",
                    stringFormat: "{0:F2} €");

                frame.Content = new VerticalStackLayout
                {
                    Spacing = 4,
                    Children = { name, price }
                };

                var tap = new TapGestureRecognizer();
                tap.SetBinding(TapGestureRecognizer.CommandProperty,
                    new Binding(nameof(PosTerminalViewModel.AddProductCommand), source: _vm));
                tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
                frame.GestureRecognizers.Add(tap);

                return frame;
            }),
            Margin = new Thickness(12)
        };
        productsGrid.SetBinding(CollectionView.ItemsSourceProperty,
            nameof(PosTerminalViewModel.AvailableProducts));
        Grid.SetRow(productsGrid, 1);

        wrapper.Add(tableBar);
        wrapper.Add(productsGrid);

        return wrapper;
    }

    // ---- PANIER (colonne droite) ----
    private View BuildCartPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        // Header panier
        var header = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Padding = new Thickness(16, 14),
            BackgroundColor = Color.FromArgb("#0F172A")
        };
        var cartTitle = new Label
        {
            Text = "🛒 Commande",
            TextColor = Colors.White,
            FontSize = 18,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center
        };
        var clearBtn = new Button
        {
            Text = "Vider",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Color.FromArgb("#EF4444"),
            FontSize = 13,
            HeightRequest = 36,
            CornerRadius = 8,
            Command = _vm.ClearCartCommand
        };
        Grid.SetColumn(clearBtn, 1);
        header.Add(cartTitle);
        header.Add(clearBtn);
        Grid.SetRow(header, 0);

        // Alerte conflit
        var conflictAlert = new Label
        {
            TextColor = Color.FromArgb("#F59E0B"),
            FontSize = 13,
            Margin = new Thickness(12, 4),
            IsVisible = false
        };
        conflictAlert.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.ConflictAlertBanner));
        conflictAlert.SetBinding(IsVisibleProperty, new Binding(
            nameof(PosTerminalViewModel.ConflictAlertBanner),
            converter: new StringToBoolConverter()));

        // Liste articles du panier
        var cartList = new CollectionView
        {
            ItemTemplate = new DataTemplate(() =>
            {
                var row = new Grid
                {
                    Padding = new Thickness(12, 8),
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = new GridLength(100) }
                    }
                };

                var nameLabel = new Label
                {
                    FontSize = 14,
                    TextColor = Colors.White,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                nameLabel.SetBinding(Label.TextProperty, "ProductName");

                var priceLabel = new Label
                {
                    FontSize = 14,
                    TextColor = Color.FromArgb("#10B981"),
                    HorizontalOptions = LayoutOptions.End
                };
                priceLabel.SetBinding(Label.TextProperty, "TotalTtc",
                    stringFormat: "{0:F2} €");

                var qtyRow = new HorizontalStackLayout
                {
                    Spacing = 8,
                    Children =
                    {
                        new Button
                        {
                            Text = "−",
                            BackgroundColor = Color.FromArgb("#334155"),
                            TextColor = Colors.White,
                            WidthRequest = 30,
                            HeightRequest = 30,
                            CornerRadius = 6,
                            FontSize = 14,
                            Padding = 0
                        }.Also(b =>
                        {
                            b.SetBinding(Button.CommandProperty,
                                new Binding(nameof(PosTerminalViewModel.DecrementQuantityCommand), source: _vm));
                            b.SetBinding(Button.CommandParameterProperty, new Binding("."));
                        }),
                        new Label
                        {
                            FontSize = 14,
                            TextColor = Colors.White,
                            VerticalOptions = LayoutOptions.Center
                        }.Also(l => l.SetBinding(Label.TextProperty, "Quantity")),
                        new Button
                        {
                            Text = "+",
                            BackgroundColor = Color.FromArgb("#3B82F6"),
                            TextColor = Colors.White,
                            WidthRequest = 30,
                            HeightRequest = 30,
                            CornerRadius = 6,
                            FontSize = 14,
                            Padding = 0
                        }.Also(b =>
                        {
                            b.SetBinding(Button.CommandProperty,
                                new Binding(nameof(PosTerminalViewModel.IncrementQuantityCommand), source: _vm));
                            b.SetBinding(Button.CommandParameterProperty, new Binding("."));
                        })
                    }
                };

                var leftCol = new VerticalStackLayout { Spacing = 4, Children = { nameLabel, qtyRow } };
                Grid.SetColumn(leftCol, 0);
                Grid.SetColumn(priceLabel, 1);
                row.Add(leftCol);
                row.Add(priceLabel);

                return new Frame
                {
                    Padding = 0,
                    Margin = new Thickness(8, 2),
                    CornerRadius = 8,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#0F172A"),
                    Content = row
                };
            })
        };
        cartList.SetBinding(CollectionView.ItemsSourceProperty, nameof(PosTerminalViewModel.CartItems));

        var cartScroll = new VerticalStackLayout
        {
            Children = { conflictAlert, cartList }
        };
        Grid.SetRow(cartScroll, 1);

        // Total TTC
        var totalRow = new Grid
        {
            Padding = new Thickness(16, 12),
            BackgroundColor = Color.FromArgb("#0F172A"),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };
        var totalLabel = new Label
        {
            Text = "TOTAL TTC",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center
        };
        var totalAmount = new Label
        {
            FontSize = 28,
            FontAttributes = FontAttributes.Bold,
            TextColor = Color.FromArgb("#10B981")
        };
        totalAmount.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.TotalTtc),
            stringFormat: "{0:F2} €");
        Grid.SetColumn(totalAmount, 1);
        totalRow.Add(totalLabel);
        totalRow.Add(totalAmount);
        Grid.SetRow(totalRow, 2);

        // Boutons action
        var actionRow = new Grid
        {
            Padding = new Thickness(8),
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            }
        };

        var sendBtn = new Button
        {
            Text = "📤 Envoyer Cuisine",
            BackgroundColor = Color.FromArgb("#059669"),
            TextColor = Colors.White,
            FontSize = 15,
            HeightRequest = 56,
            CornerRadius = 12,
            Command = _vm.SendKitchenAndResetCommand
        };

        var checkoutBtn = new Button
        {
            Text = "💳 Encaisser",
            BackgroundColor = Color.FromArgb("#7C3AED"),
            TextColor = Colors.White,
            FontSize = 15,
            HeightRequest = 56,
            CornerRadius = 12,
            Command = new Command(async () => await Shell.Current.GoToAsync("checkout"))
        };
        Grid.SetColumn(checkoutBtn, 1);

        actionRow.Add(sendBtn);
        actionRow.Add(checkoutBtn);
        Grid.SetRow(actionRow, 3);

        panel.Add(header);
        panel.Add(cartScroll);
        panel.Add(totalRow);
        panel.Add(actionRow);

        return panel;
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    private class StringToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is string s && !string.IsNullOrEmpty(s);
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}

// Extension helper pour la lisibilite du builder pattern
internal static class ViewExtensions
{
    public static T Also<T>(this T view, Action<T> configure) where T : BindableObject
    {
        configure(view);
        return view;
    }
}
#endif
