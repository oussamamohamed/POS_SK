using System;
using System.Collections.Generic;

namespace RestaurantPos.Application.Common.Interfaces;

public readonly record struct SelectedModifier(
    Guid OptionId,
    string OptionName,
    long ExtraPriceCents);

public interface IModifierValidationService
{
    bool ValidateSelection(
        int minSelections,
        int maxSelections,
        IReadOnlyList<SelectedModifier> selectedOptions,
        out string? validationError);
}
