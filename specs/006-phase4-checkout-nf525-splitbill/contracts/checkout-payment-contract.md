# Contract: Multi-Payment Checkout & Split Bill

**Feature**: `006-phase4-checkout-nf525-splitbill`
**Domain**: Order Settlement & Tender Processing

## 1. Payment Interfaces & Value Objects

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public enum PaymentMethod
{
    Cash = 0,
    CreditCard = 1,
    MealVoucher = 2,
    GiftCard = 3
}

public record PaymentTenderRequest(
    PaymentMethod Method,
    long AmountInCents,
    long TenderedInCents);

public record CheckoutResult(
    bool IsSuccess,
    long TotalPaidCents,
    long ChangeGivenCents,
    long RemainingBalanceCents,
    string ReceiptNumber,
    string? FiscalSignature);

public interface ICheckoutPaymentService
{
    Task<CheckoutResult> ProcessPaymentTendersAsync(
        Guid orderId,
        string terminalId,
        IReadOnlyList<PaymentTenderRequest> tenders,
        CancellationToken cancellationToken = default);

    IReadOnlyList<long> CalculateEqualSplitPartitions(long totalAmountCents, int numberOfGuests);
}
```
