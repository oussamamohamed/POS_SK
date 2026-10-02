using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Infrastructure.Persistence;

public class FiscalImmutabilityInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ValidateFiscalImmutability(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ValidateFiscalImmutability(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void ValidateFiscalImmutability(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Modified && entry.State != EntityState.Deleted)
            {
                continue;
            }

            if (entry.Entity is FiscalReceipt)
            {
                throw new InvalidOperationException("NF525 INV-3: Fiscal receipts are append-only and immutable.");
            }

            if (entry.Entity is PaymentTender)
            {
                throw new InvalidOperationException("NF525 INV-3: Payment tenders are immutable.");
            }

            if (entry.Entity is DailyFiscalClosure)
            {
                throw new InvalidOperationException("NF525 INV-3: Daily fiscal closures are immutable.");
            }

            if (entry.Entity is FiscalPeriodClosure)
            {
                throw new InvalidOperationException("NF525 INV-3: Period closures are immutable.");
            }

            if (entry.Entity is FiscalArchive)
            {
                throw new InvalidOperationException("NF525 INV-3: Fiscal archives are immutable.");
            }

            if (entry.Entity is TransactionJournalEntry jet && jet.ChainSequence.HasValue)
            {
                throw new InvalidOperationException("NF525 INV-3: Chained journal entries are immutable.");
            }
        }
    }
}
