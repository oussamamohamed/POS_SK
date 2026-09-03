using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

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

    Task<CheckoutResult> VoidReceiptAsync(
        Guid originalReceiptId,
        string terminalId,
        Guid operatorId,
        CancellationToken cancellationToken = default);

    IReadOnlyList<long> CalculateEqualSplitPartitions(long totalAmountCents, int numberOfGuests);
}
