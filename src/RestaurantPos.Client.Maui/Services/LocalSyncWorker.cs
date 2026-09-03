using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Services;

public class LocalSyncWorker : ILocalSyncWorker
{
    private readonly LocalAppDbContext _dbContext;
    private readonly HttpClient? _httpClient;
    private string? _masterServerUrl;

    public LocalSyncWorker(LocalAppDbContext dbContext, HttpClient? httpClient = null)
    {
        _dbContext = dbContext;
        _httpClient = httpClient;
    }

    public void ConfigureMasterServerUrl(string url)
    {
        _masterServerUrl = url?.TrimEnd('/');
    }

    public async Task<int> ProcessOutboxQueueAsync(CancellationToken cancellationToken = default)
    {
        var pending = await _dbContext.OutboxMessages
            .Where(m => m.Status == SyncStatus.Pending || m.Status == SyncStatus.Failed)
            .OrderBy(m => m.Id)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (pending.Count == 0) return 0;

        int processed = 0;

        if (!string.IsNullOrWhiteSpace(_masterServerUrl) && _httpClient is not null)
        {
            try
            {
                var payload = new
                {
                    Messages = pending.Select(m => new
                    {
                        Id = m.Id,
                        TerminalId = m.TerminalId,
                        EventType = m.EventType,
                        IdempotencyKey = m.IdempotencyKey,
                        PayloadJson = m.PayloadJson,
                        CreatedAtUtc = m.CreatedAtUtc
                    }).ToList()
                };

                var response = await _httpClient.PostAsJsonAsync(
                    $"{_masterServerUrl}/api/sync/batch",
                    payload,
                    cancellationToken
                ).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    foreach (var msg in pending)
                    {
                        msg.Status = SyncStatus.Completed;
                        msg.LastAttemptUtc = DateTimeOffset.UtcNow;
                        processed++;
                    }
                }
                else
                {
                    foreach (var msg in pending)
                    {
                        msg.Status = SyncStatus.Failed;
                        msg.LastAttemptUtc = DateTimeOffset.UtcNow;
                    }
                }
            }
            catch (Exception)
            {
                foreach (var msg in pending)
                {
                    msg.Status = SyncStatus.Failed;
                    msg.LastAttemptUtc = DateTimeOffset.UtcNow;
                }
            }
        }
        else
        {
            // Local offline simulation / reconciliation
            foreach (var msg in pending)
            {
                msg.Status = SyncStatus.Completed;
                msg.LastAttemptUtc = DateTimeOffset.UtcNow;
                processed++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return processed;
    }
}
