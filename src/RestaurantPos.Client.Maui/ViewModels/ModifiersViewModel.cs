using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ViewModels;

public class SelectableOptionItem : ObservableObject
{
    public required ProductModifierOption Option { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}

public partial class ModifiersViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly IModifierValidationService _validationService;

    [ObservableProperty]
    private Product? _product;

    [ObservableProperty]
    private ProductModifierGroup? _group;

    [ObservableProperty]
    private string _specialInstructions = string.Empty;

    [ObservableProperty]
    private string _validationErrorMessage = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    public ObservableCollection<SelectableOptionItem> SelectableOptions { get; } = [];

    public ModifiersViewModel(
        IPlatformEnvironmentService environmentService,
        IModifierValidationService validationService)
    {
        _environmentService = environmentService;
        _validationService = validationService;
    }

    public void LoadModifierGroup(Product product, ProductModifierGroup group)
    {
        Product = product;
        Group = group;
        SpecialInstructions = string.Empty;
        ValidationErrorMessage = string.Empty;
        IsCompleted = false;

        SelectableOptions.Clear();
        foreach (var opt in group.Options)
        {
            SelectableOptions.Add(new SelectableOptionItem
            {
                Option = opt,
                IsSelected = opt.IsDefault
            });
        }
    }

    [RelayCommand]
    public void ToggleOption(SelectableOptionItem item)
    {
        if (Group is null) return;

        if (Group.IsSingleChoice)
        {
            foreach (var opt in SelectableOptions)
            {
                opt.IsSelected = false;
            }
            item.IsSelected = true;
        }
        else
        {
            item.IsSelected = !item.IsSelected;
        }

        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        ValidationErrorMessage = string.Empty;
    }

    [RelayCommand]
    public void ConfirmModifiers()
    {
        if (Group is null) return;

        var selected = SelectableOptions
            .Where(o => o.IsSelected)
            .Select(o => new SelectedModifier(o.Option.Id, o.Option.Name, o.Option.ExtraPrice.AmountInCents))
            .ToList();

        if (!_validationService.ValidateSelection(Group.MinSelections, Group.MaxSelections, selected, out string? error))
        {
            ValidationErrorMessage = error ?? "Sélection invalide";
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Error);
            return;
        }

        IsCompleted = true;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
    }

    public long CalculateTotalExtraPriceCents()
    {
        return SelectableOptions
            .Where(o => o.IsSelected)
            .Sum(o => o.Option.ExtraPrice.AmountInCents);
    }
}
