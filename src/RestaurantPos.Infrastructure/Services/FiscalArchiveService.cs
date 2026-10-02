using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public partial class FiscalArchiveService : IFiscalArchiveService
{
    private readonly AppDbContext _dbContext;
    private readonly IFiscalJournal _fiscalJournal;
    private readonly string _archivesDirectory;

    public FiscalArchiveService(
        AppDbContext dbContext,
        IFiscalJournal? fiscalJournal = null,
        IConfiguration? configuration = null,
        string? archivesDirectory = null)
    {
        _dbContext = dbContext;
        _fiscalJournal = fiscalJournal ?? new FiscalJournalService(dbContext);

        _archivesDirectory = archivesDirectory
            ?? configuration?["Archives:Path"]
            ?? Path.Combine(AppContext.BaseDirectory, "archives");

        if (!Path.IsPathRooted(_archivesDirectory))
        {
            _archivesDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _archivesDirectory));
        }
    }

    public async Task<FiscalArchiveDto> CreateArchiveAsync(
        Guid periodClosureId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var periodClosure = await _dbContext.PeriodClosures
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == periodClosureId, cancellationToken)
            .ConfigureAwait(false);

        if (periodClosure == null)
        {
            throw new KeyNotFoundException($"Period closure {periodClosureId} not found.");
        }

        var alreadyExists = await _dbContext.FiscalArchives
            .AsNoTracking()
            .AnyAsync(a => a.PeriodClosureId == periodClosureId, cancellationToken)
            .ConfigureAwait(false);

        if (alreadyExists)
        {
            throw new ArchiveExistsException();
        }

        var archiveId = UuidV7.NewGuid();
        var now = DateTimeOffset.UtcNow;

        // Fetch data covered by period
        var receipts = await _dbContext.FiscalReceipts
            .Include(r => r.Tenders)
            .AsNoTracking()
            .Where(r => (periodClosure.TerminalId == "POS_MAIN_TERM" || r.TerminalId == periodClosure.TerminalId)
                     && r.CreatedAtUtc >= periodClosure.PeriodStartUtc
                     && r.CreatedAtUtc <= periodClosure.PeriodEndUtc)
            .OrderBy(r => r.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var closures = await _dbContext.DailyFiscalClosures
            .AsNoTracking()
            .Where(c => c.TerminalId == periodClosure.TerminalId
                     && c.PeriodEndUtc >= periodClosure.PeriodStartUtc
                     && c.PeriodEndUtc <= periodClosure.PeriodEndUtc)
            .OrderBy(c => c.ClosureSequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var periodClosures = await _dbContext.PeriodClosures
            .AsNoTracking()
            .Where(p => p.TerminalId == periodClosure.TerminalId
                     && (p.Id == periodClosure.Id
                         || (periodClosure.PeriodType == FiscalPeriodType.Annual
                             && p.PeriodType == FiscalPeriodType.Monthly
                             && p.PeriodStartUtc >= periodClosure.PeriodStartUtc
                             && p.PeriodEndUtc <= periodClosure.PeriodEndUtc)))
            .OrderBy(p => p.PeriodType)
            .ThenBy(p => p.ClosureSequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var journalEntries = await _dbContext.JournalEntries
            .AsNoTracking()
            .Where(j => j.OccurredAtUtc >= periodClosure.PeriodStartUtc
                     && j.OccurredAtUtc <= periodClosure.PeriodEndUtc)
            .OrderBy(j => j.ChainSequence ?? 0)
            .ThenBy(j => j.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        byte[] receiptsBytes = JsonSerializer.SerializeToUtf8Bytes(receipts, jsonOptions);
        byte[] closuresBytes = JsonSerializer.SerializeToUtf8Bytes(closures, jsonOptions);
        byte[] periodClosuresBytes = JsonSerializer.SerializeToUtf8Bytes(periodClosures, jsonOptions);
        byte[] journalBytes = JsonSerializer.SerializeToUtf8Bytes(journalEntries, jsonOptions);

        static string HashBytes(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

        var manifest = new
        {
            archiveId,
            periodClosureId = periodClosure.Id,
            terminalId = periodClosure.TerminalId,
            periodType = (int)periodClosure.PeriodType,
            periodKey = periodClosure.PeriodKey,
            periodStartUtc = periodClosure.PeriodStartUtc,
            periodEndUtc = periodClosure.PeriodEndUtc,
            createdAtUtc = now,
            counts = new
            {
                receipts = receipts.Count,
                closures = closures.Count,
                periodClosures = periodClosures.Count,
                journalEntries = journalEntries.Count
            },
            files = new Dictionary<string, string>
            {
                ["receipts.json"] = HashBytes(receiptsBytes),
                ["closures.json"] = HashBytes(closuresBytes),
                ["period-closures.json"] = HashBytes(periodClosuresBytes),
                ["journal.json"] = HashBytes(journalBytes)
            }
        };

        byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, jsonOptions);

        var typeCode = periodClosure.PeriodType == FiscalPeriodType.Monthly ? "M" : "A";
        // Le terminalId vient du client : on n'en garde que des caractères sûrs pour le nom de fichier.
        var safeTerminal = SafeFileNamePart().Replace(periodClosure.TerminalId, "_");
        var safeKey = SafeFileNamePart().Replace(periodClosure.PeriodKey, "_");
        var fileName = $"archive-{safeTerminal}-{typeCode}-{safeKey}.zip";

        if (!Directory.Exists(_archivesDirectory))
        {
            Directory.CreateDirectory(_archivesDirectory);
        }

        var targetFilePath = Path.Combine(_archivesDirectory, fileName);
        // Écriture dans un fichier temporaire : le fichier définitif n'apparaît qu'après le commit BD.
        var tempFilePath = Path.Combine(_archivesDirectory, $"{archiveId}.tmp");

        using (var fileStream = new FileStream(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);
            AddZipEntry(zip, "receipts.json", receiptsBytes);
            AddZipEntry(zip, "closures.json", closuresBytes);
            AddZipEntry(zip, "period-closures.json", periodClosuresBytes);
            AddZipEntry(zip, "journal.json", journalBytes);
            AddZipEntry(zip, "manifest.json", manifestBytes);
        }

        byte[] zipBytes = await File.ReadAllBytesAsync(tempFilePath, cancellationToken).ConfigureAwait(false);
        string fileSha256 = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
        long fileSizeBytes = zipBytes.Length;

        FiscalArchiveDto dto;
        try
        {
            dto = await _dbContext.ExecuteInTransactionAsync(async ct =>
            {
                var alreadyExistsInTx = await _dbContext.FiscalArchives
                    .AnyAsync(a => a.PeriodClosureId == periodClosureId, ct)
                    .ConfigureAwait(false);

                if (alreadyExistsInTx)
                {
                    throw new ArchiveExistsException();
                }

                var lastArchive = await _dbContext.FiscalArchives
                    .OrderByDescending(a => a.ArchiveSequence)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                long nextSequence = (lastArchive?.ArchiveSequence ?? 0) + 1;
                string prevHash = lastArchive?.SignatureHash ?? NF525FiscalAuditService.GenesisHash;
                string signatureHash = FiscalHashing.ComputeArchiveHash(
                    prevHash,
                    (int)periodClosure.PeriodType,
                    periodClosure.PeriodKey,
                    fileSha256,
                    now);

                var archive = new FiscalArchive
                {
                    Id = archiveId,
                    PeriodClosureId = periodClosure.Id,
                    PeriodType = periodClosure.PeriodType,
                    PeriodKey = periodClosure.PeriodKey,
                    FileName = fileName,
                    FileSha256 = fileSha256,
                    FileSizeBytes = fileSizeBytes,
                    ArchiveSequence = nextSequence,
                    PreviousSignatureHash = prevHash,
                    SignatureHash = signatureHash,
                    CreatedByUserId = userId,
                    CreatedAtUtc = now
                };

                _dbContext.FiscalArchives.Add(archive);
                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                await _fiscalJournal.AppendAsync(
                    JournalEventTypes.ArchiveCreated,
                    new
                    {
                        archiveId = archive.Id,
                        periodClosureId = archive.PeriodClosureId,
                        periodType = (int)archive.PeriodType,
                        periodKey = archive.PeriodKey,
                        fileName = archive.FileName,
                        fileSha256 = archive.FileSha256,
                        fileSizeBytes = archive.FileSizeBytes,
                        archiveSequence = archive.ArchiveSequence,
                        signatureHash = archive.SignatureHash
                    },
                    terminalId: periodClosure.TerminalId,
                    operatorId: userId,
                    cancellationToken: ct
                ).ConfigureAwait(false);

                return MapToDto(archive);
            }, IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            File.Delete(tempFilePath);
            throw;
        }

        File.Move(tempFilePath, targetFilePath, overwrite: true);
        return dto;
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]")]
    private static partial Regex SafeFileNamePart();

    public async Task<(Stream Stream, string FileName, string ContentType)?> GetArchiveFileAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default)
    {
        var archive = await _dbContext.FiscalArchives
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == archiveId, cancellationToken)
            .ConfigureAwait(false);

        if (archive == null)
        {
            return null;
        }

        var filePath = Path.GetFullPath(Path.Combine(_archivesDirectory, archive.FileName));
        if (!filePath.StartsWith(_archivesDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            await _fiscalJournal.AppendAsync(
                JournalEventTypes.ArchiveExported,
                new
                {
                    archiveId = archive.Id,
                    periodClosureId = archive.PeriodClosureId,
                    fileName = archive.FileName,
                    fileSha256 = archive.FileSha256
                },
                terminalId: null,
                operatorId: null,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);
        }
        catch
        {
            // Do not block download if JET entry logging fails
        }

        var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return (stream, archive.FileName, "application/zip");
    }

    public async Task<ArchiveVerificationResultDto> VerifyArchiveAsync(
        Stream zipStream,
        string? fileName = null,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await zipStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        byte[] bytes = ms.ToArray();

        if (bytes.Length == 0)
        {
            return new ArchiveVerificationResultDto(false, null, "unknown_archive");
        }

        string computedSha256 = Convert.ToHexStringLower(SHA256.HashData(bytes));

        Guid? manifestArchiveId = null;
        Guid? manifestPeriodClosureId = null;

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var manifestEntry = zip.GetEntry("manifest.json");
            if (manifestEntry != null)
            {
                using var entryStream = manifestEntry.Open();
                using var doc = JsonDocument.Parse(entryStream);
                if (doc.RootElement.TryGetProperty("archiveId", out var aProp) && aProp.TryGetGuid(out var aid))
                {
                    manifestArchiveId = aid;
                }
                if (doc.RootElement.TryGetProperty("periodClosureId", out var pProp) && pProp.TryGetGuid(out var pid))
                {
                    manifestPeriodClosureId = pid;
                }
            }
        }
        catch
        {
            // Invalid zip format or corrupted header
        }

        FiscalArchive? archive = null;
        if (manifestArchiveId.HasValue)
        {
            archive = await _dbContext.FiscalArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == manifestArchiveId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        if (archive == null && manifestPeriodClosureId.HasValue)
        {
            archive = await _dbContext.FiscalArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.PeriodClosureId == manifestPeriodClosureId.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        if (archive == null)
        {
            archive = await _dbContext.FiscalArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.FileSha256 == computedSha256, cancellationToken)
                .ConfigureAwait(false);
        }

        if (archive == null && !string.IsNullOrWhiteSpace(fileName))
        {
            archive = await _dbContext.FiscalArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.FileName == fileName, cancellationToken)
                .ConfigureAwait(false);
        }

        if (archive == null)
        {
            return new ArchiveVerificationResultDto(false, null, "unknown_archive");
        }

        if (!string.Equals(archive.FileSha256, computedSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveVerificationResultDto(false, archive.Id, "hash_mismatch");
        }

        string computedSignature = FiscalHashing.ComputeArchiveHash(
            archive.PreviousSignatureHash,
            (int)archive.PeriodType,
            archive.PeriodKey,
            archive.FileSha256,
            archive.CreatedAtUtc);

        if (!string.Equals(archive.SignatureHash, computedSignature, StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveVerificationResultDto(false, archive.Id, "chain_break");
        }

        if (archive.ArchiveSequence > 1)
        {
            var prevArchive = await _dbContext.FiscalArchives
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ArchiveSequence == archive.ArchiveSequence - 1, cancellationToken)
                .ConfigureAwait(false);

            if (prevArchive == null || !string.Equals(prevArchive.SignatureHash, archive.PreviousSignatureHash, StringComparison.OrdinalIgnoreCase))
            {
                return new ArchiveVerificationResultDto(false, archive.Id, "chain_break");
            }
        }
        else if (!string.Equals(archive.PreviousSignatureHash, NF525FiscalAuditService.GenesisHash, StringComparison.OrdinalIgnoreCase))
        {
            return new ArchiveVerificationResultDto(false, archive.Id, "chain_break");
        }

        return new ArchiveVerificationResultDto(true, archive.Id, null);
    }

    public async Task<IReadOnlyList<FiscalArchiveDto>> GetArchivesAsync(CancellationToken cancellationToken = default)
    {
        var list = await _dbContext.FiscalArchives
            .AsNoTracking()
            .OrderByDescending(a => a.ArchiveSequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.Select(MapToDto).ToList();
    }

    public async Task<FiscalArchiveDto?> GetArchiveByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var archive = await _dbContext.FiscalArchives
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);

        return archive == null ? null : MapToDto(archive);
    }

    private static void AddZipEntry(ZipArchive zip, string entryName, byte[] content)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content, 0, content.Length);
    }

    private static FiscalArchiveDto MapToDto(FiscalArchive a) => new(
        a.Id,
        a.PeriodClosureId,
        a.PeriodType,
        a.PeriodKey,
        a.FileName,
        a.FileSha256,
        a.FileSizeBytes,
        a.ArchiveSequence,
        a.PreviousSignatureHash,
        a.SignatureHash,
        a.CreatedByUserId,
        a.CreatedAtUtc);
}
