using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public record CatalogSyncPayload(
    DateTimeOffset SyncTimestampUtc,
    IReadOnlyList<Category> Categories,
    IReadOnlyList<Product> Products,
    IReadOnlyList<User> Operators);

public interface ICatalogSyncService
{
    Task<CatalogSyncPayload> GetCatalogDeltaAsync(DateTimeOffset? sinceUtc = null, CancellationToken cancellationToken = default);
}
