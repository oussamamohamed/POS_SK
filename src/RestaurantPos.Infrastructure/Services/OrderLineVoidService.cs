using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class OrderLineVoidService : IOrderLineVoidService
{
    private readonly AppDbContext _dbContext;
    private readonly IFiscalJournal _journal;

    public OrderLineVoidService(AppDbContext dbContext, IFiscalJournal journal)
    {
        _dbContext = dbContext;
        _journal = journal;
    }

    public Task<OrderLineVoidResult> VoidAsync(Guid orderId, Guid orderItemId, int quantity, string? reason, Guid operatorId, Guid? idempotencyKey = null, CancellationToken ct = default) =>
        _dbContext.ExecuteInTransactionAsync(async txCt =>
        {
            // Lue dans la transaction Serializable (BEGIN IMMEDIATE sous SQLite) : deux requêtes de même clé ne peuvent pas appliquer toutes deux.
            var reasonHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reason?.Trim() ?? string.Empty)));
            if (idempotencyKey is { } key)
            {
                var done = await _dbContext.OrderLineVoidRequests.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.IdempotencyKey == key, txCt).ConfigureAwait(false);
                if (done is not null)
                {
                    return done.OrderId == orderId && done.OrderItemId == orderItemId && done.Quantity == quantity && done.ReasonHash == reasonHash
                        ? new OrderLineVoidResult(done.VoidId is null ? OrderLineVoidOutcome.Corrected : OrderLineVoidOutcome.Voided, done.VoidId, null, done.TotalTtcCents, Replayed: true)
                        : Result(OrderLineVoidOutcome.IdempotencyKeyReuse);
                }
            }

            var order = await _dbContext.Orders.Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == orderId, txCt).ConfigureAwait(false);
            // IsDispatched est relu ici, dans la transaction : un envoi en cuisine concurrent fait passer la demande en annulation.
            var item = order?.Items.FirstOrDefault(i => i.Id == orderItemId);
            if (order is null || item is null) return Result(OrderLineVoidOutcome.NotFound);
            if (!PaymentSettlementRules.IsSettleable(order.Status)) return Result(OrderLineVoidOutcome.NotModifiable);

            var receipts = await PaymentSettlementRules.ActiveReceiptsAsync(_dbContext, orderId, txCt).ConfigureAwait(false);
            if (receipts.Count > 0)
            {
                return new OrderLineVoidResult(OrderLineVoidOutcome.OrderHasReceipts, null,
                    string.Join(", ", receipts.OrderBy(r => r.SequenceNumber).Select(r => r.ReceiptNumber)));
            }

            if (quantity < 1 || quantity > item.Quantity) return Result(OrderLineVoidOutcome.InvalidQuantity);

            if (!item.IsDispatched)
            {
                RemoveQuantity(item, quantity);
                await _dbContext.SaveChangesAsync(txCt).ConfigureAwait(false);
                return await AppliedAsync(order, OrderLineVoidOutcome.Corrected, null).ConfigureAwait(false);
            }

            var op = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == operatorId, txCt).ConfigureAwait(false);
            if (op is null || !op.IsActive || !op.CanVoidItems()) return Result(OrderLineVoidOutcome.Forbidden);
            if (string.IsNullOrWhiteSpace(reason)) return Result(OrderLineVoidOutcome.ReasonRequired);
            reason = reason.Trim();
            if (reason.Length > OrderLineVoid.MaxReasonLength) return Result(OrderLineVoidOutcome.ReasonTooLong);

            // Article de bon encore actif pour cette ligne (nul pour une ligne envoyée avant OrderItemId).
            var ticketItem = await _dbContext.KitchenTicketItems
                .Where(t => t.OrderItemId == orderItemId && t.Status != TicketItemStatus.Cancelled)
                .FirstOrDefaultAsync(txCt).ConfigureAwait(false);
            if (ticketItem is not null)
            {
                ticketItem.CancelledQuantity = Math.Min(ticketItem.Quantity, ticketItem.CancelledQuantity + quantity);
                if (ticketItem.CancelledQuantity >= ticketItem.Quantity) ticketItem.Status = TicketItemStatus.Cancelled;
            }

            var table = await _dbContext.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == order.TableNumber, txCt).ConfigureAwait(false);
            var line = new OrderLineVoid
            {
                OrderId = orderId,
                OrderItemId = orderItemId,
                TicketId = ticketItem?.TicketId,
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = quantity,
                UnitPrice = item.UnitPrice + item.ModifiersPriceExtra,
                ModifiersSummary = item.SelectedModifiers.Count > 0 ? string.Join(", ", item.SelectedModifiers) : null,
                KitchenComment = item.KitchenComment,
                PreparationStationId = item.PreparationStationId,
                Reason = reason,
                OperatorId = op.Id,
                OperatorName = op.Name,
                ServerName = table?.AssignedWaiterName ?? string.Empty,
                TableNumber = order.TableNumber
            };
            _dbContext.OrderLineVoids.Add(line);
            RemoveQuantity(item, quantity);
            await _dbContext.SaveChangesAsync(txCt).ConfigureAwait(false);

            await _journal.AppendAsync(
                JournalEventTypes.OrderLineVoided,
                new
                {
                    orderId,
                    tableNumber = line.TableNumber,
                    productId = line.ProductId,
                    productName = line.ProductName,
                    quantity,
                    unitPriceCents = line.UnitPrice.AmountInCents,
                    reason = line.Reason,
                    operatorId = op.Id,
                    operatorName = op.Name,
                    ticketId = line.TicketId
                },
                operatorId: op.Id,
                cancellationToken: txCt).ConfigureAwait(false);

            return await AppliedAsync(order, OrderLineVoidOutcome.Voided, line.Id).ConfigureAwait(false);

            // Succès : avec une clé, on la garde (même transaction) et on purge les clés de plus de RetentionDays jours.
            async Task<OrderLineVoidResult> AppliedAsync(Order applied, OrderLineVoidOutcome outcome, Guid? voidId)
            {
                var total = applied.TotalTtc.AmountInCents;
                if (idempotencyKey is { } k)
                {
                    var cutoff = DateTime.UtcNow.AddDays(-OrderLineVoidRequest.RetentionDays);
                    _dbContext.OrderLineVoidRequests.RemoveRange(await _dbContext.OrderLineVoidRequests
                        .Where(r => r.CreatedAtUtc < cutoff).ToListAsync(txCt).ConfigureAwait(false));
                    _dbContext.OrderLineVoidRequests.Add(new OrderLineVoidRequest
                    {
                        IdempotencyKey = k, OrderId = orderId, OrderItemId = orderItemId, Quantity = quantity,
                        ReasonHash = reasonHash, VoidId = voidId, TotalTtcCents = total,
                    });
                    await _dbContext.SaveChangesAsync(txCt).ConfigureAwait(false);
                }
                return new OrderLineVoidResult(outcome, voidId, null, total);
            }
        }, System.Data.IsolationLevel.Serializable, ct);

    private void RemoveQuantity(OrderItem item, int quantity)
    {
        if (quantity == item.Quantity) _dbContext.OrderItems.Remove(item);
        else item.Quantity -= quantity;
    }

    private static OrderLineVoidResult Result(OrderLineVoidOutcome outcome) => new(outcome, null, null);
}
