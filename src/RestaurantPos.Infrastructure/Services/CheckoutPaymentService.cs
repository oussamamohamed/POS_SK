using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class CheckoutPaymentService : ICheckoutPaymentService
{
    private readonly AppDbContext _dbContext;
    private readonly INF525FiscalAuditService _fiscalService;

    public CheckoutPaymentService(
        AppDbContext dbContext,
        INF525FiscalAuditService fiscalService)
    {
        _dbContext = dbContext;
        _fiscalService = fiscalService;
    }

    public IReadOnlyList<long> CalculateEqualSplitPartitions(long totalAmountCents, int numberOfGuests)
    {
        if (numberOfGuests <= 1) return [totalAmountCents];

        long basePart = totalAmountCents / numberOfGuests;
        long remainder = totalAmountCents % numberOfGuests;

        var parts = new List<long>(numberOfGuests);
        for (int i = 0; i < numberOfGuests; i++)
        {
            long amount = basePart + (i < remainder ? 1 : 0);
            parts.Add(amount);
        }

        return parts;
    }

    public async Task<CheckoutResult> ProcessPaymentTendersAsync(
        Guid orderId,
        string terminalId,
        IReadOnlyList<PaymentTenderRequest> tenders,
        CancellationToken cancellationToken = default)
    {
        // SERIALIZABLE ensures no two concurrent checkouts can claim the same NF525 sequence number.
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct)
                    .ConfigureAwait(false);

                if (order is null)
                {
                    return new CheckoutResult(false, 0, 0, 0, string.Empty, null);
                }

                long totalDueCents = order.TotalTtc.AmountInCents;
                long totalPaidCents = tenders.Sum(t => t.AmountInCents);
                long totalTenderedCents = tenders.Sum(t => t.TenderedInCents);

                long changeGivenCents = Math.Max(0, totalTenderedCents - totalDueCents);
                long remainingBalanceCents = Math.Max(0, totalDueCents - totalPaidCents);

                // Fetch last receipt for terminal to chain cryptographic signature (inside transaction lock)
                var lastReceipt = await _dbContext.FiscalReceipts
                    .Where(r => r.TerminalId == terminalId)
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                long nextSequence = (lastReceipt?.SequenceNumber ?? 0) + 1;
                string receiptNumber = $"{terminalId}-{nextSequence:D6}";
                string prevHash = lastReceipt?.SignatureHash ?? NF525FiscalAuditService.GenesisHash;

                var vatDict = new Dictionary<decimal, long>();
                foreach (var item in order.Items)
                {
                    if (!vatDict.TryGetValue(item.TaxRatePercent, out long amt))
                    {
                        vatDict[item.TaxRatePercent] = item.TaxAmount.AmountInCents;
                    }
                    else
                    {
                        vatDict[item.TaxRatePercent] = amt + item.TaxAmount.AmountInCents;
                    }
                }
                string taxJson = JsonSerializer.Serialize(vatDict);

                var now = DateTimeOffset.UtcNow;
                string sigHash = _fiscalService.ComputeReceiptHashSignature(
                    prevHash,
                    terminalId,
                    nextSequence,
                    totalDueCents,
                    now,
                    taxJson
                );

                var fiscalReceipt = new FiscalReceipt
                {
                    Id = UuidV7.NewGuid(),
                    TerminalId = terminalId,
                    ReceiptNumber = receiptNumber,
                    OrderId = order.Id,
                    SequenceNumber = nextSequence,
                    TotalTtcAmount = order.TotalTtc,
                    TotalHtAmount = order.TotalHt,
                    TaxBreakdownJson = taxJson,
                    PreviousSignatureHash = prevHash,
                    SignatureHash = sigHash,
                    CreatedAtUtc = now
                };

                foreach (var tenderReq in tenders)
                {
                    long change = (tenderReq.Method == PaymentMethod.Cash)
                        ? Math.Max(0, tenderReq.TenderedInCents - tenderReq.AmountInCents)
                        : 0;

                    fiscalReceipt.Tenders.Add(new PaymentTender
                    {
                        FiscalReceiptId = fiscalReceipt.Id,
                        Method = tenderReq.Method,
                        Amount = Money.FromCents(tenderReq.AmountInCents),
                        Tendered = Money.FromCents(tenderReq.TenderedInCents),
                        ChangeGiven = Money.FromCents(change)
                    });
                }

                _dbContext.FiscalReceipts.Add(fiscalReceipt);

                if (remainingBalanceCents == 0)
                {
                    order.Status = OrderStatus.Paid;

                    var table = await _dbContext.DiningTables
                        .FindAsync([order.TableNumber], ct)
                        .ConfigureAwait(false);
                    if (table is not null)
                    {
                        table.Status = TableStatus.Paid;
                        table.ActiveOrderId = null;
                    }
                }

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return new CheckoutResult(
                    IsSuccess: true,
                    TotalPaidCents: totalPaidCents,
                    ChangeGivenCents: changeGivenCents,
                    RemainingBalanceCents: remainingBalanceCents,
                    ReceiptNumber: receiptNumber,
                    FiscalSignature: sigHash
                );
            },
            System.Data.IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<CheckoutResult> VoidReceiptAsync(
        Guid originalReceiptId,
        string terminalId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var original = await _dbContext.FiscalReceipts
                    .Include(r => r.Tenders)
                    .FirstOrDefaultAsync(r => r.Id == originalReceiptId, ct)
                    .ConfigureAwait(false);

                if (original is null || original.IsVoid)
                {
                    return new CheckoutResult(false, 0, 0, 0, string.Empty, null);
                }

                // Mark original as void
                original.IsVoid = true;

                // Create a corrective receipt
                var lastReceipt = await _dbContext.FiscalReceipts
                    .Where(r => r.TerminalId == terminalId)
                    .OrderByDescending(r => r.SequenceNumber)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                long nextSequence = (lastReceipt?.SequenceNumber ?? 0) + 1;
                string receiptNumber = $"{terminalId}-VOID-{nextSequence:D6}";
                string prevHash = lastReceipt?.SignatureHash ?? NF525FiscalAuditService.GenesisHash;

                // Invert VAT values for the void receipt
                var originalVat = string.IsNullOrWhiteSpace(original.TaxBreakdownJson) || original.TaxBreakdownJson == "{}"
                    ? new Dictionary<string, long>()
                    : JsonSerializer.Deserialize<Dictionary<string, long>>(original.TaxBreakdownJson) ?? new Dictionary<string, long>();
                    
                var voidVat = new Dictionary<string, long>();
                foreach (var kvp in originalVat)
                {
                    voidVat[kvp.Key] = -kvp.Value;
                }
                string taxJson = JsonSerializer.Serialize(voidVat);

                var now = DateTimeOffset.UtcNow;
                string sigHash = _fiscalService.ComputeReceiptHashSignature(
                    prevHash,
                    terminalId,
                    nextSequence,
                    -original.TotalTtcAmount.AmountInCents,
                    now,
                    taxJson
                );

                var voidReceipt = new FiscalReceipt
                {
                    Id = UuidV7.NewGuid(),
                    TerminalId = terminalId,
                    ReceiptNumber = receiptNumber,
                    OrderId = original.OrderId,
                    SequenceNumber = nextSequence,
                    TotalTtcAmount = Money.FromCents(-original.TotalTtcAmount.AmountInCents),
                    TotalHtAmount = Money.FromCents(-original.TotalHtAmount.AmountInCents),
                    TaxBreakdownJson = taxJson,
                    PreviousSignatureHash = prevHash,
                    SignatureHash = sigHash,
                    CreatedAtUtc = now,
                    IsVoid = false,
                    VoidedReceiptId = original.Id
                };

                foreach (var tender in original.Tenders)
                {
                    voidReceipt.Tenders.Add(new PaymentTender
                    {
                        FiscalReceiptId = voidReceipt.Id,
                        Method = tender.Method,
                        Amount = Money.FromCents(-tender.Amount.AmountInCents),
                        Tendered = Money.FromCents(-tender.Tendered.AmountInCents),
                        ChangeGiven = Money.FromCents(-tender.ChangeGiven.AmountInCents)
                    });
                }

                _dbContext.FiscalReceipts.Add(voidReceipt);

                var order = await _dbContext.Orders.FirstOrDefaultAsync(o => o.Id == original.OrderId, ct);
                if (order is not null)
                {
                    order.Status = OrderStatus.Cancelled;
                }

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return new CheckoutResult(
                    IsSuccess: true,
                    TotalPaidCents: -original.TotalTtcAmount.AmountInCents,
                    ChangeGivenCents: 0,
                    RemainingBalanceCents: 0,
                    ReceiptNumber: receiptNumber,
                    FiscalSignature: sigHash
                );
            },
            System.Data.IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
    }
}

