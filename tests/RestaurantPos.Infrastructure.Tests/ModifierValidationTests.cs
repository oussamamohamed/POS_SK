using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ModifierValidationTests
{
    [Fact]
    public void ValidateSelectionWhenMinNotMetReturnsFalseWithErrorMessage()
    {
        // Arrange
        var service = new ModifierValidationService();
        var selected = new List<SelectedModifier>();

        // Act
        bool isValid = service.ValidateSelection(1, 1, selected, out string? error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("au moins 1 option");
    }

    [Fact]
    public void ValidateSelectionWhenExceedingMaxReturnsFalseWithErrorMessage()
    {
        // Arrange
        var service = new ModifierValidationService();
        var selected = new List<SelectedModifier>
        {
            new(Guid.NewGuid(), "Bleu", 0),
            new(Guid.NewGuid(), "Saignant", 0)
        };

        // Act
        bool isValid = service.ValidateSelection(1, 1, selected, out string? error);

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("pas sélectionner plus de 1");
    }

    [Fact]
    public void ValidateSelectionWhenWithinBoundsReturnsTrue()
    {
        // Arrange
        var service = new ModifierValidationService();
        var selected = new List<SelectedModifier>
        {
            new(Guid.NewGuid(), "Saignant", 0)
        };

        // Act
        bool isValid = service.ValidateSelection(1, 1, selected, out string? error);

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }
}
