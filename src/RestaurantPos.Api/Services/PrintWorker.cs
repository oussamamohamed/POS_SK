using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Services;

/// <summary>Dépile PrintJobs : une tâche par imprimante (imprimantes en parallèle), purge quotidienne.</summary>
public sealed partial class PrintWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly PrintQueueProcessor _processor;
    private readonly PrintSignal _signal;
    private readonly TimeProvider _time;
    private readonly ILogger<PrintWorker> _logger;
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public PrintWorker(PrintQueueProcessor processor, PrintSignal signal, TimeProvider time, ILogger<PrintWorker> logger)
    {
        _processor = processor;
        _signal = signal;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextPurge = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var printerId in await _processor.PrepareAsync(stoppingToken).ConfigureAwait(false))
                {
                    if (_running.TryGetValue(printerId, out var running) && !running.IsCompleted) continue;
                    _running[printerId] = RunPrinterAsync(printerId, stoppingToken);
                }
                if (_time.GetUtcNow() >= nextPurge)
                {
                    await _processor.PurgeAsync(stoppingToken).ConfigureAwait(false);
                    nextPurge = _time.GetUtcNow().AddDays(1);
                }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogCycleFailed(_logger, ex);
            }
            await _signal.WaitAsync(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunPrinterAsync(Guid printerId, CancellationToken ct)
    {
        try
        {
            await _processor.ProcessPrinterAsync(printerId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            LogCycleFailed(_logger, ex);
        }
    }

    [LoggerMessage(EventId = 3010, Level = LogLevel.Error, Message = "Service d'impression en erreur.")]
    private static partial void LogCycleFailed(ILogger logger, Exception exception);
}
