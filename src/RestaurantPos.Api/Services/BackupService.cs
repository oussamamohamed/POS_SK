using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Services;

public sealed record BackupStatus(
    bool Configured, string Folder, DateTimeOffset? LastSuccessUtc, string? LastFileName, long? LastSizeBytes,
    DateTimeOffset? LastFailureUtc, string? LastError, int Copies, bool Overdue);

public sealed record BackupResult(string FileName, long SizeBytes);

/// <summary>Une seule sauvegarde à la fois (worker horaire et bouton « sauvegarder maintenant »), journal JET compris.</summary>
public sealed partial class BackupService : IDisposable
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupService> _logger;

    public BackupService(IConfiguration config, IServiceScopeFactory scopes, TimeProvider time, ILogger<BackupService> logger)
    {
        _config = config;
        _scopes = scopes;
        _time = time;
        _logger = logger;
    }

    public bool IsConfigured => Passphrase is not null;

    public void Dispose() => _gate.Dispose();

    private string? Passphrase => _config["Backup:Passphrase"] is { Length: >= DatabaseBackup.MinimumPassphraseLength } p ? p : null;
    private string Connection => _config.GetConnectionString("DefaultConnection") ?? "Data Source=restaurantpos.db";
    private string Folder => Path.GetFullPath(_config["Backup:Path"] ?? "backups");

    public async Task<BackupStatus> GetStatusAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var success = await LastAsync(db, JournalEventTypes.BackupCompleted, ct).ConfigureAwait(false);
        var failure = await LastAsync(db, JournalEventTypes.BackupFailed, ct).ConfigureAwait(false);
        string[] files = Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "pos-backup-*.posbak").Order(StringComparer.Ordinal).ToArray()
            : [];
        bool failed = failure is not null && (success is null || failure.OccurredAtUtc > success.OccurredAtUtc);
        return new BackupStatus(
            IsConfigured, Path.GetFileName(Folder), success?.OccurredAtUtc,
            files.Length > 0 ? Path.GetFileName(files[^1]) : null,
            files.Length > 0 ? new FileInfo(files[^1]).Length : null,
            failed ? failure!.OccurredAtUtc : null, failed ? Reason(failure!.PayloadJson) : null,
            files.Length, IsConfigured && (success is null || _time.GetUtcNow() - success.OccurredAtUtc > MaxAge));
    }

    /// <summary>Sauvegarde si la dernière date de plus de 24 h (ou n'existe pas). Rien si pas de phrase.</summary>
    public Task<BackupResult?> RunIfDueAsync(CancellationToken ct) => RunAsync(force: false, ct);

    /// <summary>Sauvegarde immédiate. Lève <see cref="InvalidOperationException"/> sans phrase configurée.</summary>
    public async Task<BackupResult> RunNowAsync(CancellationToken ct) =>
        await RunAsync(force: true, ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Aucune phrase de sauvegarde configurée.");

    private async Task<BackupResult?> RunAsync(bool force, CancellationToken ct)
    {
        if (Passphrase is not { } passphrase) return null;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var journal = scope.ServiceProvider.GetRequiredService<IFiscalJournal>();
            var now = _time.GetUtcNow();
            var lastDone = await LastAsync(db, JournalEventTypes.BackupCompleted, ct).ConfigureAwait(false);
            if (!force && lastDone is { } done && now - done.OccurredAtUtc <= MaxAge) return null;

            try
            {
                string file = await Task.Run(() => DatabaseBackup.Run(Connection, Folder, passphrase, now), ct).ConfigureAwait(false);
                var result = new BackupResult(Path.GetFileName(file), new FileInfo(file).Length);
                await journal.AppendAsync(JournalEventTypes.BackupCompleted, new { fileName = result.FileName, sizeBytes = result.SizeBytes }, cancellationToken: ct).ConfigureAwait(false);
                LogDone(_logger, file);
                return result;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Un seul BACKUP_FAILED par série d'échecs : le contrôle horaire ne remplit pas le journal.
                var lastFailed = await LastAsync(db, JournalEventTypes.BackupFailed, ct).ConfigureAwait(false);
                if (lastFailed is null || (lastDone is { } d && lastFailed.OccurredAtUtc < d.OccurredAtUtc))
                {
                    await journal.AppendAsync(JournalEventTypes.BackupFailed, new { reason = ex.Message }, cancellationToken: ct).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string? Reason(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Task<TransactionJournalEntry?> LastAsync(AppDbContext db, string eventType, CancellationToken ct) =>
        db.JournalEntries.AsNoTracking()
            .Where(j => j.EventType == eventType)
            .OrderByDescending(j => j.ChainSequence)
            .FirstOrDefaultAsync(ct);

    [LoggerMessage(EventId = 3020, Level = LogLevel.Information, Message = "Sauvegarde chiffrée de la base écrite : {File}.")]
    private static partial void LogDone(ILogger logger, string file);
}
