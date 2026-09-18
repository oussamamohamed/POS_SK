using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

#if MAUI_UI
using Microsoft.Maui.ApplicationModel;
#endif

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
    private static int _localSequence = 0;
    private readonly IPlatformEnvironmentService _environmentService;
    private readonly ICheckoutPaymentService? _checkoutService;
    private readonly IRoomBillingService? _roomBillingService;
    private readonly FloorPlanViewModel? _floorPlanViewModel;
    private readonly PosTerminalViewModel? _posTerminalViewModel;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopeFactory;

    [ObservableProperty]
    private Guid _orderId;

    [ObservableProperty]
    private string _terminalId = "POS01";

    [ObservableProperty]
    private string _tableNumber = string.Empty;

    [ObservableProperty]
    private long _totalDueCents;

    [ObservableProperty]
    private long _remainingBalanceCents;

    [ObservableProperty]
    private long _changeDueCents;

    [ObservableProperty]
    private PaymentMethod _selectedMethod = PaymentMethod.CreditCard;

    [ObservableProperty]
    private string _roomNumber = string.Empty;

    [ObservableProperty]
    private string _guestName = string.Empty;

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _receiptNumber = string.Empty;

    public ObservableCollection<ActiveTenderItem> AppliedTenders { get; } = [];

    public CheckoutViewModel(
        IPlatformEnvironmentService environmentService,
        ICheckoutPaymentService? checkoutService = null,
        IRoomBillingService? roomBillingService = null,
        FloorPlanViewModel? floorPlanViewModel = null,
        PosTerminalViewModel? posTerminalViewModel = null,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? scopeFactory = null)
    {
        _environmentService = environmentService;
        _checkoutService = checkoutService;
        _roomBillingService = roomBillingService;
        _floorPlanViewModel = floorPlanViewModel;
        _posTerminalViewModel = posTerminalViewModel;
        _scopeFactory = scopeFactory;
    }

    public void Initialize(Guid orderId, long totalAmountCents, string tableNumber = "")
    {
#if MAUI_UI
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => Initialize(orderId, totalAmountCents, tableNumber));
            return;
        }
#endif
        OrderId = orderId;
        TotalDueCents = totalAmountCents;
        RemainingBalanceCents = totalAmountCents;
        TableNumber = tableNumber;
        ChangeDueCents = 0;
        IsCompleted = false;
        ReceiptNumber = string.Empty;
        AppliedTenders.Clear();
    }

    [RelayCommand]
    public void SelectPaymentMethod(PaymentMethod method)
    {
#if MAUI_UI
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => SelectPaymentMethod(method));
            return;
        }
#endif
        SelectedMethod = method;
        _environmentService.TriggerHapticFeedback(HapticFeedbackType.LightTap);
    }

    [RelayCommand]
    public void AddCashFastBill(long billAmountCents)
    {
#if MAUI_UI
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => AddCashFastBill(billAmountCents));
            return;
        }
#endif
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
#if MAUI_UI
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(FinalizeCheckoutAsync);
            return;
        }
#endif
        if (RemainingBalanceCents > 0)
        {
            // Settle remainder with currently selected method (including Cash if no bills were pre-selected)
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
                if (SelectedMethod == PaymentMethod.RoomCharge && _roomBillingService is not null && !string.IsNullOrWhiteSpace(RoomNumber))
                {
                    await _roomBillingService.PostRoomChargeAsync(
                        OrderId,
                        tableNumber: "T01",
                        roomNumber: RoomNumber,
                        guestName: string.IsNullOrWhiteSpace(GuestName) ? "Client Chambre" : GuestName,
                        amount: Money.FromCents(TotalDueCents),
                        tipAmount: Money.Zero(),
                        signatureDataUrl: null
                    ).ConfigureAwait(false);
                }

                ReceiptNumber = result.ReceiptNumber;
                ChangeDueCents = result.ChangeGivenCents;
                IsCompleted = true;
                _environmentService.TriggerHapticFeedback(HapticFeedbackType.Success);
            }
        }
        else
        {
            if (SelectedMethod == PaymentMethod.RoomCharge && _roomBillingService is not null && !string.IsNullOrWhiteSpace(RoomNumber))
            {
                await _roomBillingService.PostRoomChargeAsync(
                    OrderId,
                    tableNumber: !string.IsNullOrWhiteSpace(TableNumber) ? TableNumber : "T01",
                    roomNumber: RoomNumber,
                    guestName: string.IsNullOrWhiteSpace(GuestName) ? "Client Chambre" : GuestName,
                    amount: Money.FromCents(TotalDueCents),
                    tipAmount: Money.Zero(),
                    signatureDataUrl: null
                ).ConfigureAwait(false);
            }

            long totalCents = TotalDueCents > 0 ? TotalDueCents : AppliedTenders.Sum(t => t.AmountCents);
            long htCents = (long)Math.Round(totalCents / 1.10m);
            long vatCents = totalCents - htCents;
            string taxJson = $"{{\"10\":{vatCents}}}";
            DateTimeOffset now = DateTimeOffset.UtcNow;
            long nextSeq = 0;
            string prevHash = "GENESIS_0000000000000000000000000000000000000000000000000000000000000000";

            var scopeFactory = _scopeFactory
#if MAUI_UI
                ?? App.Services?.GetService<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>()
#endif
                ;
            if (scopeFactory is not null)
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetService<RestaurantPos.Client.Maui.Persistence.LocalAppDbContext>();
                    if (db is not null)
                    {
                        var lastReceipt = db.FiscalReceipts
                            .Where(r => r.TerminalId == TerminalId)
                            .OrderByDescending(r => r.SequenceNumber)
                            .FirstOrDefault();

                        nextSeq = (lastReceipt?.SequenceNumber ?? 0) + 1;
                        prevHash = lastReceipt?.SignatureHash ?? prevHash;

                        ReceiptNumber = $"{TerminalId}-{nextSeq:D6}";

                        string rawData = $"{prevHash}|{TerminalId}|{nextSeq}|{totalCents}|{now:O}|{taxJson}";
                        string sigHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawData)));

                        var fiscalReceipt = new FiscalReceipt
                        {
                            Id = Guid.NewGuid(),
                            TerminalId = TerminalId,
                            ReceiptNumber = ReceiptNumber,
                            OrderId = OrderId != Guid.Empty ? OrderId : Guid.NewGuid(),
                            SequenceNumber = nextSeq,
                            TotalTtcAmount = Money.FromCents(totalCents),
                            TotalHtAmount = Money.FromCents(htCents),
                            TaxBreakdownJson = taxJson,
                            SignatureHash = sigHash,
                            PreviousSignatureHash = prevHash,
                            CreatedAtUtc = now,
                            IsVoid = false
                        };

                        foreach (var tender in AppliedTenders)
                        {
                            fiscalReceipt.Tenders.Add(new PaymentTender
                            {
                                Id = Guid.NewGuid(),
                                FiscalReceiptId = fiscalReceipt.Id,
                                Method = tender.Method,
                                Amount = Money.FromCents(tender.AmountCents),
                                Tendered = Money.FromCents(tender.TenderedCents),
                                ChangeGiven = Money.FromCents(Math.Max(0, tender.TenderedCents - tender.AmountCents))
                            });
                        }

                        db.FiscalReceipts.Add(fiscalReceipt);
                        await db.SaveChangesAsync();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Local fiscal receipt write error: {ex}");
                }
            }

            if (string.IsNullOrWhiteSpace(ReceiptNumber))
            {
                nextSeq = System.Threading.Interlocked.Increment(ref _localSequence);
                ReceiptNumber = $"{TerminalId}-{nextSeq:D6}";
            }

            var tendersSnapshot = AppliedTenders.Select(t => new
            {
                Method = (int)t.Method,
                t.AmountCents,
                t.TenderedCents
            }).ToList();

            var currentOrderId = OrderId != Guid.Empty ? OrderId : Guid.NewGuid();
            var currentTable = !string.IsNullOrWhiteSpace(TableNumber) ? TableNumber : "Comptoir";
            var currentTerm = TerminalId;

            // Fire-and-forget sync to backend API Master POS
            _ = Task.Run(async () =>
            {
                try
                {
                    using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                    var payload = new
                    {
                        OrderId = currentOrderId,
                        TableNumber = currentTable,
                        TotalTtcCents = totalCents,
                        TerminalId = currentTerm,
                        Tenders = tendersSnapshot
                    };
                    await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, "http://127.0.0.1:5000/api/sync/receipt", payload);
                }
                catch
                {
                    // Offline or server not yet reachable
                }
            });

            IsCompleted = true;
            _environmentService?.TriggerHapticFeedback(HapticFeedbackType.Success);
        }

        if (IsCompleted)
        {
            var targetTable = !string.IsNullOrWhiteSpace(TableNumber)
                ? TableNumber
                : (_posTerminalViewModel?.ActiveTable ?? "");

#if MAUI_UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
#endif
                if (!string.IsNullOrWhiteSpace(targetTable))
                {
                    _floorPlanViewModel?.SetTableStatus(targetTable, TableStatus.Free);
                    _posTerminalViewModel?.ClearTableOrder(targetTable);
                }
                else
                {
                    _posTerminalViewModel?.ClearCart();
                }
#if MAUI_UI
            });
#endif
        }
    }
}
