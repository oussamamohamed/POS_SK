using FluentAssertions;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.ViewModels;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Client.Maui.Tests;

public class ModifiersViewModelTests
{
    private readonly Mock<IPlatformEnvironmentService> _envMock = new();
    private readonly Mock<IModifierValidationService> _validationMock = new();

    [Fact]
    public void ToggleOptionOnSingleChoiceGroupSelectsOnlyOneOption()
    {
        // Arrange
        var vm = new ModifiersViewModel(_envMock.Object, _validationMock.Object);
        var product = new Product { Name = "Entrecôte", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(22m) };
        var group = new ProductModifierGroup
        {
            GroupName = "Cuisson",
            MinSelections = 1,
            MaxSelections = 1,
            Options =
            [
                new ProductModifierOption { Name = "Bleu", ExtraPrice = Money.Zero() },
                new ProductModifierOption { Name = "Saignant", ExtraPrice = Money.Zero() },
                new ProductModifierOption { Name = "À point", ExtraPrice = Money.Zero() }
            ]
        };

        vm.LoadModifierGroup(product, group);

        // Act
        vm.ToggleOption(vm.SelectableOptions[0]); // Bleu
        vm.ToggleOption(vm.SelectableOptions[1]); // Saignant

        // Assert
        vm.SelectableOptions.Count(o => o.IsSelected).Should().Be(1);
        vm.SelectableOptions[1].IsSelected.Should().BeTrue();
    }

    [Fact]
    public void CalculateTotalExtraPriceCentsSumsAllSelectedOptions()
    {
        // Arrange
        var vm = new ModifiersViewModel(_envMock.Object, _validationMock.Object);
        var product = new Product { Name = "Burger", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(15m) };
        var group = new ProductModifierGroup
        {
            GroupName = "Suppléments",
            MinSelections = 0,
            MaxSelections = 5,
            Options =
            [
                new ProductModifierOption { Name = "Bacon", ExtraPrice = Money.FromDecimal(2.00m) },
                new ProductModifierOption { Name = "Fromage", ExtraPrice = Money.FromDecimal(1.50m) }
            ]
        };

        vm.LoadModifierGroup(product, group);

        // Act
        vm.ToggleOption(vm.SelectableOptions[0]);
        vm.ToggleOption(vm.SelectableOptions[1]);

        // Assert
        vm.CalculateTotalExtraPriceCents().Should().Be(350); // 2.00 + 1.50 = 3.50 EUR (350 cents)
    }
}
