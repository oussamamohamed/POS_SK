using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Api.Services;

/// <summary>Applique la durée de conservation des réservations (téléphone, puis ligne entière) au lancement puis chaque heure.</summary>
public sealed partial class ReservationPurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<ReservationPurgeService> _logger;

    public ReservationPurgeService(IServiceScopeFactory scopes, ILogger<ReservationPurgeService> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                using var scope = _scopes.CreateScope();
                int touched = await scope.ServiceProvider.GetRequiredService<IReservationService>().PurgeAsync(stoppingToken).ConfigureAwait(false);
                if (touched > 0) LogPurged(_logger, touched);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogPurgeFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Purge des réservations : {Count} ligne(s) touchée(s).")]
    private static partial void LogPurged(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purge des réservations en échec ; nouvel essai à la prochaine heure.")]
    private static partial void LogPurgeFailed(ILogger logger, Exception exception);
}
