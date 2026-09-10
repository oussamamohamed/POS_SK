using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.ViewModels;

public class SplitPartitionItem : ObservableObject
{
    public int PartitionIndex { get; init; }
    public long AmountCents { get; set; }
    public bool IsPaid { get; set; }

    public string DisplayText => $"Part {PartitionIndex}: {AmountCents / 100.0:F2} € {(IsPaid ? "(Payé)" : "")}";
}

public partial class SplitBillViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ICheckoutPaymentService _checkoutService;

    [ObservableProperty]
    private long _totalOrderAmountCents;

    [ObservableProperty]
    private int _guestsCount = 2;

    public ObservableCollection<SplitPartitionItem> Partitions { get; } = [];

    public SplitBillViewModel(
        IPlatformEnvironmentService environmentService,
        ICheckoutPaymentService checkoutService)
    {
        _environmentService = environmentService;
        _checkoutService = checkoutService;
    }

    public void Initialize(long totalCents, int initialGuests = 2)
    {
        TotalOrderAmountCents = totalCents;
        GuestsCount = Math.Max(2, initialGuests);
        RecalculatePartitions();
    }

    [RelayCommand]
    public void IncreaseGuests()
    {
        GuestsCount++;
        RecalculatePartitions();
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void DecreaseGuests()
    {
        if (GuestsCount > 2)
        {
            GuestsCount--;
            RecalculatePartitions();
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
        }
    }

    public void RecalculatePartitions()
    {
        var parts = _checkoutService.CalculateEqualSplitPartitions(TotalOrderAmountCents, GuestsCount);
        Partitions.Clear();

        for (int i = 0; i < parts.Count; i++)
        {
            Partitions.Add(new SplitPartitionItem
            {
                PartitionIndex = i + 1,
                AmountCents = parts[i],
                IsPaid = false
            });
        }
    }
}
