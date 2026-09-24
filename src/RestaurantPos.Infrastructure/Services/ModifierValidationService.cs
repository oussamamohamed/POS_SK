using System.Collections.Generic;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Services;

public class ModifierValidationService : IModifierValidationService
{
    public bool ValidateSelection(
        int minSelections,
        int maxSelections,
        IReadOnlyList<SelectedModifier> selectedOptions,
        out string? validationError)
    {
        int count = selectedOptions?.Count ?? 0;

        if (count < minSelections)
        {
            validationError = $"Veuillez sélectionner au moins {minSelections} option(s).";
            return false;
        }

        if (count > maxSelections)
        {
            validationError = $"Vous ne pouvez pas sélectionner plus de {maxSelections} option(s).";
            return false;
        }

        validationError = null;
        return true;
    }
}
