using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class CatalogAdminViewModelTests
{
    private readonly Mock<IBackOfficeCatalogService> _catalogServiceMock = new();
    private readonly Mock<IPlatformEnvironmentService> _environmentMock = new();

    [Fact]
    public async Task LoadCategories_ShouldPopulateCategories_AndSelectFirst()
    {
        var cat1 = new Category { Id = "CAT1", Name = "Plats", DisplayOrder = 1 };
        var cat2 = new Category { Id = "CAT2", Name = "Desserts", DisplayOrder = 2 };

        _catalogServiceMock
            .Setup(s => s.GetAllCategoriesAsync(false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([cat1, cat2]);

        _catalogServiceMock
            .Setup(s => s.GetProductsByCategoryAsync(cat1.Id, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var vm = new CatalogAdminViewModel(_catalogServiceMock.Object, _environmentMock.Object);

        await vm.LoadCategoriesAsync();

        vm.Categories.Should().HaveCount(2);
        vm.SelectedCategory.Should().Be(cat1);
    }

    [Fact]
    public async Task CreateProduct_ShouldCallService_AndAppendToProducts()
    {
        var cat = new Category { Id = "CAT_PIZZA", Name = "Pizzas" };
        var product = new Product
        {
            Id = UuidV7.NewGuid(),
            Name = "Calzone",
            CategoryId = cat.Id,
            Price = new Money(1200, "EUR")
        };

        _catalogServiceMock
            .Setup(s => s.CreateProductAsync(
                "Calzone", cat.Id, 10.00m, 10.0m, null, null, It.IsAny<int>(), false, "HOT", It.IsAny<CancellationToken>()))
            .ReturnsAsync(product);

        var vm = new CatalogAdminViewModel(_catalogServiceMock.Object, _environmentMock.Object)
        {
            SelectedCategory = cat
        };

        await vm.CreateProductAsync("Calzone");

        vm.Products.Should().ContainSingle(p => p.Name == "Calzone");
        _environmentMock.Verify(e => e.TriggerHapticFeedback(HapticFeedbackType.Success), Times.Once);
    }
}
