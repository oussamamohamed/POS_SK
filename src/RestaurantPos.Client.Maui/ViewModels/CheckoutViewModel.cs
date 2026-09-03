using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ViewModels;

public class ActiveTenderItem : ObservableObject
{
    public PaymentMethod Method { get; init; }
    public long AmountCents { get; set; }
    public long TenderedCents { get; set; }

    public string DisplayText => $"{Method}: {AmountCents / 100.0:F2} €";
}

public partial class CheckoutViewModel : ObservableObject
{
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ICheckoutPaymentService? _checkoutService;

    [ObservableProperty]
    private Guid _orderId;

    [ObservableProperty]
    private string _terminalId = "POS01";

    [ObservableProperty]
    private long _totalDueCents;

    [ObservableProperty]
    private long _remainingBalanceCents;

    [ObservableProperty]
    private long _changeDueCents;

    [ObservableProperty]
    private PaymentMethod _selectedMethod = PaymentMethod.CreditCard;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _receiptNumber = string.Empty;

    public ObservableCollection<ActiveTenderItem> AppliedTenders { get; } = [];

    public CheckoutViewModel(
        IPlatformEnvironmentService environmentService,
        ICheckoutPaymentService? checkoutService = null)
    {
        _environmentService = environmentService;
        _checkoutService = checkoutService;
    }

    public void Initialize(Guid orderId, long totalAmountCents)
    {
        OrderId = orderId;
        TotalDueCents = totalAmountCents;
        RemainingBalanceCents = totalAmountCents;
        ChangeDueCents = 0;
        IsCompleted = false;
        ReceiptNumber = string.Empty;
        AppliedTenders.Clear();
    }

    [RelayCommand]
    public void SelectPaymentMethod(PaymentMethod method)
    {
        SelectedMethod = method;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void AddCashFastBill(long billAmountCents)
    {
        if (RemainingBalanceCents <= 0) return;

        long tenderAmount = Math.Min(RemainingBalanceCents, billAmountCents);
        long change = Math.Max(0, billAmountCents - tenderAmount);

        AppliedTenders.Add(new ActiveTenderItem
        {
            Method = PaymentMethod.Cash,
            AmountCents = tenderAmount,
            TenderedCents = billAmountCents
        });

        RemainingBalanceCents -= tenderAmount;
        ChangeDueCents = change;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public async Task FinalizeCheckoutAsync()
    {
        if (RemainingBalanceCents > 0 && SelectedMethod != PaymentMethod.Cash)
        {
            // Settle remainder with currently selected non-cash method
            AppliedTenders.Add(new ActiveTenderItem
            {
                Method = SelectedMethod,
                AmountCents = RemainingBalanceCents,
                TenderedCents = RemainingBalanceCents
            });
            RemainingBalanceCents = 0;
        }

        if (_checkoutService is not null)
        {
            var tenderRequests = AppliedTenders.Select(t =>
                new PaymentTenderRequest(t.Method, t.AmountCents, t.TenderedCents)).ToList();

            var result = await _checkoutService.ProcessPaymentTendersAsync(OrderId, TerminalId, tenderRequests).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                ReceiptNumber = result.ReceiptNumber;
                ChangeDueCents = result.ChangeGivenCents;
                IsCompleted = true;
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
            }
        }
        else
        {
            ReceiptNumber = $"{TerminalId}-000001";
            IsCompleted = true;
            _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
        }
    }
}
