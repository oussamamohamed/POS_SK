using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace RestaurantPos.Infrastructure.Persistence;

/// <summary>
/// Centralized database transaction helper for AppDbContext.
///
/// USAGE — Wrap any multi-step write operation to get atomicity:
/// <code>
///   return await _dbContext.ExecuteInTransactionAsync(async ct =>
///   {
///       // All reads, mutations and SaveChangesAsync here
///       // On exception: automatic rollback
///       // On success:   automatic commit
///       return result;
///   }, IsolationLevel.ReadCommitted, cancellationToken);
/// </code>
///
/// ISOLATION LEVELS (choose the right one for each use-case):
///   ReadCommitted  — Default. Prevents dirty reads. Use for most write operations.
///   RepeatableRead — Prevents dirty + non-repeatable reads. Use for concurrent order mutations.
///   Serializable   — Full isolation. Use ONLY for fiscal sequences (NF525) to prevent hash-chain races.
///
/// NOTE: The InMemory provider (used in unit tests) does not support transactions.
/// ExecuteInTransactionAsync detects this automatically and runs without a transaction,
/// preserving test compatibility without any changes to test code.
/// </summary>
public static class AppDbContextTransactionExtensions
{
    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction and returns its result.
    /// Automatically commits on success and rolls back on any exception.
    /// Safe to use with the InMemory provider (no-op transaction in that case).
    /// </summary>
    public static async Task<T> ExecuteInTransactionAsync<T>(
        this AppDbContext db,
        Func<CancellationToken, Task<T>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        // InMemory provider does not support transactions — run without one (tests only).
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
        {
            return await operation(cancellationToken).ConfigureAwait(false);
        }

        IDbContextTransaction? transaction = null;
        try
        {
            transaction = await db.Database
                .BeginTransactionAsync(isolationLevel, cancellationToken)
                .ConfigureAwait(false);

            T result = await operation(cancellationToken).ConfigureAwait(false);

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Executes <paramref name="operation"/> inside a database transaction (void overload).
    /// Automatically commits on success and rolls back on any exception.
    /// </summary>
    public static async Task ExecuteInTransactionAsync(
        this AppDbContext db,
        Func<CancellationToken, Task> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        await db.ExecuteInTransactionAsync<bool>(
            async ct =>
            {
                await operation(ct).ConfigureAwait(false);
                return true;
            },
            isolationLevel,
            cancellationToken).ConfigureAwait(false);
    }
}
