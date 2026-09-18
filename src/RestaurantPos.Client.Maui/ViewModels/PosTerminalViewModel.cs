using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class PosTerminalViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ILocalJournalService _journalService;
    private readonly IOrderDiscountService? _discountService;
    private readonly KdsViewModel? _kdsViewModel;
    private readonly ITableManagementService? _tableService;
    private readonly FloorPlanViewModel? _floorPlanViewModel;
    private readonly Dictionary<string, (Order Order, List<OrderItem> Items)> _tableOrdersCache = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private Order _activeOrder = new();

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private string _customNumericEntry = string.Empty;

    [ObservableProperty]
    private Money _totalTtc = Money.Zero();

    [ObservableProperty]
    private string _activeTable = "Comptoir";

    [ObservableProperty]
    private string _destination = "EatIn";

    [ObservableProperty]
    private int _coversCount = 2;

    [ObservableProperty]
    private int _heldOrdersCount = 0;

    private readonly List<(Order Order, List<OrderItem> Items)> _heldOrders = [];

    public Money TotalHt => new((long)Math.Round(TotalTtc.AmountInCents / 1.10m, MidpointRounding.AwayFromZero));
    public Money TotalVat => new(TotalTtc.AmountInCents - TotalHt.AmountInCents);

    [ObservableProperty]
    private string _conflictAlertBanner = string.Empty;

    [ObservableProperty]
    private int _catalogGridColumns = 4;

    private readonly IGridLayoutApiService? _gridLayoutService;

    public Dictionary<Guid, List<ProductModifierGroup>> ProductModifierGroups { get; } = new();

    public bool HasModifiers(Product product) => ProductModifierGroups.TryGetValue(product.Id, out var groups) && groups.Count > 0;

    public List<ProductModifierGroup> GetModifierGroups(Product product) => ProductModifierGroups.TryGetValue(product.Id, out var groups) ? groups : [];

    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Product> AvailableProducts { get; } = [];
    public ObservableCollection<OrderItem> CartItems { get; } = [];

    public PosTerminalViewModel(
        IPlatformEnvironmentService environmentService,
        ILocalJournalService journalService,
        IOrderDiscountService? discountService = null,
        KdsViewModel? kdsViewModel = null,
        ITableManagementService? tableService = null,
        FloorPlanViewModel? floorPlanViewModel = null,
        IGridLayoutApiService? gridLayoutService = null)
    {
        _environmentService = environmentService;
        _journalService = journalService;
        _discountService = discountService;
        _kdsViewModel = kdsViewModel;
        _tableService = tableService;
        _floorPlanViewModel = floorPlanViewModel;
        _gridLayoutService = gridLayoutService;
    }

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private string _pageDisplay = "Page 1 / 1";

    public const int ItemsPerPage = 12;

    public List<Product> MasterCatalogProducts { get; } = [];

    [RelayCommand]
    public async Task SelectCategoryAsync(Category? category)
    {
        SelectedCategory = category ?? Categories.FirstOrDefault();
        CurrentPage = 1;
        await FilterProductsForCurrentCategoryAndPageAsync();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public async Task PreviousPageAsync()
    {
        if (CurrentPage > 1)
        {
            CurrentPage--;
            await FilterProductsForCurrentCategoryAndPageAsync();
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
    }

    [RelayCommand]
    public async Task NextPageAsync()
    {
        if (CurrentPage < TotalPages)
        {
            CurrentPage++;
            await FilterProductsForCurrentCategoryAndPageAsync();
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
    }

    [ObservableProperty]
    private GridLayoutDto? _currentMatrix;

    public async Task FilterProductsForCurrentCategoryAndPageAsync()
    {
        var isAll = SelectedCategory is null || SelectedCategory.Id == "ALL";
        var filtered = isAll
            ? MasterCatalogProducts.ToList()
            : MasterCatalogProducts.Where(p => p.CategoryId == SelectedCategory.Id).ToList();

        if (!isAll && _gridLayoutService != null)
        {
            CurrentMatrix = await _gridLayoutService.GetLayoutByCategoryAsync(SelectedCategory!.Id, CurrentPage - 1);
            if (CurrentMatrix != null && CurrentMatrix.ColumnsCount > 0)
            {
                CatalogGridColumns = CurrentMatrix.ColumnsCount;
            }
        }
        else
        {
            CurrentMatrix = null; // Tells UI to use FlexLayout
            CatalogGridColumns = 4;
        }

        TotalPages = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)ItemsPerPage));
        if (CurrentPage > TotalPages) CurrentPage = TotalPages;
        if (CurrentPage < 1) CurrentPage = 1;

        PageDisplay = $"Page {CurrentPage} / {TotalPages}";

        var pageItems = filtered.Skip((CurrentPage - 1) * ItemsPerPage).Take(ItemsPerPage).ToList();

        AvailableProducts.Clear();
        foreach (var p in pageItems)
        {
            AvailableProducts.Add(p);
        }
    }

    [RelayCommand]
    public async Task AddProductAsync(Product product)
    {
        var existing = CartItems.FirstOrDefault(i => i.ProductId == product.Id);
        if (existing is not null)
        {
            existing.Quantity++;
        }
        else
        {
            var item = new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                Quantity = 1,
                TaxRatePercent = product.TaxRatePercent
            };
            CartItems.Add(item);
        }

        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);

        // Record to local append-only journal
        try
        {
            await _journalService.RecordTransactionAsync(
                "OrderItemAdded",
                $"ORD-{ActiveOrder.Id}-ITEM-{Guid.NewGuid():N}",
                new { OrderId = ActiveOrder.Id, ProductId = product.Id, product.Name }
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error logging journal: {ex}");
        }
    }

    public async Task AddProductWithModifiersAsync(
        Product product,
        List<string> selectedModifiers,
        decimal extraPrice,
        string? kitchenComment)
    {
        var item = new OrderItem
        {
            ProductId = product.Id,
            ProductName = product.Name,
            UnitPrice = product.Price,
            Quantity = 1,
            TaxRatePercent = product.TaxRatePercent,
            ModifiersPriceExtra = Money.FromDecimal(extraPrice),
            KitchenComment = kitchenComment
        };

        foreach (var mod in selectedModifiers)
        {
            item.SelectedModifiers.Add(mod);
        }

        CartItems.Add(item);
        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);

        try
        {
            await _journalService.RecordTransactionAsync(
                "OrderItemAdded",
                $"ORD-{ActiveOrder.Id}-ITEM-{Guid.NewGuid():N}",
                new
                {
                    OrderId = ActiveOrder.Id,
                    ProductId = product.Id,
                    product.Name,
                    SelectedModifiers = selectedModifiers,
                    ExtraPrice = extraPrice,
                    KitchenComment = kitchenComment
                }
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error logging journal: {ex}");
        }
    }

    public void SetItemKitchenComment(OrderItem item, string comment)
    {
        item.KitchenComment = comment;
        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void RemoveItem(OrderItem item)
    {
        CartItems.Remove(item);
        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void IncrementQuantity(OrderItem item)
    {
        item.Quantity++;
        RecalculateTotals();
    }

    [RelayCommand]
    public void DecrementQuantity(OrderItem item)
    {
        if (item.Quantity > 1)
        {
            item.Quantity--;
            RecalculateTotals();
        }
        else
        {
            RemoveItem(item);
        }
    }

    [RelayCommand]
    public void ClearCart()
    {
        var undispatchedItems = CartItems.Where(i => !i.IsDispatched).ToList();
        var dispatchedItems = CartItems.Where(i => i.IsDispatched).ToList();

        if (CartItems.Count == 0)
        {
            return;
        }

        if (undispatchedItems.Count > 0 && dispatchedItems.Count > 0)
        {
            foreach (var item in undispatchedItems)
            {
                CartItems.Remove(item);
            }
            ConflictAlertBanner = $"{undispatchedItems.Count} nouvel/nouveaux article(s) retiré(s). Les articles en cuisine sont conservés.";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
        else if (undispatchedItems.Count > 0 && dispatchedItems.Count == 0)
        {
            CartItems.Clear();
            ConflictAlertBanner = string.Empty;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
        else if (undispatchedItems.Count == 0 && dispatchedItems.Count > 0)
        {
            ConflictAlertBanner = "Les articles déjà transmis en cuisine ne peuvent pas être vidés.";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Warning);
        }

        RecalculateTotals();
    }

    [RelayCommand]
    public async Task SendKitchenAndResetAsync(ITableManagementService? tableService = null)
    {
        if (CartItems.Count == 0)
        {
            return;
        }

        var effTableService = tableService ?? _tableService;
        if (effTableService is not null && !string.IsNullOrWhiteSpace(ActiveTable))
        {
            var undispatched = CartItems.Where(i => !i.IsDispatched).ToList();
            if (undispatched.Count > 0)
            {
                var inputDtos = undispatched.Select(i => new OrderItemInputDto(
                    i.ProductId,
                    i.ProductName,
                    i.Quantity,
                    i.UnitPrice.ToDecimal(),
                    i.TaxRatePercent,
                    i.PreparationStationId,
                    i.SelectedModifiers
                )).ToList();

                await effTableService.AddOrUpdateTableOrderItemsAsync(ActiveTable, inputDtos).ConfigureAwait(false);
            }

            await effTableService.DispatchOrderLinesAsync(ActiveTable).ConfigureAwait(false);
        }

        if (_kdsViewModel is not null)
        {
            var ticketItems = CartItems.Select(i => new KitchenTicketItemDto(
                i.Id,
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.SelectedModifiers.Count > 0 ? string.Join(", ", i.SelectedModifiers) : null,
                i.KitchenComment,
                TicketItemStatus.Pending
            )).ToList();

            var ticket = new KitchenTicketDto(
                TicketId: Guid.NewGuid(),
                OrderId: ActiveOrder.Id,
                TableNumber: string.IsNullOrWhiteSpace(ActiveTable) ? "Comptoir" : ActiveTable,
                ServerName: "Alexandre D.",
                CoversCount: 2,
                StationId: "STATION-ALL",
                Status: TicketStatus.Pending,
                DispatchedAtUtc: DateTimeOffset.UtcNow,
                Items: ticketItems
            );

            _kdsViewModel.AddIncomingTicket(ticket);
        }

        foreach (var item in CartItems)
        {
            item.IsDispatched = true;
        }

        if (!string.IsNullOrWhiteSpace(ActiveTable))
        {
            _tableOrdersCache[ActiveTable] = (ActiveOrder, CartItems.Select(CloneOrderItem).ToList());
        }

        CartItems.Clear();
        RecalculateTotals();
        ConflictAlertBanner = string.Empty;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
    }

    [RelayCommand]
    public void AppendNumpadDigit(string digit)
    {
        CustomNumericEntry += digit;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void ClearNumpad()
    {
        CustomNumericEntry = string.Empty;
    }

    [RelayCommand]
    public void DismissConflictAlert()
    {
        ConflictAlertBanner = string.Empty;
    }

    [RelayCommand]
    public void SetDestination(string dest)
    {
        Destination = dest;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void HoldCurrentCart()
    {
        if (CartItems.Count == 0) return;
        _heldOrders.Add((ActiveOrder, CartItems.Select(CloneOrderItem).ToList()));
        HeldOrdersCount = _heldOrders.Count;
        CartItems.Clear();
        ActiveOrder = new Order { TableNumber = ActiveTable };
        RecalculateTotals();
        ConflictAlertBanner = "Commande mise en attente (Parkée).";
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
    }

    [RelayCommand]
    public void RecallHeldOrder()
    {
        if (_heldOrders.Count == 0) return;
        var last = _heldOrders[^1];
        _heldOrders.RemoveAt(_heldOrders.Count - 1);
        HeldOrdersCount = _heldOrders.Count;

        CartItems.Clear();
        ActiveOrder = last.Order;
        foreach (var itm in last.Items)
        {
            CartItems.Add(CloneOrderItem(itm));
        }
        RecalculateTotals();
        ConflictAlertBanner = "Commande en attente rappelée.";
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    public async Task LoadActiveTableOrderAsync(string tableNumber, ITableManagementService? tableService = null)
    {
        var isSameTable = string.Equals(ActiveTable, tableNumber, StringComparison.OrdinalIgnoreCase);

        // If we are already on this table and already have items in the cart, do not wipe!
        if (isSameTable && CartItems.Count > 0)
        {
            _tableOrdersCache[tableNumber] = (ActiveOrder, CartItems.Select(CloneOrderItem).ToList());
            return;
        }

        // If switching from another table and we have cart items, cache previous table's state first
        if (!string.IsNullOrWhiteSpace(ActiveTable) &&
            !isSameTable &&
            CartItems.Count > 0)
        {
            _tableOrdersCache[ActiveTable] = (ActiveOrder, CartItems.Select(CloneOrderItem).ToList());
        }

        ActiveTable = tableNumber;
        var effTableService = tableService ?? _tableService;

        if (effTableService is not null)
        {
            var orderDto = await effTableService.GetActiveOrderForTableAsync(tableNumber);
            if (orderDto is not null && orderDto.Lines.Count > 0)
            {
                CartItems.Clear();
                ActiveOrder = new Order
                {
                    Id = orderDto.OrderId,
                    TableNumber = tableNumber,
                    GlobalDiscountType = orderDto.GlobalDiscountType,
                    GlobalDiscountValue = orderDto.GlobalDiscountValue,
                    GlobalDiscountReason = orderDto.GlobalDiscountReason
                };

                foreach (var line in orderDto.Lines)
                {
                    CartItems.Add(new OrderItem
                    {
                        Id = line.LineId,
                        ProductId = line.ProductId,
                        ProductName = line.ProductName,
                        Quantity = line.Quantity,
                        UnitPrice = Money.FromDecimal(line.UnitPrice, "EUR"),
                        TaxRatePercent = line.TaxRatePercent,
                        PreparationStationId = line.PreparationStationId,
                        IsDispatched = line.IsDispatched,
                        SelectedModifiers = line.ModifiersSummary.ToList(),
                        IsComp = line.IsComp,
                        DiscountPercent = line.DiscountPercent,
                        Course = line.Course,
                        ModifiersPriceExtra = Money.FromDecimal(line.ModifiersPriceExtra, "EUR")
                    });
                }

                _tableOrdersCache[tableNumber] = (ActiveOrder, CartItems.Select(CloneOrderItem).ToList());
                RecalculateTotals();
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
                return;
            }
        }

        // Check local table cache
        if (_tableOrdersCache.TryGetValue(tableNumber, out var cached) && cached.Items.Count > 0)
        {
            CartItems.Clear();
            ActiveOrder = cached.Order;
            foreach (var item in cached.Items)
            {
                CartItems.Add(CloneOrderItem(item));
            }
        }
        else
        {
            CartItems.Clear();
            ActiveOrder = new Order { TableNumber = tableNumber };
        }

        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    public void ClearTableOrder(string tableNumber)
    {
        _tableOrdersCache.Remove(tableNumber);
        if (string.Equals(ActiveTable, tableNumber, StringComparison.OrdinalIgnoreCase))
        {
            CartItems.Clear();
            ActiveOrder = new Order { TableNumber = tableNumber };
            RecalculateTotals();
        }
    }

    private static OrderItem CloneOrderItem(OrderItem item) => new()
    {
        Id = item.Id,
        OrderId = item.OrderId,
        ProductId = item.ProductId,
        ProductName = item.ProductName,
        UnitPrice = item.UnitPrice,
        Quantity = item.Quantity,
        TaxRatePercent = item.TaxRatePercent,
        PreparationStationId = item.PreparationStationId,
        IsDispatched = item.IsDispatched,
        SelectedModifiers = [.. item.SelectedModifiers],
        ModifiersPriceExtra = item.ModifiersPriceExtra,
        Course = item.Course,
        IsComp = item.IsComp,
        CompReason = item.CompReason,
        DiscountPercent = item.DiscountPercent,
        KitchenComment = item.KitchenComment
    };

    public async Task ApplyGlobalDiscountAsync(DiscountType type, decimal value, string reason, Guid? operatorId = null)
    {
        ActiveOrder.GlobalDiscountType = type;
        ActiveOrder.GlobalDiscountValue = value;
        ActiveOrder.GlobalDiscountReason = reason;

        if (_discountService is not null)
        {
            await _discountService.ApplyGlobalDiscountAsync(
                ActiveOrder.Id,
                type,
                value,
                reason,
                operatorId ?? Guid.Empty
            ).ConfigureAwait(false);
        }

        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    public async Task CompItemAsync(OrderItem item, string reason, Guid? operatorId = null)
    {
        item.IsComp = true;
        item.CompReason = reason;

        if (_discountService is not null)
        {
            await _discountService.CompOrderItemAsync(
                ActiveOrder.Id,
                item.Id,
                reason,
                operatorId ?? Guid.Empty
            ).ConfigureAwait(false);
        }

        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    public void RecalculateTotals()
    {
        ActiveOrder.Items.Clear();
        ActiveOrder.Items.AddRange(CartItems);

        long totalCents = CartItems.Sum(i => i.CalculateTotalTtc().AmountInCents);

        if (ActiveOrder.GlobalDiscountType == DiscountType.Percentage && ActiveOrder.GlobalDiscountValue > 0)
        {
            long discountedCents = (long)Math.Round(totalCents * (1.0m - (ActiveOrder.GlobalDiscountValue / 100.0m)), MidpointRounding.AwayFromZero);
            totalCents = Math.Max(0, discountedCents);
        }
        else if (ActiveOrder.GlobalDiscountType == DiscountType.FixedAmount && ActiveOrder.GlobalDiscountValue > 0)
        {
            long discountCents = (long)Math.Round(ActiveOrder.GlobalDiscountValue * 100m, MidpointRounding.AwayFromZero);
            totalCents = Math.Max(0, totalCents - discountCents);
        }

        TotalTtc = new Money(totalCents);
        OnPropertyChanged(nameof(TotalHt));
        OnPropertyChanged(nameof(TotalVat));

        if (!string.IsNullOrWhiteSpace(ActiveTable))
        {
            if (CartItems.Count > 0)
            {
                _tableOrdersCache[ActiveTable] = (ActiveOrder, CartItems.Select(CloneOrderItem).ToList());
            }
            _floorPlanViewModel?.UpdateTableTotal(ActiveTable, TotalTtc.ToDecimal());
        }
    }



    public void SeedDemoTableOrders()
    {
        // No seed orders to ensure reports start at clean zero state
    }

    public async Task RefreshCatalogAsync()
    {
        try
        {
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var catDtos = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<List<ApiCategoryItem>>(client, "http://127.0.0.1:5000/api/catalog/categories");
            var prodDtos = await System.Net.Http.Json.HttpClientJsonExtensions.GetFromJsonAsync<List<ApiProductItem>>(client, "http://127.0.0.1:5000/api/catalog/products");

            if (catDtos is not null && catDtos.Count > 0 && prodDtos is not null && prodDtos.Count > 0)
            {
                Categories.Clear();
                foreach (var c in catDtos.OrderBy(c => c.DisplayOrder))
                {
                    Categories.Add(new Category
                    {
                        Id = c.Id,
                        Name = c.Name,
                        ColorHex = c.ColorHex,
                        DisplayOrder = c.DisplayOrder,
                        IconName = c.IconName
                    });
                }

                MasterCatalogProducts.Clear();
                foreach (var p in prodDtos.OrderBy(p => p.DisplayOrder))
                {
                    var prod = new Product
                    {
                        Id = p.Id,
                        Name = p.Name,
                        CategoryId = p.CategoryId,
                        Price = Money.FromDecimal(p.Price),
                        TaxRatePercent = p.TaxRatePercent,
                        ColorHex = p.ColorHex,
                        DisplayOrder = p.DisplayOrder,
                        IsQuickKey = p.IsQuickKey
                    };
                    MasterCatalogProducts.Add(prod);

                    if (p.ModifierGroups is not null && p.ModifierGroups.Count > 0)
                    {
                        ProductModifierGroups[prod.Id] = p.ModifierGroups.Select(g => new ProductModifierGroup
                        {
                            ProductId = prod.Id,
                            GroupName = g.GroupName,
                            MinSelections = g.MinSelections,
                            MaxSelections = g.MaxSelections,
                            Options = g.Options.Select(o => new ProductModifierOption
                            {
                                Name = o.Name,
                                ExtraPrice = Money.FromDecimal(o.ExtraPrice),
                                IsDefault = o.IsDefault
                            }).ToList()
                        }).ToList();
                    }
                }
                await FilterProductsForCurrentCategoryAndPageAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching catalog: {ex.Message}");
        }
    }

    private class ApiCategoryItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ColorHex { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public string? IconName { get; set; }
    }

    private class ApiProductItem
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal TaxRatePercent { get; set; }
        public string ColorHex { get; set; } = string.Empty;
        public int DisplayOrder { get; set; }
        public bool IsQuickKey { get; set; }
        public List<ApiModifierGroupItem>? ModifierGroups { get; set; }
    }

    private class ApiModifierGroupItem
    {
        public string GroupName { get; set; } = string.Empty;
        public int MinSelections { get; set; }
        public int MaxSelections { get; set; }
        public List<ApiModifierOptionItem> Options { get; set; } = [];
    }

    private class ApiModifierOptionItem
    {
        public string Name { get; set; } = string.Empty;
        public decimal ExtraPrice { get; set; }
        public bool IsDefault { get; set; }
    }
}
