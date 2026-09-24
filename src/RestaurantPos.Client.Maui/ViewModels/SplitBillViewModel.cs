using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.ViewModels;

public partial class SplitPartitionItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private int _partitionIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private long _amountCents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayText))]
    private bool _isPaid;

    public string DisplayText => $"Part {PartitionIndex}: {AmountCents / 100.0:F2} € {(IsPaid ? "(Payé)" : "")}";
}

public partial class SplitBillViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ICheckoutPaymentService? _checkoutService;

    [ObservableProperty]
    private long _totalOrderAmountCents;

    [ObservableProperty]
    private int _guestsCount = 2;

    [ObservableProperty]
    private Guid _activeOrderId;

    [ObservableProperty]
    private string _activeTable = string.Empty;

    public ObservableCollection<SplitPartitionItem> Partitions { get; } = [];

    public SplitBillViewModel(
        IPlatformEnvironmentService environmentService,
        ICheckoutPaymentService? checkoutService = null)
    {
        _environmentService = environmentService;
        _checkoutService = checkoutService;
    }

    public void Initialize(long totalCents, int initialGuests = 2, Guid orderId = default, string tableNumber = "")
    {
        TotalOrderAmountCents = totalCents;
        GuestsCount = Math.Max(2, initialGuests);
        ActiveOrderId = orderId;
        ActiveTable = tableNumber;
        RecalculatePartitions();
    }

    public void MarkPartitionPaid(int partitionIndex)
    {
        var part = Partitions.FirstOrDefault(p => p.PartitionIndex == partitionIndex);
        if (part != null)
        {
            part.IsPaid = true;
            OnPropertyChanged(nameof(Partitions));
        }
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
        var parts = _checkoutService != null
            ? _checkoutService.CalculateEqualSplitPartitions(TotalOrderAmountCents, GuestsCount)
            : CalculateEqualSplit(TotalOrderAmountCents, GuestsCount);

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

    private static IReadOnlyList<long> CalculateEqualSplit(long totalAmountCents, int numberOfGuests)
    {
        if (numberOfGuests <= 0) return [totalAmountCents];
        long baseShare = totalAmountCents / numberOfGuests;
        long remainder = totalAmountCents % numberOfGuests;
        var result = new List<long>(numberOfGuests);
        for (int i = 0; i < numberOfGuests; i++)
        {
            result.Add(baseShare + (i < remainder ? 1 : 0));
        }
        return result;
    }
}
