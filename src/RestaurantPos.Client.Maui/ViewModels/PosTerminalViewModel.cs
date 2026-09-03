using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class PosTerminalViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ILocalJournalService _journalService;

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
    private string _conflictAlertBanner = string.Empty;

    public ObservableCollection<Category> Categories { get; } = [];
    public ObservableCollection<Product> AvailableProducts { get; } = [];
    public ObservableCollection<OrderItem> CartItems { get; } = [];

    public PosTerminalViewModel(
        IPlatformEnvironmentService environmentService,
        ILocalJournalService journalService)
    {
        _environmentService = environmentService;
        _journalService = journalService;

        LoadSampleCatalog();
    }

    [RelayCommand]
    public void SelectCategory(Category category)
    {
        SelectedCategory = category;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
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
        await _journalService.RecordTransactionAsync(
            "OrderItemAdded",
            $"ORD-{ActiveOrder.Id}-ITEM-{Guid.NewGuid():N}",
            new { OrderId = ActiveOrder.Id, ProductId = product.Id, product.Name }
        ).ConfigureAwait(false);
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

        if (tableService is not null && !string.IsNullOrWhiteSpace(ActiveTable))
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

                await tableService.AddOrUpdateTableOrderItemsAsync(ActiveTable, inputDtos).ConfigureAwait(false);
            }

            await tableService.DispatchOrderLinesAsync(ActiveTable).ConfigureAwait(false);
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

    public async Task LoadActiveTableOrderAsync(string tableNumber, ITableManagementService? tableService = null)
    {
        ActiveTable = tableNumber;
        CartItems.Clear();

        if (tableService is not null)
        {
            var orderDto = await tableService.GetActiveOrderForTableAsync(tableNumber);
            if (orderDto is not null)
            {
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
                        SelectedModifiers = line.ModifiersSummary.ToList()
                    });
                }
            }
        }

        RecalculateTotals();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    public void RecalculateTotals()
    {
        long totalCents = CartItems.Sum(i => i.CalculateTotalTtc().AmountInCents);
        TotalTtc = new Money(totalCents);
    }

    private void LoadSampleCatalog()
    {
        var catBoissons = new Category { Id = "CAT-DRINKS", Name = "Boissons", DisplayOrder = 1, ColorHex = "#3B82F6" };
        var catPlats = new Category { Id = "CAT-MAINS", Name = "Plats", DisplayOrder = 2, ColorHex = "#10B981" };
        var catDesserts = new Category { Id = "CAT-DESSERTS", Name = "Desserts", DisplayOrder = 3, ColorHex = "#F59E0B" };

        Categories.Add(catBoissons);
        Categories.Add(catPlats);
        Categories.Add(catDesserts);
        SelectedCategory = catBoissons;

        AvailableProducts.Add(new Product { Name = "Café Espresso", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(2.50m) });
        AvailableProducts.Add(new Product { Name = "Eau Minérale 50cl", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(3.00m) });
        AvailableProducts.Add(new Product { Name = "Bière Pression 33cl", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(5.50m) });
        AvailableProducts.Add(new Product { Name = "Burger Maison & Frites", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(16.50m) });
        AvailableProducts.Add(new Product { Name = "Entrecôte Grillée 250g", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(22.00m) });
        AvailableProducts.Add(new Product { Name = "Tiramisu Maison", CategoryId = "CAT-DESSERTS", Price = Money.FromDecimal(7.50m) });
    }
}
