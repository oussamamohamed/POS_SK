using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class OrderDiscountService : IOrderDiscountService
{
    private readonly AppDbContext _dbContext;

    public OrderDiscountService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order> ApplyGlobalDiscountAsync(
        Guid orderId,
        DiscountType type,
        decimal value,
        string reason,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (value < 0)
        {
            throw new ArgumentException("La valeur de la remise ne peut pas être négative.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif de la remise est obligatoire.");
        }

        if (type == DiscountType.Percentage && value > 100)
        {
            throw new ArgumentException("Le pourcentage de remise ne peut pas dépasser 100%.");
        }

        return await _dbContext.ExecuteInTransactionAsync(
            async ct2 =>
            {
                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct2)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Commande introuvable: {orderId}");

                long initialTotal = order.Items.Sum(i => i.CalculateTotalTtc().AmountInCents);

                order.GlobalDiscountType = type;
                order.GlobalDiscountValue = value;
                order.GlobalDiscountReason = reason.Trim();

                long newTotal = order.CalculateTotalTtc().AmountInCents;
                long savedCents = Math.Max(0, initialTotal - newTotal);

                var audit = new OrderDiscountAudit
                {
                    OrderId = orderId,
                    DiscountType = type,
                    Value = value,
                    AmountSaved = Money.FromCents(savedCents),
                    Reason = reason.Trim(),
                    AuthorizedByOperatorId = operatorId,
                    AppliedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.OrderDiscountAudits.Add(audit);
                await _dbContext.SaveChangesAsync(ct2).ConfigureAwait(false);

                return order;
            }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<Order> CompOrderItemAsync(
        Guid orderId,
        Guid orderItemId,
        string reason,
        Guid operatorId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Le motif de l'article offert est obligatoire.");
        }

        return await _dbContext.ExecuteInTransactionAsync(
            async ct2 =>
            {
                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct2)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Commande introuvable: {orderId}");

                var item = order.Items.FirstOrDefault(i => i.Id == orderItemId)
                    ?? throw new InvalidOperationException($"Article introuvable dans la commande: {orderItemId}");

                long originalCents = (item.UnitPrice * item.Quantity).AmountInCents;

                item.IsComp = true;
                item.CompReason = reason.Trim();

                var audit = new OrderDiscountAudit
                {
                    OrderId = orderId,
                    OrderItemId = orderItemId,
                    DiscountType = DiscountType.Comp,
                    Value = 100m,
                    AmountSaved = Money.FromCents(originalCents),
                    Reason = reason.Trim(),
                    AuthorizedByOperatorId = operatorId,
                    AppliedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.OrderDiscountAudits.Add(audit);
                await _dbContext.SaveChangesAsync(ct2).ConfigureAwait(false);

                return order;
            }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<Order> RemoveDiscountAsync(Guid orderId, CancellationToken ct = default)
    {
        return await _dbContext.ExecuteInTransactionAsync(
            async ct2 =>
            {
                var order = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == orderId, ct2)
                    .ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Commande introuvable: {orderId}");

                order.GlobalDiscountType = null;
                order.GlobalDiscountValue = 0;
                order.GlobalDiscountReason = null;

                await _dbContext.SaveChangesAsync(ct2).ConfigureAwait(false);
                return order;
            }, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<OrderDiscountAudit>> GetDiscountAuditTrailAsync(Guid orderId, CancellationToken ct = default)
    {
        return await _dbContext.OrderDiscountAudits
            .Where(a => a.OrderId == orderId)
            .OrderByDescending(a => a.AppliedAtUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }
}
