using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class CatalogAdminViewModel : ObservableObject
{
    private readonly IBackOfficeCatalogService? _catalogService;
    private readonly IPlatformEnvironmentService _environmentService;

    [ObservableProperty]
    private ObservableCollection<Category> _categories = [];

    [ObservableProperty]
    private Category? _selectedCategory;

    [ObservableProperty]
    private ObservableCollection<Product> _products = [];

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    public CatalogAdminViewModel(IPlatformEnvironmentService environmentService)
        : this(null, environmentService)
    {
    }

    public CatalogAdminViewModel(
        IBackOfficeCatalogService? catalogService,
        IPlatformEnvironmentService environmentService)
    {
        _catalogService = catalogService;
        _environmentService = environmentService;
    }

    [RelayCommand]
    public async Task LoadCategoriesAsync()
    {
        IsLoading = true;
        try
        {
            Categories.Clear();
            if (_catalogService != null)
            {
                var list = await _catalogService.GetAllCategoriesAsync(includeArchived: false);
                foreach (var item in list)
                {
                    Categories.Add(item);
                }
            }

            if (Categories.Count == 0)
            {
                Categories.Add(new Category { Id = "CAT-DRINKS", Name = "Boissons", DisplayOrder = 1, ColorHex = "#3B82F6" });
                Categories.Add(new Category { Id = "CAT-MAINS", Name = "Plats", DisplayOrder = 2, ColorHex = "#10B981" });
                Categories.Add(new Category { Id = "CAT-DESSERTS", Name = "Desserts", DisplayOrder = 3, ColorHex = "#F59E0B" });
            }

            if (SelectedCategory is null && Categories.Count > 0)
            {
                SelectedCategory = Categories[0];
                await LoadProductsAsync();
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task SelectCategoryAsync(Category category)
    {
        SelectedCategory = category;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        await LoadProductsAsync();
    }

    [RelayCommand]
    public async Task LoadProductsAsync()
    {
        if (SelectedCategory is null) return;

        IsLoading = true;
        try
        {
            Products.Clear();
            if (_catalogService != null)
            {
                var list = await _catalogService.GetProductsByCategoryAsync(SelectedCategory.Id, includeArchived: false);
                foreach (var item in list)
                {
                    if (string.IsNullOrWhiteSpace(SearchFilter) || item.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        Products.Add(item);
                    }
                }
            }

            if (Products.Count == 0)
            {
                if (SelectedCategory.Id == "CAT-DRINKS")
                {
                    Products.Add(new Product { Name = "Café Espresso", CategoryId = "CAT-DRINKS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(2.50m) });
                    Products.Add(new Product { Name = "Eau Minérale 50cl", CategoryId = "CAT-DRINKS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(3.00m) });
                    Products.Add(new Product { Name = "Bière Pression 33cl", CategoryId = "CAT-DRINKS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(5.50m) });
                }
                else if (SelectedCategory.Id == "CAT-MAINS")
                {
                    Products.Add(new Product { Name = "Burger Maison & Frites", CategoryId = "CAT-MAINS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(16.50m) });
                    Products.Add(new Product { Name = "Entrecôte Grillée 250g", CategoryId = "CAT-MAINS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(22.00m) });
                }
                else
                {
                    Products.Add(new Product { Name = "Tiramisu Maison", CategoryId = "CAT-DESSERTS", Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(7.50m) });
                }
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task CreateCategoryAsync(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;

        Category cat;
        if (_catalogService != null)
        {
            cat = await _catalogService.CreateCategoryAsync(name, "#4A90E2", Categories.Count + 1, null);
        }
        else
        {
            cat = new Category { Id = $"CAT-{Guid.NewGuid():N}", Name = name, ColorHex = "#4A90E2", DisplayOrder = Categories.Count + 1 };
        }

        Categories.Add(cat);
        SelectedCategory = cat;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
        StatusMessage = $"Famille '{name}' créée avec succès.";
        await LoadProductsAsync();
    }

    [RelayCommand]
    public async Task CreateProductAsync(string name)
    {
        if (SelectedCategory is null || string.IsNullOrWhiteSpace(name)) return;

        Product prod;
        if (_catalogService != null)
        {
            prod = await _catalogService.CreateProductAsync(
                name: name,
                categoryId: SelectedCategory.Id,
                price: 10.00m,
                taxRatePercent: 10.0m,
                description: null,
                colorHex: null,
                displayOrder: Products.Count + 1,
                isQuickKey: false,
                stationId: "HOT"
            );
        }
        else
        {
            prod = new Product
            {
                Name = name,
                CategoryId = SelectedCategory.Id,
                Price = RestaurantPos.Domain.ValueObjects.Money.FromDecimal(10.00m),
                TaxRatePercent = 10.0m
            };
        }

        Products.Add(prod);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
        StatusMessage = $"Article '{name}' ajouté au catalogue.";
    }

    [RelayCommand]
    public async Task ArchiveProductAsync(Product product)
    {
        if (_catalogService != null)
        {
            await _catalogService.ArchiveProductAsync(product.Id);
        }
        Products.Remove(product);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        StatusMessage = $"Article '{product.Name}' archivé.";
    }
}
