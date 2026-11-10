using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RestaurantPos.Api.Services;

/// <summary>
/// Comme l'iPad : contrôle chaque heure et au lancement, sauvegarde si la dernière (<c>BACKUP_COMPLETED</c> du JET) date de plus de 24 h ou n'existe pas
/// (<see cref="BackupService"/>). Phrase dans <c>Backup:Passphrase</c> (8 caractères au moins, variable <c>Backup__Passphrase</c>), dossier <c>Backup:Path</c>
/// (<c>backups</c>). Sans phrase : aucune sauvegarde.
/// </summary>
public sealed partial class DatabaseBackupWorker : BackgroundService
{
    private readonly BackupService _backup;
    private readonly ILogger<DatabaseBackupWorker> _logger;

    public DatabaseBackupWorker(BackupService backup, ILogger<DatabaseBackupWorker> logger)
    {
        _backup = backup;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_backup.IsConfigured)
        {
            LogNoPassphrase(_logger, DatabaseBackup.MinimumPassphraseLength);
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await _backup.RunIfDueAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                LogFailed(_logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    [LoggerMessage(EventId = 3021, Level = LogLevel.Error, Message = "Sauvegarde de la base en échec ; nouvel essai dans l'heure.")]
    private static partial void LogFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3022, Level = LogLevel.Warning, Message = "Aucune sauvegarde automatique : définir Backup:Passphrase (variable Backup__Passphrase, {Minimum} caractères au moins).")]
    private static partial void LogNoPassphrase(ILogger logger, int minimum);
}
