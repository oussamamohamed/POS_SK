using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class CatalogSyncService : ICatalogSyncService
{
    private readonly AppDbContext _dbContext;

    public CatalogSyncService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<CatalogSyncPayload> GetCatalogDeltaAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default)
    {
        var syncTime = DateTimeOffset.UtcNow;

        var categoriesQuery = _dbContext.Categories.AsNoTracking();
        var productsQuery = _dbContext.Products.AsNoTracking();
        var usersQuery = _dbContext.Users.AsNoTracking().Where(u => u.IsActive);

        if (sinceUtc.HasValue)
        {
            var threshold = sinceUtc.Value;
            categoriesQuery = categoriesQuery.Where(c => c.UpdatedAtUtc >= threshold);
            productsQuery = productsQuery.Where(p => p.UpdatedAtUtc >= threshold);
            usersQuery = usersQuery.Where(u => u.UpdatedAtUtc >= threshold);
        }

        var categories = await categoriesQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var products = await productsQuery.ToListAsync(cancellationToken).ConfigureAwait(false);
        var users = await usersQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

        return new CatalogSyncPayload(syncTime, categories, products, users);
    }
}
