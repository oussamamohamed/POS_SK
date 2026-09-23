#if MAUI_UI
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using RestaurantPos.Client.Maui.Controls;
using RestaurantPos.Client.Maui.Theme;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.Views;

/// <summary>
/// Terminal de caisse tactile principal conforme aux Apple Human Interface Guidelines (HIG) pour iPadOS.
/// Disposition 2 colonnes Split-View : Panier Inset Grouped (Gauche 380px) | Catalogue & Touches Rapides (Droite Star).
/// </summary>
public class PosTerminalPage : ContentPage, IQueryAttributable
{
    private readonly PosTerminalViewModel _vm;
    private Grid? _discountModal;
    private Grid? _transferModal;
    private Grid? _modifiersModal;
    private Frame? _modifiersCard;
    private Grid? _billNoteModal;
    private Frame? _billNoteCard;
    private HorizontalStackLayout? _categoryTabsLayout;
    private FlexLayout? _productsLayout;
    private Button? _prevBtn;
    private Button? _nextBtn;

    public bool SimulateCategoryButtonClick(string categoryNameOrAll)
    {
        if (_categoryTabsLayout == null) return false;
        foreach (var child in _categoryTabsLayout.Children)
        {
            if (child is Button btn)
            {
                if ((categoryNameOrAll.Equals("ALL", StringComparison.OrdinalIgnoreCase) && btn.Text.Contains("Tous")) ||
                    btn.Text.Contains(categoryNameOrAll, StringComparison.OrdinalIgnoreCase))
                {
                    btn.SendClicked();
                    return true;
                }
            }
        }
        return false;
    }

    public void SimulateNextPageClick() => _nextBtn?.SendClicked();
    public void SimulatePrevPageClick() => _prevBtn?.SendClicked();

    public PosTerminalPage(PosTerminalViewModel vm)
    {
        _vm = vm;
        BindingContext = vm;
        BackgroundColor = AppleHigTheme.SystemBackground;
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

        // Modales overlay (Remise & Transfert & Modificateurs & Note)
        _discountModal = BuildDiscountModal();
        _transferModal = BuildTransferModal();
        _modifiersModal = BuildModifiersModal();
        _billNoteModal = BuildBillNoteModal();

        Content = new Grid
        {
            Children =
            {
                mainLayout,
                _discountModal,
                _transferModal,
                _modifiersModal,
                _billNoteModal
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.RefreshCatalogAsync();
    }

    // =========================================================================
    // PANNEAU GAUCHE : PANIER / TICKET (380px) — MATCH 1:1 AVEC pos-cart-panel WEB
    // =========================================================================
    private View BuildCartPanel()
    {
        var panel = new Grid
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
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
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
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

                var modifiersLabel = new Label
                {
                    FontSize = 11,
                    TextColor = Color.FromArgb("#38BDF8"),
                    LineBreakMode = LineBreakMode.WordWrap
                };
                modifiersLabel.SetBinding(Label.TextProperty, new Binding("SelectedModifiers", converter: new SelectedModifiersToStringConverter()));
                modifiersLabel.SetBinding(IsVisibleProperty, new Binding("SelectedModifiers", converter: new CollectionNotEmptyToBoolConverter()));

                var commentLabel = new Label
                {
                    FontSize = 11,
                    TextColor = Color.FromArgb("#F59E0B"),
                    LineBreakMode = LineBreakMode.WordWrap
                };
                commentLabel.SetBinding(Label.TextProperty, new Binding("KitchenComment", stringFormat: "📝 {0}"));
                commentLabel.SetBinding(IsVisibleProperty, new Binding("KitchenComment", converter: new StringToBoolConverter()));

                var priceLabel = new Label
                {
                    FontSize = 15,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#10B981"),
                    HorizontalOptions = LayoutOptions.End,
                    VerticalOptions = LayoutOptions.Center
                };
                priceLabel.SetBinding(Label.TextProperty, "TotalTtc", stringFormat: "{0:F2} €");

                var itemNoteBtn = new Button
                {
                    Text = "📝",
                    BackgroundColor = Color.FromArgb("#334155"),
                    TextColor = Color.FromArgb("#F59E0B"),
                    WidthRequest = 28,
                    HeightRequest = 28,
                    CornerRadius = 6,
                    FontSize = 11,
                    Padding = 0
                };
                itemNoteBtn.Clicked += async (s, e) =>
                {
                    if (itemNoteBtn.BindingContext is OrderItem it)
                    {
                        await EditItemNoteAsync(it);
                    }
                };

                var qtyRow = new HorizontalStackLayout
                {
                    Spacing = 6,
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
                        }),
                        itemNoteBtn
                    }
                };

                var leftCol = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children = { statusBadge, nameLabel, modifiersLabel, commentLabel, qtyRow }
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
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            Padding = new Thickness(14, 10),
            Spacing = 6
        };

        // Ligne HT
        var htRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
            Children =
            {
                new Label { Text = "Total HT :", TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 },
                new Label { TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 }.Also(l =>
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
                new Label { Text = "TVA (10% / 20%) :", TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 },
                new Label { TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 }.Also(l =>
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
                new Label { Text = "TOTAL TTC", TextColor = AppleHigTheme.LabelPrimary, FontSize = 16, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                new Label
                {
                    FontSize = 26,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = AppleHigTheme.SystemGreen,
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

        // Action Buttons (Grille responsive ergonomique Apple HIG min 44pt)
        var actionButtonsGrid = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Star },
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
        var btnKitchen = MakeCartActionBtn("📤 Envoyer Cuisine", AppleHigTheme.SystemBlue, new Command(async () =>
        {
            var tableNum = _vm.ActiveTable;
            await _vm.SendKitchenAndResetAsync();
            var floorVm = Handler?.MauiContext?.Services.GetService<FloorPlanViewModel>();
            floorVm?.SetTableStatus(tableNum, TableStatus.Occupied, 2);
            await Shell.Current.GoToAsync("//floor");
        }));
        Grid.SetRow(btnKitchen, 0); Grid.SetColumn(btnKitchen, 0);

        var btnHold = MakeCartActionBtn("⏸️ Attente", AppleHigTheme.QuaternarySystemFill, _vm.HoldCurrentCartCommand);
        Grid.SetRow(btnHold, 0); Grid.SetColumn(btnHold, 1);

        var btnDiscount = MakeCartActionBtn("🏷️ Remise", AppleHigTheme.SystemIndigo, new Command(() =>
        {
            if (_discountModal != null) _discountModal.IsVisible = true;
        }));
        Grid.SetRow(btnDiscount, 0); Grid.SetColumn(btnDiscount, 2);

        var btnNote = MakeCartActionBtn("🧾 Note", AppleHigTheme.SystemTeal, new Command(() =>
        {
            OpenBillNoteModal();
        }));
        Grid.SetRow(btnNote, 0); Grid.SetColumn(btnNote, 3);

        // Row 1
        var btnTransfer = MakeCartActionBtn("🔄 Transférer", AppleHigTheme.SystemTeal, new Command(() =>
        {
            if (_transferModal != null) _transferModal.IsVisible = true;
        }));
        Grid.SetRow(btnTransfer, 1); Grid.SetColumn(btnTransfer, 0);

        var btnSplit = MakeCartActionBtn("➗ Split", AppleHigTheme.SystemOrange, new Command(async () =>
        {
            var splitVm = Handler?.MauiContext?.Services.GetService<SplitBillViewModel>();
            splitVm?.Initialize(_vm.TotalTtc.AmountInCents, 2, _vm.ActiveOrder.Id, _vm.ActiveTable);
            await Shell.Current.GoToAsync($"split?orderId={_vm.ActiveOrder.Id}&totalCents={_vm.TotalTtc.AmountInCents}&tableNumber={_vm.ActiveTable}");
        }));
        Grid.SetRow(btnSplit, 1); Grid.SetColumn(btnSplit, 1);

        var btnPay = MakeCartActionBtn("💳 Encaisser", AppleHigTheme.SystemGreen, new Command(async () =>
        {
            var checkoutVm = Handler?.MauiContext?.Services.GetService<CheckoutViewModel>();
            if (checkoutVm != null)
            {
                checkoutVm.Initialize(_vm.ActiveOrder.Id, _vm.TotalTtc.AmountInCents, _vm.ActiveTable);
            }
            await Shell.Current.GoToAsync($"checkout?orderId={_vm.ActiveOrder.Id}&totalCents={_vm.TotalTtc.AmountInCents}&tableNumber={_vm.ActiveTable}");
        }));
        Grid.SetRow(btnPay, 1); Grid.SetColumn(btnPay, 2);
        Grid.SetColumnSpan(btnPay, 2);

        actionButtonsGrid.Add(btnKitchen);
        actionButtonsGrid.Add(btnHold);
        actionButtonsGrid.Add(btnDiscount);
        actionButtonsGrid.Add(btnNote);
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
            BackgroundColor = AppleHigTheme.QuaternarySystemFill,
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 36,
            CornerRadius = 8,
            Padding = 0,
            Command = command
        };
        Grid.SetColumn(btn, col);
        return btn;
    }

    private static Button MakeCartActionBtn(string text, Color color, System.Windows.Input.ICommand command)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = color,
            TextColor = Colors.White,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            MinimumHeightRequest = AppleHigTheme.MinTouchTarget,
            CornerRadius = 10,
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
            checkoutVm.Initialize(_vm.ActiveOrder.Id, _vm.TotalTtc.AmountInCents, _vm.ActiveTable);
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
            BackgroundColor = AppleHigTheme.SystemBackground,
            Padding = new Thickness(16),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto }, // Touches Rapides (Rush Bar)
                new RowDefinition { Height = GridLength.Auto }, // Onglets Familles Horizontaux
                new RowDefinition { Height = GridLength.Star }, // Grille Articles
                new RowDefinition { Height = GridLength.Auto }  // Barre Pagination
            },
            RowSpacing = 12
        };

        // 1. Touches Rapides ("Coup de feu")
        var quickKeysBar = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                new Label { Text = "⚡ Rapide :", TextColor = AppleHigTheme.SystemOrange, FontSize = 13, FontAttributes = FontAttributes.Bold, VerticalOptions = LayoutOptions.Center },
                MakeQuickKeyBtn("☕ Café", () => AddProductByName("Café")),
                MakeQuickKeyBtn("🍔 Burger", () => AddProductByName("Burger")),
                MakeQuickKeyBtn("💧 Eau", () => AddProductByName("Eau")),
                MakeQuickKeyBtn("🍺 Bière", () => AddProductByName("Bière"))
            }
        };
        Grid.SetRow(quickKeysBar, 0);

        // 2. Onglets Familles Horizontaux (Écrans de familles réactifs)
        var categoryTabsLayout = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center
        };
        _categoryTabsLayout = categoryTabsLayout;

        var categoryTabs = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            HeightRequest = 46,
            Content = categoryTabsLayout
        };
        Grid.SetRow(categoryTabs, 1);

        void RenderCategoryTabs()
        {
            categoryTabsLayout.Children.Clear();

            // Bouton "⚡ Tous"
            bool isAll = _vm.SelectedCategory is null || _vm.SelectedCategory.Id == "ALL";
            var allBtn = new Button
            {
                Text = "⚡ Tous",
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                Padding = new Thickness(16, 6),
                CornerRadius = 10,
                BackgroundColor = isAll ? Color.FromArgb("#2563EB") : AppleHigTheme.TertiarySystemBackground,
                TextColor = isAll ? Colors.White : AppleHigTheme.LabelPrimary,
                HeightRequest = 40
            };
            allBtn.Clicked += (s, e) =>
            {
                _vm.SelectCategory(null);
                RenderCategoryTabs();
            };
            categoryTabsLayout.Children.Add(allBtn);

            foreach (var cat in _vm.Categories)
            {
                bool isSel = _vm.SelectedCategory?.Id == cat.Id;
                var btn = new Button
                {
                    Text = cat.Name,
                    FontSize = 13,
                    FontAttributes = FontAttributes.Bold,
                    Padding = new Thickness(16, 6),
                    CornerRadius = 10,
                    BackgroundColor = isSel ? Color.FromArgb("#2563EB") : AppleHigTheme.TertiarySystemBackground,
                    TextColor = isSel ? Colors.White : AppleHigTheme.LabelPrimary,
                    HeightRequest = 40
                };
                var capturedCat = cat;
                btn.Clicked += (s, e) =>
                {
                    _vm.SelectCategory(capturedCat);
                    RenderCategoryTabs();
                };
                categoryTabsLayout.Children.Add(btn);
            }
        }

        RenderCategoryTabs();
        _vm.Categories.CollectionChanged += (s, e) => MainThread.BeginInvokeOnMainThread(RenderCategoryTabs);

        // 3. Grille Articles (products-grid FlexLayout iOS-safe)
        var productsLayout = new FlexLayout
        {
            Direction = Microsoft.Maui.Layouts.FlexDirection.Row,
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
        };
        _productsLayout = productsLayout;

        Frame CreateProductFrame(Product prod)
        {
            var frame = new Frame
            {
                Padding = new Thickness(14),
                CornerRadius = (float)AppleHigTheme.CornerRadiusLarge,
                HasShadow = false,
                BackgroundColor = AppleHigTheme.SecondarySystemBackground,
                BorderColor = AppleHigTheme.Separator,
                WidthRequest = 180,
                HeightRequest = 110,
                Margin = new Thickness(6)
            };

            var name = new Label
            {
                Text = prod.Name,
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppleHigTheme.LabelPrimary,
                LineBreakMode = LineBreakMode.WordWrap,
                MaxLines = 2
            };

            var price = new Label
            {
                Text = $"{prod.Price:F2} €",
                FontSize = 16,
                TextColor = AppleHigTheme.SystemGreen,
                FontAttributes = FontAttributes.Bold
            };

            var optBadge = new Label
            {
                Text = "⚙️ Modificateurs",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = AppleHigTheme.SystemTeal,
                BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                Padding = new Thickness(6, 2),
                HorizontalOptions = LayoutOptions.Start,
                IsVisible = _vm.HasModifiers(prod)
            };

            frame.Content = new VerticalStackLayout
            {
                Spacing = 5,
                Children = { name, price, optBadge },
                InputTransparent = true
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (s, e) =>
            {
                if (_vm.HasModifiers(prod))
                {
                    OpenModifiersModal(prod);
                }
                else
                {
                    _vm.AddProductCommand.Execute(prod);
                }
            };
            frame.GestureRecognizers.Add(tap);

            return frame;
        }

        BindableLayout.SetItemTemplate(productsLayout, new DataTemplate(() =>
        {
            var frame = new Frame();
            frame.SetBinding(BindableObject.BindingContextProperty, ".");
            frame.BindingContextChanged += (s, e) =>
            {
                if (frame.BindingContext is Product prod)
                {
                    var actualFrame = CreateProductFrame(prod);
                    frame.Content = actualFrame.Content;
                    frame.BackgroundColor = actualFrame.BackgroundColor;
                    frame.BorderColor = actualFrame.BorderColor;
                    frame.Padding = actualFrame.Padding;
                    frame.CornerRadius = actualFrame.CornerRadius;
                    frame.WidthRequest = actualFrame.WidthRequest;
                    frame.HeightRequest = actualFrame.HeightRequest;
                    frame.Margin = actualFrame.Margin;
                    foreach(var g in actualFrame.GestureRecognizers) frame.GestureRecognizers.Add(g);
                }
            };
            return frame;
        }));

        productsLayout.SetBinding(BindableLayout.ItemsSourceProperty, nameof(PosTerminalViewModel.AvailableProducts));

        var productsScroll = new ScrollView
        {
            Content = productsLayout
        };
        Grid.SetRow(productsScroll, 2);

        // 4. Barre Pagination (grid-pagination-bar)
        var prevBtn = new Button
        {
            Text = "◀ Précédent",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 38,
            CornerRadius = 8,
            Command = _vm.PreviousPageCommand
        };
        _prevBtn = prevBtn;
        Grid.SetColumn(prevBtn, 0);

        var pageLabel = new Label
        {
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        pageLabel.SetBinding(Label.TextProperty, nameof(PosTerminalViewModel.PageDisplay));
        Grid.SetColumn(pageLabel, 1);

        var nextBtn = new Button
        {
            Text = "Suivant ▶",
            BackgroundColor = AppleHigTheme.TertiarySystemBackground,
            TextColor = AppleHigTheme.LabelPrimary,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 38,
            CornerRadius = 8,
            Command = _vm.NextPageCommand
        };
        _nextBtn = nextBtn;
        Grid.SetColumn(nextBtn, 2);

        var paginationBar = new Grid
        {
            Padding = new Thickness(12, 6),
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            Children = { prevBtn, pageLabel, nextBtn }
        };
        Grid.SetRow(paginationBar, 3);

        panel.Add(quickKeysBar);
        panel.Add(categoryTabs);
        panel.Add(productsScroll);
        panel.Add(paginationBar);

        // Recalcule la largeur des tuiles dès que le panel est redimensionné ou que le nombre de colonnes change
        void ApplyTileWidth()
        {
            double available = productsScroll.Width > 0 ? productsScroll.Width : 720;
            int cols = _vm.CatalogGridColumns is >= 2 and <= 6 ? _vm.CatalogGridColumns : 4;
            double tileWidth = Math.Max(100, (available - cols * 12.0) / cols);
            if (_productsLayout is null) return;
            foreach (var child in _productsLayout.Children)
            {
                if (child is Frame f) f.WidthRequest = tileWidth;
            }
        }

        productsScroll.SizeChanged += (s, e) => ApplyTileWidth();

        void UpdateProductsLayout()
        {
            if (_vm.CurrentMatrix != null)
            {
                var grid = new Grid
                {
                    RowSpacing = 8,
                    ColumnSpacing = 8,
                    Padding = new Thickness(6)
                };

                int cols = _vm.CurrentMatrix.ColumnsCount > 0 ? _vm.CurrentMatrix.ColumnsCount : 4;
                int rows = _vm.CurrentMatrix.RowsCount > 0 ? _vm.CurrentMatrix.RowsCount : 4;

                for (int c = 0; c < cols; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
                for (int r = 0; r < rows; r++) grid.RowDefinitions.Add(new RowDefinition { Height = 120 });

                foreach (var slot in _vm.CurrentMatrix.Slots)
                {
                    if (slot.ProductId.HasValue)
                    {
                        var prod = _vm.MasterCatalogProducts.FirstOrDefault(p => p.Id == slot.ProductId.Value);
                        if (prod != null)
                        {
                            var frame = CreateProductFrame(prod);
                            frame.Margin = new Thickness(0);
                            frame.WidthRequest = -1; // Auto width in grid
                            Grid.SetColumn(frame, slot.ColumnIndex);
                            Grid.SetRow(frame, slot.RowIndex);
                            grid.Children.Add(frame);
                        }
                    }
                }

                productsScroll.Content = grid;
                _productsLayout = null; // Disable flex layout logic
            }
            else
            {
                productsScroll.Content = productsLayout;
                _productsLayout = productsLayout;
                ApplyTileWidth();
            }
        }

        _vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PosTerminalViewModel.SelectedCategory))
            {
                MainThread.BeginInvokeOnMainThread(RenderCategoryTabs);
            }
            else if (e.PropertyName == nameof(PosTerminalViewModel.CatalogGridColumns) || e.PropertyName == nameof(PosTerminalViewModel.CurrentMatrix))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (e.PropertyName == nameof(PosTerminalViewModel.CurrentMatrix))
                    {
                        UpdateProductsLayout();
                    }
                    else
                    {
                        ApplyTileWidth();
                    }
                });
            }
        };

        return panel;
    }

    private static Button MakeQuickKeyBtn(string text, Action onTapped)
    {
        return new Button
        {
            Text = text,
            BackgroundColor = Color.FromRgba(255, 159, 10, 30),
            TextColor = AppleHigTheme.SystemOrange,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 36,
            CornerRadius = 8,
            Padding = new Thickness(10, 0),
            BorderColor = Color.FromRgba(255, 159, 10, 75),
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
            BackgroundColor = Color.FromArgb("#AA000000"),
            IsVisible = false
        };

        var card = new Frame
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            BorderColor = AppleHigTheme.Separator,
            CornerRadius = (float)AppleHigTheme.CornerRadiusSheet,
            Padding = new Thickness(24, 16),
            WidthRequest = 380,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    AppleHigTheme.CreateSheetGrabber(),
                    new Label { Text = "🏷️ Remise sur la Note", TextColor = AppleHigTheme.LabelPrimary, FontSize = AppleHigTheme.Title3, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        HorizontalOptions = LayoutOptions.Center,
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
                        BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                        TextColor = AppleHigTheme.LabelSecondary,
                        HeightRequest = 44,
                        CornerRadius = 10,
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
            BackgroundColor = AppleHigTheme.SystemIndigo,
            TextColor = Colors.White,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            MinimumWidthRequest = 70,
            CornerRadius = 10,
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
            BackgroundColor = Color.FromArgb("#AA000000"),
            IsVisible = false
        };

        var card = new Frame
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            BorderColor = AppleHigTheme.Separator,
            CornerRadius = (float)AppleHigTheme.CornerRadiusSheet,
            Padding = new Thickness(24, 16),
            WidthRequest = 380,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false,
            Content = new VerticalStackLayout
            {
                Spacing = 16,
                Children =
                {
                    AppleHigTheme.CreateSheetGrabber(),
                    new Label { Text = "🔄 Transférer la Table", TextColor = AppleHigTheme.LabelPrimary, FontSize = AppleHigTheme.Title3, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center },
                    new Label { Text = "Sélectionner la table de destination :", TextColor = AppleHigTheme.LabelSecondary, FontSize = 13, HorizontalOptions = LayoutOptions.Center },
                    new HorizontalStackLayout
                    {
                        Spacing = 10,
                        HorizontalOptions = LayoutOptions.Center,
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
                        BackgroundColor = AppleHigTheme.TertiarySystemBackground,
                        TextColor = AppleHigTheme.LabelSecondary,
                        HeightRequest = 44,
                        CornerRadius = 10,
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
            BackgroundColor = AppleHigTheme.SystemTeal,
            TextColor = Colors.White,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            MinimumWidthRequest = 70,
            CornerRadius = 10,
            Command = new Command(async () =>
            {
                var currentTable = _vm.ActiveTable;
                await _vm.LoadActiveTableOrderAsync(targetTable);
                _vm.ClearTableOrder(currentTable);
                if (_transferModal != null) _transferModal.IsVisible = false;
            })
        };
    }

    private Grid BuildModifiersModal()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#AA000000"),
            IsVisible = false
        };

        _modifiersCard = new Frame
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            BorderColor = AppleHigTheme.Separator,
            CornerRadius = (float)AppleHigTheme.CornerRadiusSheet,
            Padding = new Thickness(24, 16),
            WidthRequest = 480,
            MaximumHeightRequest = 620,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false
        };

        overlay.Children.Add(_modifiersCard);
        return overlay;
    }

    public void CloseModifiersModal()
    {
        if (_modifiersModal != null) _modifiersModal.IsVisible = false;
    }

    public void OpenModifiersModal(Product product)
    {
        if (_modifiersModal == null || _modifiersCard == null) return;

        var groups = _vm.GetModifierGroups(product);
        var selectedOpts = new List<ProductModifierOption>();
        foreach (var g in groups)
        {
            foreach (var opt in g.Options)
            {
                if (opt.IsDefault) selectedOpts.Add(opt);
            }
        }

        var mainLayout = new VerticalStackLayout { Spacing = 14 };

        var titleLabel = new Label
        {
            Text = $"⚙️ {product.Name}",
            TextColor = Colors.White,
            FontSize = 20,
            FontAttributes = FontAttributes.Bold
        };
        var subLabel = new Label
        {
            Text = $"Prix de base : {product.Price.ToDecimal():F2} €  •  TVA {product.TaxRatePercent}%",
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 13
        };
        mainLayout.Children.Add(titleLabel);
        mainLayout.Children.Add(subLabel);

        var groupsStack = new VerticalStackLayout { Spacing = 12 };
        var optionButtons = new List<(ProductModifierGroup Group, ProductModifierOption Option, Button Btn)>();

        var extraPriceLabel = new Label
        {
            TextColor = Color.FromArgb("#10B981"),
            FontSize = 13,
            FontAttributes = FontAttributes.Bold
        };
        var finalPriceLabel = new Label
        {
            TextColor = Color.FromArgb("#38BDF8"),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        };

        void UpdatePriceDisplays()
        {
            decimal totalExtra = selectedOpts.Sum(o => o.ExtraPrice.ToDecimal());
            decimal finalPrice = product.Price.ToDecimal() + totalExtra;
            extraPriceLabel.Text = totalExtra > 0 ? $"Suppléments : +{totalExtra:F2} €" : "Suppléments : inclus";
            finalPriceLabel.Text = $"Prix total : {finalPrice:F2} €";

            foreach (var (grp, opt, btn) in optionButtons)
            {
                bool isSelected = selectedOpts.Contains(opt);
                btn.BackgroundColor = isSelected ? Color.FromArgb("#0284C7") : Color.FromArgb("#0F172A");
                btn.BorderColor = isSelected ? Color.FromArgb("#38BDF8") : Color.FromArgb("#334155");
                btn.TextColor = isSelected ? Colors.White : Color.FromArgb("#CBD5E1");
            }
        }

        foreach (var grp in groups)
        {
            var grpBox = new VerticalStackLayout { Spacing = 6 };
            grpBox.Children.Add(new Label
            {
                Text = $"{grp.GroupName} {(grp.IsSingleChoice ? "(1 choix)" : "(Choix multiples)")} :",
                TextColor = Color.FromArgb("#F8FAFC"),
                FontSize = 13,
                FontAttributes = FontAttributes.Bold
            });

            var optionsFlex = new FlexLayout
            {
                Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
                AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Start
            };

            foreach (var opt in grp.Options)
            {
                var optBtn = new Button
                {
                    Text = opt.ExtraPrice.AmountInCents > 0
                        ? $"{opt.Name} (+{opt.ExtraPrice.ToDecimal():F2} €)"
                        : opt.Name,
                    FontSize = 12,
                    FontAttributes = FontAttributes.Bold,
                    HeightRequest = 36,
                    CornerRadius = 8,
                    BorderWidth = 1,
                    Margin = new Thickness(0, 0, 8, 8),
                    Padding = new Thickness(10, 0)
                };

                optBtn.Clicked += (s, e) =>
                {
                    if (grp.IsSingleChoice)
                    {
                        selectedOpts.RemoveAll(o => grp.Options.Contains(o));
                        selectedOpts.Add(opt);
                    }
                    else
                    {
                        if (!selectedOpts.Remove(opt))
                            selectedOpts.Add(opt);
                    }
                    UpdatePriceDisplays();
                };

                optionButtons.Add((grp, opt, optBtn));
                optionsFlex.Children.Add(optBtn);
            }

            grpBox.Children.Add(optionsFlex);
            groupsStack.Children.Add(grpBox);
        }

        mainLayout.Children.Add(new ScrollView { Content = groupsStack, MaximumHeightRequest = 260 });

        var instructionEntry = new Entry
        {
            Placeholder = "Instructions cuisine / Note (ex: sans sel, bien chaud...)",
            PlaceholderColor = Color.FromArgb("#64748B"),
            TextColor = Colors.White,
            BackgroundColor = Color.FromArgb("#0F172A"),
            FontSize = 13,
            HeightRequest = 40
        };
        mainLayout.Children.Add(instructionEntry);

        UpdatePriceDisplays();
        var priceSummaryRow = new HorizontalStackLayout
        {
            Spacing = 16,
            Children = { extraPriceLabel, finalPriceLabel }
        };
        mainLayout.Children.Add(priceSummaryRow);

        var btnAdd = new Button
        {
            Text = "✔ Valider & Ajouter au Panier",
            BackgroundColor = Color.FromArgb("#10B981"),
            TextColor = Colors.White,
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            CornerRadius = 8
        };
        btnAdd.Clicked += async (s, e) =>
        {
            decimal totalExtra = selectedOpts.Sum(o => o.ExtraPrice.ToDecimal());
            var selectedNames = selectedOpts.Select(o => o.ExtraPrice.AmountInCents > 0 ? $"{o.Name} (+{o.ExtraPrice.ToDecimal():F2} €)" : o.Name).ToList();
            await _vm.AddProductWithModifiersAsync(product, selectedNames, totalExtra, string.IsNullOrWhiteSpace(instructionEntry.Text) ? null : instructionEntry.Text.Trim());
            _modifiersModal.IsVisible = false;
        };

        var btnQuickAdd = new Button
        {
            Text = "⚡ Ajout Simple Direct",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontSize = 12,
            HeightRequest = 38,
            CornerRadius = 8
        };
        btnQuickAdd.Clicked += async (s, e) =>
        {
            await _vm.AddProductAsync(product);
            _modifiersModal.IsVisible = false;
        };

        var btnCancel = new Button
        {
            Text = "Annuler",
            BackgroundColor = Color.FromArgb("#1E293B"),
            TextColor = Color.FromArgb("#94A3B8"),
            FontSize = 12,
            HeightRequest = 36,
            CornerRadius = 8
        };
        btnCancel.Clicked += (s, e) =>
        {
            _modifiersModal.IsVisible = false;
        };

        var actionsGrid = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = GridLength.Auto } },
            ColumnSpacing = 8,
            Children = { btnAdd }
        };
        Grid.SetColumn(btnQuickAdd, 1);
        Grid.SetColumn(btnCancel, 2);
        actionsGrid.Children.Add(btnQuickAdd);
        actionsGrid.Children.Add(btnCancel);
        mainLayout.Children.Add(actionsGrid);

        _modifiersCard.Content = mainLayout;
        _modifiersModal.IsVisible = true;
    }

    private Grid BuildBillNoteModal()
    {
        var overlay = new Grid
        {
            BackgroundColor = Color.FromArgb("#AA000000"),
            IsVisible = false
        };

        _billNoteCard = new Frame
        {
            BackgroundColor = AppleHigTheme.SecondarySystemBackground,
            BorderColor = AppleHigTheme.Separator,
            CornerRadius = (float)AppleHigTheme.CornerRadiusSheet,
            Padding = new Thickness(24, 16),
            WidthRequest = 420,
            MaximumHeightRequest = 620,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            HasShadow = false
        };

        overlay.Children.Add(_billNoteCard);
        return overlay;
    }

    public void OpenDiscountModal()
    {
        if (_discountModal != null) _discountModal.IsVisible = true;
    }

    public void CloseDiscountModal()
    {
        if (_discountModal != null) _discountModal.IsVisible = false;
    }

    public void OpenTransferModal()
    {
        if (_transferModal != null) _transferModal.IsVisible = true;
    }

    public void CloseTransferModal()
    {
        if (_transferModal != null) _transferModal.IsVisible = false;
    }

    public void CloseBillNoteModal()
    {
        if (_billNoteModal != null) _billNoteModal.IsVisible = false;
    }

    public void OpenBillNoteModal()
    {
        if (_billNoteModal == null || _billNoteCard == null) return;

        var stack = new VerticalStackLayout { Spacing = 12 };
        stack.Children.Add(AppleHigTheme.CreateSheetGrabber());

        var header = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = "🧾 Note de Table (Addition Provisoire)", TextColor = AppleHigTheme.LabelPrimary, FontSize = AppleHigTheme.Title3, FontAttributes = FontAttributes.Bold },
                new Label { Text = $"Table : {_vm.ActiveTable}  •  Couverts : {_vm.CoversCount}  •  Opérateur : Alexandre D.", TextColor = AppleHigTheme.LabelSecondary, FontSize = 12 },
                new Label { Text = $"Éditée le {DateTime.Now:dd/MM/yyyy à HH:mm}", TextColor = AppleHigTheme.LabelTertiary, FontSize = 11 },
                new Label { Text = "⚠️ DOCUMENT PROVISOIRE — NE CONSTITUE PAS UNE FACTURE", TextColor = AppleHigTheme.SystemOrange, FontSize = 10, FontAttributes = FontAttributes.Bold }
            }
        };
        stack.Children.Add(header);
        stack.Children.Add(new BoxView { HeightRequest = 1, Color = AppleHigTheme.Separator });

        var itemsStack = new VerticalStackLayout { Spacing = 6 };
        if (_vm.CartItems.Count == 0)
        {
            itemsStack.Children.Add(new Label { Text = "Aucun article dans la commande.", TextColor = Color.FromArgb("#94A3B8"), FontSize = 13, HorizontalOptions = LayoutOptions.Center });
        }
        else
        {
            foreach (var it in _vm.CartItems)
            {
                var row = new Grid
                {
                    ColumnDefinitions =
                    {
                        new ColumnDefinition { Width = new GridLength(28) },
                        new ColumnDefinition { Width = GridLength.Star },
                        new ColumnDefinition { Width = GridLength.Auto }
                    }
                };

                row.Children.Add(new Label { Text = $"{it.Quantity}x", TextColor = Color.FromArgb("#38BDF8"), FontSize = 12, FontAttributes = FontAttributes.Bold });
                
                var details = new VerticalStackLayout { Spacing = 1 };
                details.Children.Add(new Label { Text = it.ProductName, TextColor = Colors.White, FontSize = 13, FontAttributes = FontAttributes.Bold });
                if (it.SelectedModifiers.Count > 0)
                {
                    details.Children.Add(new Label { Text = "• " + string.Join(" • ", it.SelectedModifiers), TextColor = Color.FromArgb("#94A3B8"), FontSize = 10 });
                }
                if (!string.IsNullOrWhiteSpace(it.KitchenComment))
                {
                    details.Children.Add(new Label { Text = $"📝 {it.KitchenComment}", TextColor = Color.FromArgb("#F59E0B"), FontSize = 10 });
                }
                Grid.SetColumn(details, 1);
                row.Children.Add(details);

                var price = new Label { Text = $"{it.CalculateTotalTtc().ToDecimal():F2} €", TextColor = Color.FromArgb("#10B981"), FontSize = 13, FontAttributes = FontAttributes.Bold };
                Grid.SetColumn(price, 2);
                row.Children.Add(price);

                itemsStack.Children.Add(row);
            }
        }
        stack.Children.Add(new ScrollView { Content = itemsStack, MaximumHeightRequest = 260 });

        stack.Children.Add(new BoxView { HeightRequest = 1, Color = Color.FromArgb("#334155") });
        var totalsBox = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
                    Children =
                    {
                        new Label { Text = "Total HT :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 },
                        new Label { Text = $"{_vm.TotalHt.ToDecimal():F2} €", TextColor = Colors.White, FontSize = 12 }.Also(l => Grid.SetColumn(l, 1))
                    }
                },
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
                    Children =
                    {
                        new Label { Text = "TVA (10% & 20%) :", TextColor = Color.FromArgb("#94A3B8"), FontSize = 12 },
                        new Label { Text = $"{_vm.TotalVat.ToDecimal():F2} €", TextColor = Colors.White, FontSize = 12 }.Also(l => Grid.SetColumn(l, 1))
                    }
                },
                new Grid
                {
                    ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Star }, new ColumnDefinition { Width = GridLength.Auto } },
                    Children =
                    {
                        new Label { Text = "TOTAL TTC :", TextColor = Colors.White, FontSize = 16, FontAttributes = FontAttributes.Bold },
                        new Label { Text = $"{_vm.TotalTtc.ToDecimal():F2} €", TextColor = Color.FromArgb("#10B981"), FontSize = 18, FontAttributes = FontAttributes.Bold }.Also(l => Grid.SetColumn(l, 1))
                    }
                }
            }
        };
        stack.Children.Add(totalsBox);

        var printBtn = new Button
        {
            Text = "🖨️ Imprimer Note & Demande d'Addition",
            BackgroundColor = Color.FromArgb("#0284C7"),
            TextColor = Colors.White,
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HeightRequest = 44,
            CornerRadius = 8
        };
        printBtn.Clicked += async (s, e) =>
        {
            var floorVm = Handler?.MauiContext?.Services.GetService<FloorPlanViewModel>();
            floorVm?.SetTableStatus(_vm.ActiveTable, TableStatus.BillRequested);
            _vm.ActiveOrder.Status = OrderStatus.BillRequested;
            _billNoteModal.IsVisible = false;
            await DisplayAlert("Note Imprimée", $"La note de table pour {_vm.ActiveTable} ({_vm.TotalTtc.ToDecimal():F2} €) a été imprimée. La table est désormais marquée en statut 'Addition'.", "OK");
        };

        var closeBtn = new Button
        {
            Text = "Fermer",
            BackgroundColor = Color.FromArgb("#334155"),
            TextColor = Colors.White,
            FontSize = 13,
            HeightRequest = 38,
            CornerRadius = 8
        };
        closeBtn.Clicked += (s, e) =>
        {
            _billNoteModal.IsVisible = false;
        };

        stack.Children.Add(printBtn);
        stack.Children.Add(closeBtn);

        _billNoteCard.Content = stack;
        _billNoteModal.IsVisible = true;
    }

    private async Task EditItemNoteAsync(OrderItem item)
    {
        string current = item.KitchenComment ?? string.Empty;
        string result = await DisplayPromptAsync(
            "Consigne Cuisine",
            $"Instruction ou note pour {item.ProductName} :",
            initialValue: current,
            placeholder: "Ex: Sans sel, bien cuit, sauce à part...");

        if (result != null)
        {
            _vm.SetItemKitchenComment(item, result.Trim());
        }
    }

    private static T AddToGrid<T>(T view, int row) where T : View
    {
        Grid.SetRow(view, row);
        return view;
    }

    // =========================================================================
    // CONVERTISSEURS INTERNES POUR L'UI TACTILE
    // =========================================================================
    private class SelectedModifiersToStringConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is List<string> list && list.Count > 0)
            {
                return "• " + string.Join(" • ", list);
            }
            return string.Empty;
        }
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotImplementedException();
    }

    private class CollectionNotEmptyToBoolConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value is System.Collections.ICollection col && col.Count > 0;
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
