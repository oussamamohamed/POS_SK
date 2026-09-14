#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Controls;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Terminal de caisse tactile principal répliquant fidèlement le layout web (posView).
/// Disposition 2 colonnes : Panier / Ticket (Gauche 380px) | Catalogue & Touches Rapides (Droite Star).
/// </summary>
public class PosTerminalPage : ContentPage, IQueryAttributable
{
    private readonly PosTerminalViewModel _vm;
    private Grid? _discountModal;
    private Grid? _transferModal;

    public PosTerminalPage(PosTerminalViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = Color.FromArgb("#0F172A");
        Shell.SetNavBarIsVisible(this, false);
        Build();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("table", out var tableObj) && tableObj is string tableNumber && !string.IsNullOrWhiteSpace(tableNumber))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await _vm.LoadActiveTableOrderAsync(tableNumber);
            });
        }
    }

    private void Build()
    {
        var topBar = new GlobalHeaderView(PosActiveViewTab.Pos);

        var bodyGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = new GridLength(380) }, // Panier / Ticket (Gauche, 380px comme en web)
                new ColumnDefinition { Width = GridLength.Star }       // Catalogue Articles (Droite)
            },
            ColumnSpacing = 0
        };

        var cartPanel = BuildCartPanel();
        Grid.SetColumn(cartPanel, 0);
        Grid.SetRow(cartPanel, 0);

        var catalogPanel = BuildCatalogPanel();
        Grid.SetColumn(catalogPanel, 1);
        Grid.SetRow(catalogPanel, 0);

        bodyGrid.Children.Add(cartPanel);
        bodyGrid.Children.Add(catalogPanel);

        var mainLayout = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(topBar, 0),
                AddToGrid(bodyGrid, 1)
            }
        };

        // Modales overlay (Remise & Transfert)
        _discountModal = BuildDiscountModal();
        _transferModal = BuildTransferModal();

        Content = new Grid
        {
            Children =
            {
                mainLayout,
                _discountModal,
                _transferModal
            }
        };
    }

    // =========================================================================
    // PANNEAU GAUCHE : PANIER / TICKET (380px) — MATCH 1:1 AVEC pos-cart-panel WEB
    // =========================================================================
    private View BuildCartPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Header
                new RowDefinition { Height = GridLength.Star }, // Items list
                new RowDefinition { Height = GridLength.Auto }  // Summary & Actions
            }
        };

        // 1. Cart Header
        var headerGrid = new Grid
        {
            Padding = new Thickness(16, 12),
            BackgroundColor = Color.FromArgb("#0F172A"),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            }
        };

        var tableInfoStack = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                // Table Badge
                new Frame
                {
                    Padding = new Thickness(8, 4),
                    CornerRadius = 6,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#3B82F6"),
                    Content = new Label
                    {
                        TextColor = Colors.White,
                        FontSize = 13,
                        FontAttributes = FontAttributes.Bold,
                        VerticalOptions = LayoutOptions.Center
                    }.Also(l => l.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.ActiveTable)))
                },
                // Destination Toggles (🥡 À Emporter / 🍽️ Sur Place)
                new Button
                {
                    Text = "🥡 Emporté",
                    FontSize = 11,
                    HeightRequest = 32,
                    CornerRadius = 6,
                    Padding = new Thickness(8, 0),
                    Command = new Command(() => _vm.SetDestination("Takeaway"))
                }.Also(b =>
                {
                    b.SetBinding(Button.BackgroundColorProperty, new Binding(nameof(PosTerminalViewModel.Destination),
                        converter: new DestinationToColorConverter("Takeaway")));
                    b.SetBinding(Button.TextColorProperty, new Binding(nameof(PosTerminalViewModel.Destination),
                        converter: new DestinationToTextColorConverter("Takeaway")));
                }),
                new Button
                {
                    Text = "🍽️ Sur Place",
                    FontSize = 11,
                    HeightRequest = 32,
                    CornerRadius = 6,
                    Padding = new Thickness(8, 0),
                    Command = new Command(() => _vm.SetDestination("EatIn"))
                }.Also(b =>
                {
                    b.SetBinding(Button.BackgroundColorProperty, new Binding(nameof(PosTerminalViewModel.Destination),
                        converter: new DestinationToColorConverter("EatIn")));
                    b.SetBinding(Button.TextColorProperty, new Binding(nameof(PosTerminalViewModel.Destination),
                        converter: new DestinationToTextColorConverter("EatIn")));
                }),
                // Covers Badge
                new Label
                {
                    Text = "👥 2 Couverts",
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 12,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
        Grid.SetColumn(tableInfoStack, 0);

        var headerControls = new HorizontalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                // Bouton File d'Attente (Parked Orders)
                new Button
                {
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Color.FromArgb("#F59E0B"),
                    FontSize = 12,
                    HeightRequest = 32,
                    CornerRadius = 6,
                    Padding = new Thickness(8, 0),
                    Command = _vm.RecallHeldOrderCommand
                }.Also(b => b.SetBinding(Button.TextProperty, new Binding(nameof(PosTerminalViewModel.HeldOrdersCount), stringFormat: "⏸️ {0}"))),
                // Vider le Panier
                new Button
                {
                    Text = "🗑️",
                    BackgroundColor = Colors.Transparent,
                    TextColor = Color.FromArgb("#EF4444"),
                    FontSize = 16,
                    WidthRequest = 36,
                    HeightRequest = 32,
                    Padding = 0,
                    Command = _vm.ClearCartCommand
                }
            }
        };
        Grid.SetColumn(headerControls, 1);

        headerGrid.Add(tableInfoStack);
        headerGrid.Add(headerControls);
        Grid.SetRow(headerGrid, 0);

        // 2. Alerte Conflit Banner
        var conflictAlert = new Label
        {
            TextColor = Color.FromArgb("#F59E0B"),
            FontSize = 12,
            Margin = new Thickness(12, 4),
            IsVisible = false
        };
        conflictAlert.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.ConflictAlertBanner));
        conflictAlert.SetBinding(IsVisibleProperty, new Binding(
            nameof(PosTerminalViewModel.ConflictAlertBanner),
            converter: new StringToBoolConverter()));

        // 3. Cart Items List
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

                var statusBadge = new Label
                {
                    FontSize = 10,
                    FontAttributes = FontAttributes.Bold
                };
                statusBadge.SetBinding(Label.TextProperty, new Binding("IsDispatched", converter: new DispatchedToTextConverter()));
                statusBadge.SetBinding(Label.TextColorProperty, new Binding("IsDispatched", converter: new DispatchedToColorConverter()));

                var nameLabel = new Label
                {
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                nameLabel.SetBinding(Label.TextProperty, "ProductName");

                var priceLabel = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#10B981"),
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center
                };
                priceLabel.SetBinding(Label.TextProperty, "TotalTtc", stringFormat: "{0:F2} €");

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
                            WidthRequest = 28,
                            HeightRequest = 28,
                            CornerRadius = 6,
                            FontSize = 14,
                            Padding = 0
                        }.Also(b =>
                        {
                            b.SetBinding(Button.CommandProperty, new Binding(nameof(PosTerminalViewModel.DecrementQuantityCommand), source: _vm));
                            b.SetBinding(Button.CommandParameterProperty, new Binding("."));
                        }),
                        new Label
                        {
                            FontSize = 14,
                            TextColor = Colors.White,
                            FontAttributes = FontAttributes.Bold,
                            VerticalOptions = LayoutOptions.Center
                        }.Also(l => l.SetBinding(Label.TextProperty, "Quantity")),
                        new Button
                        {
                            Text = "+",
                            BackgroundColor = Color.FromArgb("#3B82F6"),
                            TextColor = Colors.White,
                            WidthRequest = 28,
                            HeightRequest = 28,
                            CornerRadius = 6,
                            FontSize = 14,
                            Padding = 0
                        }.Also(b =>
                        {
                            b.SetBinding(Button.CommandProperty, new Binding(nameof(PosTerminalViewModel.IncrementQuantityCommand), source: _vm));
                            b.SetBinding(Button.CommandParameterProperty, new Binding("."));
                        })
                    }
                };

                var leftCol = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children = { statusBadge, nameLabel, qtyRow }
                };
                Grid.SetColumn(leftCol, 0);
                Grid.SetColumn(priceLabel, 1);
                row.Add(leftCol);
                row.Add(priceLabel);

                var frame = new Frame
                {
                    Padding = 0,
                    Margin = new Thickness(8, 3),
                    CornerRadius = 8,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#0F172A"),
                    Content = row
                };
                frame.SetBinding(Microsoft.Maui.Controls.Frame.BorderColorProperty, new Binding("IsDispatched", converter: new DispatchedToBorderConverter()));
                return frame;
            })
        };
        cartList.SetBinding(CollectionView.ItemsSourceProperty, nameof(PosTerminalViewModel.CartItems));

        var cartListWrapper = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            },
            Children =
            {
                AddToGrid(conflictAlert, 0),
                AddToGrid(cartList, 1)
            }
        };
        Grid.SetRow(cartListWrapper, 1);

        // 4. Cart Summary & 6 Action Buttons
        var bottomSummary = BuildCartSummary();
        Grid.SetRow(bottomSummary, 2);

        panel.Add(headerGrid);
        panel.Add(cartListWrapper);
        panel.Add(bottomSummary);

        return panel;
    }

    private View BuildCartSummary()
    {
        var summaryLayout = new VerticalStackLayout
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = new Thickness(14, 10),
            Spacing = 6
        };

        // Ligne HT
        var htRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
            Children =
            {
                new Label { Text = "Total HT :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 },
                new Label { TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 }.Also(l =>
                {
                    Grid.SetColumn(l, 1);
                    l.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.TotalHt), stringFormat: "{0:F2} €");
                })
            }
        };

        // Ligne TVA
        var vatRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
            Children =
            {
                new Label { Text = "TVA (10% / 20%) :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 },
                new Label { TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 }.Also(l =>
                {
                    Grid.SetColumn(l, 1);
                    l.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.TotalVat), stringFormat: "{0:F2} €");
                })
            }
        };

        // Ligne TOTAL TTC
        var ttcRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
            Padding = new Thickness(0, 4, 0, 4),
            Children =
            {
                new Label { Text = "TOTAL TTC", TextColor = Colors.White, FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                new Label
                {
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#10B981"),
                    HorizontalOptions = LayoutOptions.End
                }.Also(l =>
                {
                    Grid.SetColumn(l, 1);
                    l.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.TotalTtc), stringFormat: "{0:F2} €");
                })
            }
        };

        // Fast Cash Shortcuts Bar (10 €, 20 €, 50 €, Exact)
        var fastCashBar = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            ColumnSpacing = 6,
            Children =
            {
                MakeFastCashBtn("10 €", 0, new Command(async () => await ExecuteFastCash(10m))),
                MakeFastCashBtn("20 €", 1, new Command(async () => await ExecuteFastCash(20m))),
                MakeFastCashBtn("50 €", 2, new Command(async () => await ExecuteFastCash(50m))),
                MakeFastCashBtn("Exact", 3, new Command(async () => await ExecuteFastCash(null)))
            }
        };

        // 6 Action Buttons (Grille 2x3 identique au web)
        var actionButtonsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Star }
            },
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            ColumnSpacing = 6,
            RowSpacing = 6,
            Margin = new Thickness(0, 6, 0, 0)
        };

        // Row 0
        var btnKitchen = MakeCartActionBtn("📤 Cuisine", "#3B82F6", new Command(async () =>
        {
            var tableNum = _vm.ActiveTable;
            await _vm.SendKitchenAndResetAsync();
            var floorVm = Handler?.MauiContext?.Services.GetService<FloorPlanViewModel>();
            floorVm?.SetTableStatus(tableNum, TableStatus.Occupied, 2);
            await Shell.Current.GoToAsync("//floor");
        }));
        Grid.SetRow(btnKitchen, 0); Grid.SetColumn(btnKitchen, 0);

        var btnHold = MakeCartActionBtn("⏸️ Attente", "#475569", _vm.HoldCurrentCartCommand);
        Grid.SetRow(btnHold, 0); Grid.SetColumn(btnHold, 1);

        var btnDiscount = MakeCartActionBtn("🏷️ Remise", "#8B5CF6", new Command(() =>
        {
            if (_discountModal != null) _discountModal.IsVisible = true;
        }));
        Grid.SetRow(btnDiscount, 0); Grid.SetColumn(btnDiscount, 2);

        // Row 1
        var btnTransfer = MakeCartActionBtn("🔄 Transférer", "#06B6D4", new Command(() =>
        {
            if (_transferModal != null) _transferModal.IsVisible = true;
        }));
        Grid.SetRow(btnTransfer, 1); Grid.SetColumn(btnTransfer, 0);

        var btnSplit = MakeCartActionBtn("➗ Split", "#F59E0B", new Command(async () =>
        {
            var splitVm = Handler?.MauiContext?.Services.GetService<SplitBillViewModel>();
            splitVm?.Initialize(_vm.TotalTtc.AmountInCents, 2);
            await Shell.Current.GoToAsync($"split?orderId={_vm.ActiveOrder.Id}&totalCents={_vm.TotalTtc.AmountInCents}&tableNumber={_vm.ActiveTable}");
        }));
        Grid.SetRow(btnSplit, 1); Grid.SetColumn(btnSplit, 1);

        var btnPay = MakeCartActionBtn("💳 Encaisser", "#10B981", new Command(async () =>
        {
            var checkoutVm = Handler?.MauiContext?.Services.GetService<CheckoutViewModel>();
            if (checkoutVm != null)
            {
                checkoutVm.Initialize(_vm.ActiveOrder.Id, _vm.TotalTtc.AmountInCents);
            }
            await Shell.Current.GoToAsync($"checkout?orderId={_vm.ActiveOrder.Id}&totalCents={_vm.TotalTtc.AmountInCents}&tableNumber={_vm.ActiveTable}");
        }));
        Grid.SetRow(btnPay, 1); Grid.SetColumn(btnPay, 2);

        actionButtonsGrid.Add(btnKitchen);
        actionButtonsGrid.Add(btnHold);
        actionButtonsGrid.Add(btnDiscount);
        actionButtonsGrid.Add(btnTransfer);
        actionButtonsGrid.Add(btnSplit);
        actionButtonsGrid.Add(btnPay);

        summaryLayout.Add(htRow);
        summaryLayout.Add(vatRow);
        summaryLayout.Add(ttcRow);
        summaryLayout.Add(fastCashBar);
        summaryLayout.Add(actionButtonsGrid);

        return summaryLayout;
    }

    private static Button MakeFastCashBtn(string text, int col, Command command)
    {
        var btn = new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 34,
            CornerRadius = 6,
            Padding = 0,
            Command = command
        };
        Grid.SetColumn(btn, col);
        return btn;
    }

    private static Button MakeCartActionBtn(string text, string colorHex, System.Windows.Input.ICommand command)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb(colorHex),
            TextColor = Colors.White,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            CornerRadius = 8,
            Padding = 0,
            Command = command
        };
    }

    private async Task ExecuteFastCash(decimal? givenAmount)
    {
        if (_vm.CartItems.Count == 0) return;
        var checkoutVm = Handler?.MauiContext?.Services.GetService<CheckoutViewModel>();
        if (checkoutVm != null)
        {
            checkoutVm.Initialize(_vm.ActiveOrder.Id, _vm.TotalTtc.AmountInCents);
        }
        await Shell.Current.GoToAsync($"checkout?orderId={_vm.ActiveOrder.Id}&totalCents={_vm.TotalTtc.AmountInCents}&tableNumber={_vm.ActiveTable}");
    }

    // =========================================================================
    // PANNEAU DROIT : CATALOGUE, TOUCHES RAPIDES, ONGLETS & GRILLE (pos-catalog-panel)
    // =========================================================================
    private View BuildCatalogPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = Color.FromArgb("#0F172A"),
            Padding = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Touches Rapides (Rush Bar)
                new RowDefinition { Height = GridLength.Auto }, // Onglets Familles Horizontaux
                new RowDefinition { Height = GridLength.Star }, // Grille Articles
                new RowDefinition { Height = GridLength.Auto }  // Barre Pagination
            },
            RowSpacing = 10
        };

        // 1. Touches Rapides ("Coup de feu")
        var quickKeysBar = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "⚡ Rapide :", TextColor = Color.FromArgb("#F59E0B"), FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                MakeQuickKeyBtn("☕ Café", () => AddProductByName("Café")),
                MakeQuickKeyBtn("🍔 Burger", () => AddProductByName("Burger")),
                MakeQuickKeyBtn("💧 Eau", () => AddProductByName("Eau")),
                MakeQuickKeyBtn("🍺 Bière", () => AddProductByName("Bière"))
            }
        };
        Grid.SetRow(quickKeysBar, 0);

        // 2. Onglets Catégories Horizontaux (Matching category-tabs-bar)
        var categoryTabs = new CollectionView
        {
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Horizontal) { ItemSpacing = 8 },
            SelectionMode = SelectionMode.Single,
            ItemTemplate = new DataTemplate(() =>
            {
                var frame = new Frame
                {
                    Padding = new Thickness(16, 8),
                    CornerRadius = 8,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    BorderColor = Color.FromArgb("#334155")
                };

                var label = new Label
                {
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    VerticalOptions = LayoutOptions.Center
                };
                label.SetBinding(Label.TextProperty, "Name");
                frame.Content = label;

                var tap = new TapGestureRecognizer();
                tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(PosTerminalViewModel.SelectCategoryCommand), source: _vm));
                tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
                frame.GestureRecognizers.Add(tap);

                return frame;
            }),
            HeightRequest = 48
        };
        categoryTabs.SetBinding(CollectionView.ItemsSourceProperty, nameof(PosTerminalViewModel.Categories));
        categoryTabs.SetBinding(CollectionView.SelectedItemProperty, nameof(PosTerminalViewModel.SelectedCategory));
        Grid.SetRow(categoryTabs, 1);

        // 3. Grille Articles (products-grid)
        var productsGrid = new CollectionView
        {
            ItemsLayout = new GridItemsLayout(4, ItemsLayoutOrientation.Vertical)
            {
                HorizontalItemSpacing = 10,
                VerticalItemSpacing = 10
            },
            ItemTemplate = new DataTemplate(() =>
            {
                var frame = new Frame
                {
                    Padding = new Thickness(12),
                    CornerRadius = 8,
                    HasShadow = false,
                    BackgroundColor = Color.FromArgb("#1E293B"),
                    BorderColor = Color.FromRgba(255, 255, 255, 25)
                };

                var name = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    LineBreakMode = LineBreakMode.WordWrap,
                    MaxLines = 2
                };
                name.SetBinding(Label.TextProperty, "Name");

                var price = new Label
                {
                    FontSize = 17,
                    TextColor = Color.FromArgb("#10B981"),
                    FontAttributes = FontAttributes.Bold
                };
                price.SetBinding(Label.TextProperty, "Price", stringFormat: "{0:F2} €");

                frame.Content = new VerticalStackLayout
                {
                    Spacing = 6,
                    Children = { name, price },
                    InputTransparent = true
                };

                var tap = new TapGestureRecognizer();
                tap.SetBinding(TapGestureRecognizer.CommandProperty, new Binding(nameof(PosTerminalViewModel.AddProductCommand), source: _vm));
                tap.SetBinding(TapGestureRecognizer.CommandParameterProperty, new Binding("."));
                frame.GestureRecognizers.Add(tap);

                return frame;
            })
        };
        productsGrid.SetBinding(CollectionView.ItemsSourceProperty, nameof(PosTerminalViewModel.AvailableProducts));
        Grid.SetRow(productsGrid, 2);

        // 4. Barre Pagination (grid-pagination-bar)
        var paginationBar = new Grid
        {
            Padding = new Thickness(12, 6),
            BackgroundColor = Color.FromArgb("#1E293B"),
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children =
            {
                new Button
                {
                    Text = "◀ Précédent",
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 36,
                    CornerRadius = 6
                }.Also(b => Grid.SetColumn(b, 0)),
                new Label
                {
                    Text = "Page 1 / 1",
                    TextColor = Colors.White,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }.Also(l => Grid.SetColumn(l, 1)),
                new Button
                {
                    Text = "Suivant ▶",
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Color.FromArgb("#94A3B8"),
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 36,
                    CornerRadius = 6
                }.Also(b => Grid.SetColumn(b, 2))
            }
        };
        Grid.SetRow(paginationBar, 3);

        panel.Add(quickKeysBar);
        panel.Add(categoryTabs);
        panel.Add(productsGrid);
        panel.Add(paginationBar);

        return panel;
    }

    private static Button MakeQuickKeyBtn(string text, Action onTapped)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromRgba(245, 158, 11, 40),
            TextColor = Color.FromArgb("#FEF08A"),
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 34,
            CornerRadius = 6,
            Padding = new Thickness(10, 0),
            BorderColor = Color.FromRgba(245, 158, 11, 80),
            BorderWidth = 1,
            Command = new Command(onTapped)
        };
    }

    private void AddProductByName(string search)
    {
        var prod = _vm.AvailableProducts.FirstOrDefault(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
        if (prod != null)
        {
            _ = _vm.AddProductAsync(prod);
        }
    }

    // =========================================================================
    // MODALES SECONDAIRES (Remise & Transfert)
    // =========================================================================
    private Grid BuildDiscountModal()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#BB000000"),
            IsVisible = false
        };

        var card = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            CornerRadius = 14,
            Padding = new Thickness(24),
            WidthRequest = 360,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label { Text = "🏷️ Remise sur la Note", TextColor = Colors.White, FontSize = 18, FontAttributes = FontAttributes.Bold },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            MakeDiscountPill("10%", async () => await ApplyQuickDiscount(10m)),
                            MakeDiscountPill("20%", async () => await ApplyQuickDiscount(20m)),
                            MakeDiscountPill("50%", async () => await ApplyQuickDiscount(50m))
                        }
                    },
                    new Button
                    {
                        Text = "Annuler",
                        BackgroundColor = Color.FromArgb("#334155"),
                        TextColor = Colors.White,
                        HeightRequest = 40,
                        CornerRadius = 8,
                        Command = new Command(() => overlay.IsVisible = false)
                    }
                }
            }
        };

        overlay.Children.Add(card);
        return overlay;
    }

    private static Button MakeDiscountPill(string text, Func<Task> action)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromArgb("#8B5CF6"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 40,
            CornerRadius = 8,
            Command = new Command(async () => await action())
        };
    }

    private async Task ApplyQuickDiscount(decimal percent)
    {
        await _vm.ApplyGlobalDiscountAsync(DiscountType.Percentage, percent, "Geste commercial");
        if (_discountModal != null) _discountModal.IsVisible = false;
    }

    private Grid BuildTransferModal()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#BB000000"),
            IsVisible = false
        };

        var card = new Frame
        {
            BackgroundColor = Color.FromArgb("#1E293B"),
            CornerRadius = 14,
            Padding = new Thickness(24),
            WidthRequest = 360,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    new Label { Text = "🔄 Transférer la Table", TextColor = Colors.White, FontSize = 18, FontAttributes = FontAttributes.Bold },
                    new Label { Text = "Sélectionner la table de destination :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 13 },
                    new HorizontalStackLayout
                    {
                        Spacing = 8,
                        Children =
                        {
                            MakeTransferPill("T02"),
                            MakeTransferPill("T04"),
                            MakeTransferPill("T05")
                        }
                    },
                    new Button
                    {
                        Text = "Annuler",
                        BackgroundColor = Color.FromArgb("#334155"),
                        TextColor = Colors.White,
                        HeightRequest = 40,
                        CornerRadius = 8,
                        Command = new Command(() => overlay.IsVisible = false)
                    }
                }
            }
        };

        overlay.Children.Add(card);
        return overlay;
    }

    private Button MakeTransferPill(string targetTable)
    {
        return new Button
        {
            Text = targetTable,
            BackgroundColor = Color.FromArgb("#06B6D4"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 40,
            CornerRadius = 8,
            Command = new Command(async () =>
            {
                var currentTable = _vm.ActiveTable;
                await _vm.LoadActiveTableOrderAsync(targetTable);
                _vm.ClearTableOrder(currentTable);
                if (_transferModal != null) _transferModal.IsVisible = false;
            })
        };
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    // =========================================================================
    // CONVERTISSEURS INTERNES POUR L'UI TACTILE
    // =========================================================================
    private class StringToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is string s && !string.IsNullOrEmpty(s);
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class DestinationToColorConverter(string target) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => string.Equals(value as string, target, StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb("#3B82F6")
                : Color.FromArgb("#334155");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class DestinationToTextColorConverter(string target) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => string.Equals(value as string, target, StringComparison.OrdinalIgnoreCase)
                ? Colors.White
                : Color.FromArgb("#94A3B8");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class DispatchedToTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is true ? "✓ EN CUISINE" : "⏳ EN ATTENTE";
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class DispatchedToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is true ? Color.FromArgb("#34D399") : Color.FromArgb("#FBBF24");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class DispatchedToBorderConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is true ? Color.FromArgb("#10B981") : Color.FromArgb("#F59E0B");
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }
}

internal static class ViewExtensions
{
    public static T Also<T>(this T view, Action<T> configure) where T : BindableObject
    {
        configure(view);
        return view;
    }
}
#endif
