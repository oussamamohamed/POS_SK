using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// Deterministic fake for <see cref="ICheckoutPaymentService"/>.
/// Always returns a successful payment result and performs real integer split arithmetic.
/// </summary>
public sealed class FakeCheckoutPaymentService : ICheckoutPaymentService
{
    public Task<CheckoutResult> ProcessPaymentTendersAsync(
        Guid orderId,
        string terminalId,
        IReadOnlyList<PaymentTenderRequest> tenders,
        CancellationToken cancellationToken = default)
    {
        long totalPaid = tenders.Sum(t => t.AmountInCents);
        var result = new CheckoutResult(
            IsSuccess: true,
            TotalPaidCents: totalPaid,
            ChangeGivenCents: 0,
            RemainingBalanceCents: 0,
            ReceiptNumber: $"{terminalId}-SIM-{orderId:N}"[..24],
            FiscalSignature: "SIM-SIG");
        return Task.FromResult(result);
    }

    public Task<CheckoutResult> VoidReceiptAsync(
        Guid originalReceiptId,
        string terminalId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var result = new CheckoutResult(
            IsSuccess: true,
            TotalPaidCents: 0,
            ChangeGivenCents: 0,
            RemainingBalanceCents: 0,
            ReceiptNumber: $"VOID-{originalReceiptId:N}"[..24],
            FiscalSignature: "VOID-SIG");
        return Task.FromResult(result);
    }

    /// <summary>
    /// Real equal-split: each part = totalCents / n;
    /// remainder is added to the last partition.
    /// </summary>
    public IReadOnlyList<long> CalculateEqualSplitPartitions(long totalAmountCents, int numberOfGuests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numberOfGuests);

        long baseAmount = totalAmountCents / numberOfGuests;
        long remainder = totalAmountCents % numberOfGuests;

        var parts = new long[numberOfGuests];
        for (int i = 0; i < numberOfGuests; i++)
            parts[i] = baseAmount;

        parts[numberOfGuests - 1] += remainder;
        return parts;
    }
}
