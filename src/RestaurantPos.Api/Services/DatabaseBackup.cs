using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Services;

/// <summary>Sauvegarde chiffrée de la base SQLite pendant que le serveur tourne, et restauration vérifiée : même contrat que `LocalBackupService` (iPad).</summary>
public static class DatabaseBackup
{
    public const int KeepCount = 14;
    public const int MinimumPassphraseLength = 8;
    private const string Prefix = "pos-backup-";
    private const string Extension = ".posbak";

    /// <summary>
    /// Écrit <c>pos-backup-AAAAMMJJThhmmssZ.posbak</c> dans <paramref name="folder"/> (créé au besoin) puis garde les <see cref="KeepCount"/> plus récentes.
    /// Copie en ligne, contrôlée (<c>integrity_check</c>), chiffrée, écrite sous un nom temporaire puis renommée : un échec ne laisse aucun fichier
    /// qui ressemble à une sauvegarde valide. La copie en clair n'existe que le temps de l'opération, lisible par le seul propriétaire.
    /// </summary>
    public static string Run(string connectionString, string folder, string passphrase, DateTimeOffset now, int iterations = BackupCrypto.DefaultIterations)
    {
        if (passphrase.Length < MinimumPassphraseLength)
        {
            throw new ArgumentException($"La phrase de sauvegarde doit comporter au moins {MinimumPassphraseLength} caractères.", nameof(passphrase));
        }
        Directory.CreateDirectory(folder);
        // Restes d'un arrêt en cours de sauvegarde (une seule sauvegarde à la fois).
        foreach (string stale in Directory.EnumerateFiles(folder, "pos-*.sqlite").Concat(Directory.EnumerateFiles(folder, "*" + Extension + ".tmp")))
        {
            File.Delete(stale);
        }

        string name = string.Create(CultureInfo.InvariantCulture, $"{Prefix}{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}{Extension}");
        string destination = Path.Combine(folder, name);
        string sealedPath = destination + ".tmp";
        string plainPath = Path.Combine(folder, "pos-" + Guid.NewGuid().ToString("N") + ".sqlite");
        try
        {
            CreatePrivate(plainPath).Dispose();
            var source = new SqliteConnectionStringBuilder(connectionString) { Pooling = false };
            using (var from = new SqliteConnection(source.ToString()))
            using (var to = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = plainPath, Pooling = false }.ToString()))
            {
                from.Open();
                to.Open();
                from.BackupDatabase(to);
                // Un seul fichier autonome : pas de -wal/-shm à côté de la copie.
                Execute(to, "PRAGMA journal_mode=DELETE");
                if (Execute(to, "PRAGMA integrity_check") is not "ok")
                {
                    throw new InvalidDataException("La copie de la base échoue au contrôle d'intégrité.");
                }
            }
            byte[] sealedBytes = BackupCrypto.Seal(File.ReadAllBytes(plainPath), passphrase, iterations);
            using (var output = CreatePrivate(sealedPath))
            {
                output.Write(sealedBytes);
            }
            File.Move(sealedPath, destination, overwrite: false);
        }
        finally
        {
            File.Delete(plainPath);
            File.Delete(sealedPath);
        }

        foreach (string old in Directory.EnumerateFiles(folder, Prefix + "*" + Extension)
                     .OrderBy(f => f, StringComparer.Ordinal)
                     .SkipLast(KeepCount))
        {
            File.Delete(old);
        }
        return destination;
    }

    /// <summary>
    /// Déchiffre vers <paramref name="destination"/> (nouvelle base SQLite) puis vérifie les chaînes fiscales. Refuse si <paramref name="destination"/>,
    /// <c>-wal</c> ou <c>-shm</c> existe : un fichier existant n'est jamais touché. Contrat : restaurer vers un nouveau chemin, serveur arrêté, puis
    /// l'échanger avec la base active. Phrase fausse ou chaîne rompue : rien n'est laissé à <paramref name="destination"/>.
    /// </summary>
    public static async Task RestoreAsync(string file, string passphrase, string destination, CancellationToken cancellationToken = default)
    {
        string[] paths = [destination, destination + "-wal", destination + "-shm"];
        if (paths.Any(File.Exists))
        {
            throw new IOException("Le fichier de destination existe déjà.");
        }
        byte[] plain = BackupCrypto.Open(File.ReadAllBytes(file), passphrase);
        using (var output = CreatePrivate(destination))
        {
            output.Write(plain);
        }
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString())
                .Options;
            await using var db = new AppDbContext(options);
            var result = await new FiscalChainVerificationService(db).VerifyAllChainsAsync(cancellationToken).ConfigureAwait(false);
            if (!result.IsValid)
            {
                string broken = string.Join(" ; ", result.Chains.Where(c => !c.IsValid).Take(3).Select(c => c.Chain + (c.Break is null ? "" : " " + c.Break.Reference)));
                throw new InvalidDataException("Chaîne fiscale rompue dans la sauvegarde : " + broken);
            }
        }
        catch
        {
            foreach (string path in paths) File.Delete(path);
            throw;
        }
    }

    /// <summary><c>--restore-backup fichier.posbak --to nouvelle.db</c>, phrase dans la variable d'environnement <c>Backup__Passphrase</c>. Code de sortie du processus.</summary>
    public static int RestoreCommand(string[] args)
    {
        int file = Array.IndexOf(args, "--restore-backup");
        int to = Array.IndexOf(args, "--to");
        string? passphrase = Environment.GetEnvironmentVariable("Backup__Passphrase");
        if (file < 0 || file + 1 >= args.Length || to < 0 || to + 1 >= args.Length || string.IsNullOrEmpty(passphrase))
        {
            Console.Error.WriteLine("Usage : Backup__Passphrase=<phrase> dotnet run --project src/RestaurantPos.Api -- --restore-backup <fichier.posbak> --to <nouvelle-base.db>");
            return 2;
        }
        try
        {
            RestoreAsync(args[file + 1], passphrase, args[to + 1]).GetAwaiter().GetResult();
            Console.WriteLine("Base restaurée et chaînes fiscales vérifiées : " + args[to + 1]);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Console.Error.WriteLine("ERREUR : " + ex.Message);
            return 1;
        }
    }

    private static FileStream CreatePrivate(string path)
    {
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new FileStream(path, options);
    }

    private static string? Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
