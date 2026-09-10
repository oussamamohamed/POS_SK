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
    private readonly IBackOfficeCatalogService _catalogService;
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

    public CatalogAdminViewModel(
        IBackOfficeCatalogService catalogService,
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
            var list = await _catalogService.GetAllCategoriesAsync(includeArchived: false);
            Categories.Clear();
            foreach (var item in list)
            {
                Categories.Add(item);
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
            var list = await _catalogService.GetProductsByCategoryAsync(SelectedCategory.Id, includeArchived: false);
            Products.Clear();
            foreach (var item in list)
            {
                if (string.IsNullOrWhiteSpace(SearchFilter) || item.Name.Contains(SearchFilter, StringComparison.OrdinalIgnoreCase))
                {
                    Products.Add(item);
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

        var cat = await _catalogService.CreateCategoryAsync(name, "#4A90E2", Categories.Count + 1, null);
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

        var prod = await _catalogService.CreateProductAsync(
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

        Products.Add(prod);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
        StatusMessage = $"Article '{name}' ajouté au catalogue.";
    }

    [RelayCommand]
    public async Task ArchiveProductAsync(Product product)
    {
        await _catalogService.ArchiveProductAsync(product.Id);
        Products.Remove(product);
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        StatusMessage = $"Article '{product.Name}' archivé.";
    }
}
