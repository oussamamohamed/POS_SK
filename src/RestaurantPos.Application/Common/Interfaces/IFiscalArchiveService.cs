using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public class ArchiveExistsException : Exception
{
    public string Code { get; } = "archive_exists";

    public ArchiveExistsException(string message = "An archive already exists for this period closure.")
        : base(message)
    {
    }
}

public record FiscalArchiveDto(
    Guid Id,
    Guid PeriodClosureId,
    FiscalPeriodType PeriodType,
    string PeriodKey,
    string FileName,
    string FileSha256,
    long FileSizeBytes,
    long ArchiveSequence,
    string PreviousSignatureHash,
    string SignatureHash,
    Guid CreatedByUserId,
    DateTimeOffset CreatedAtUtc);

public record ArchiveVerificationResultDto(
    bool IsValid,
    Guid? ArchiveId,
    string? Reason);

public interface IFiscalArchiveService
{
    Task<FiscalArchiveDto> CreateArchiveAsync(
        Guid periodClosureId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<(Stream Stream, string FileName, string ContentType)?> GetArchiveFileAsync(
        Guid archiveId,
        CancellationToken cancellationToken = default);

    Task<ArchiveVerificationResultDto> VerifyArchiveAsync(
        Stream zipStream,
        string? fileName = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FiscalArchiveDto>> GetArchivesAsync(
        CancellationToken cancellationToken = default);

    Task<FiscalArchiveDto?> GetArchiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
