using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Api.Services;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

public sealed class DatabaseBackupTests : IDisposable
{
    private const string Phrase = "phrase-de-test";
    private const int Fast = 1000; // itérations PBKDF2 réduites : seul le format compte ici
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pos-backup-" + Guid.NewGuid().ToString("N"));

    public DatabaseBackupTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Cs(string path) => new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private AppDbContext NewDb(string name, bool brokenJournal = false)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(Cs(Path.Combine(_dir, name))).Options);
        db.Database.EnsureCreated();
        if (brokenJournal)
        {
            db.JournalEntries.Add(new TransactionJournalEntry
            {
                LocalSequence = 1, ChainSequence = 1, IdempotencyKey = "k", EventType = "TEST", PayloadJson = "{}",
                PreviousHash = NF525FiscalAuditService.GenesisHash, EntryHash = "mauvais",
            });
            db.SaveChanges();
        }
        return db;
    }

    [Fact]
    public void Run_WritesAnEncryptedFileThatOnlyTheRightPassphraseOpens()
    {
        string path = Path.Combine(_dir, "live.db");
        using var live = new SqliteConnection(Cs(path));
        live.Open();
        foreach (string sql in new[] { "PRAGMA journal_mode=WAL", "CREATE TABLE t(v TEXT)", "INSERT INTO t VALUES ('a')" })
        {
            using var c = live.CreateCommand();
            c.CommandText = sql;
            c.ExecuteNonQuery();
        }

        string file = DatabaseBackup.Run(Cs(path), Path.Combine(_dir, "out"), Phrase, At, Fast);

        Path.GetFileName(file).Should().Be("pos-backup-20261109T120000Z.posbak");
        Directory.GetFiles(Path.Combine(_dir, "out")).Should().ContainSingle("ni copie en clair ni .tmp laissés");
        byte[] bytes = File.ReadAllBytes(file);
        System.Text.Encoding.Latin1.GetString(bytes).Should().NotContain("SQLite format 3");
        Action wrong = () => BackupCrypto.Open(bytes, "autre-phrase-123");
        wrong.Should().Throw<InvalidDataException>();

        string plainPath = Path.Combine(_dir, "plain.db");
        File.WriteAllBytes(plainPath, BackupCrypto.Open(bytes, Phrase));
        using var copy = new SqliteConnection(Cs(plainPath));
        copy.Open();
        using var count = copy.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM t";
        count.ExecuteScalar().Should().Be(1L, "la ligne encore dans le WAL est copiée");
    }

    [Fact]
    public void Run_KeepsFourteenNewestAndRejectsShortPassphrase()
    {
        string path = Path.Combine(_dir, "live.db");
        using (var c = new SqliteConnection(Cs(path)))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "CREATE TABLE t(v TEXT)";
            cmd.ExecuteNonQuery();
        }
        string folder = Path.Combine(_dir, "out");
        for (int i = 0; i < 16; i++)
        {
            DatabaseBackup.Run(Cs(path), folder, Phrase, At.AddSeconds(i), Fast);
        }

        string[] names = Directory.GetFiles(folder).Select(f => Path.GetFileName(f)!).Order(StringComparer.Ordinal).ToArray();
        names.Should().HaveCount(14);
        names[0].Should().Be("pos-backup-20261109T120002Z.posbak");
        names[^1].Should().Be("pos-backup-20261109T120015Z.posbak");

        Action tooShort = () => DatabaseBackup.Run(Cs(path), folder, "court", At, Fast);
        tooShort.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task RestoreAsync_RestoresAValidBackup_AndNeverOverwrites()
    {
        using (NewDb("live.db")) { }
        string file = DatabaseBackup.Run(Cs(Path.Combine(_dir, "live.db")), Path.Combine(_dir, "out"), Phrase, At, Fast);
        string target = Path.Combine(_dir, "restored.db");

        await DatabaseBackup.RestoreAsync(file, Phrase, target);

        File.Exists(target).Should().BeTrue();
        Func<Task> again = () => DatabaseBackup.RestoreAsync(file, Phrase, target);
        await again.Should().ThrowAsync<IOException>();
        Func<Task> wrong = () => DatabaseBackup.RestoreAsync(file, "autre-phrase-123", Path.Combine(_dir, "other.db"));
        await wrong.Should().ThrowAsync<InvalidDataException>();
        File.Exists(Path.Combine(_dir, "other.db")).Should().BeFalse();
    }

    [Fact]
    public async Task RestoreAsync_RefusesABackupWithABrokenFiscalChain_AndLeavesNothing()
    {
        using (NewDb("live.db", brokenJournal: true)) { }
        string file = DatabaseBackup.Run(Cs(Path.Combine(_dir, "live.db")), Path.Combine(_dir, "out"), Phrase, At, Fast);
        string target = Path.Combine(_dir, "restored.db");

        Func<Task> restore = () => DatabaseBackup.RestoreAsync(file, Phrase, target);

        await restore.Should().ThrowAsync<InvalidDataException>().WithMessage("*Chaîne fiscale rompue*");
        File.Exists(target).Should().BeFalse();
    }
}
