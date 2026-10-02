using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public record ChainBreakDto(
    long SequenceNumber,
    string Reference,
    string Kind);

public record ChainStatusDto(
    string Chain,
    string? TerminalId,
    int CheckedCount,
    bool IsValid,
    ChainBreakDto? Break,
    int? LegacyCount = null);

public record FiscalVerificationResultDto(
    bool IsValid,
    DateTimeOffset CheckedAtUtc,
    IReadOnlyList<ChainStatusDto> Chains);

public interface IFiscalChainVerificationService
{
    Task<FiscalVerificationResultDto> VerifyAllChainsAsync(CancellationToken cancellationToken = default);
}
