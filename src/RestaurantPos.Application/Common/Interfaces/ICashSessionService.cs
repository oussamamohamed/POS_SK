using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public sealed record CashMovementDto(Guid Id, string Kind, long AmountCents, string Reason, Guid OperatorId, DateTimeOffset CreatedAtUtc);

public sealed record CashCountDto(long ExpectedCents, long CountedCents, long VarianceCents, string? VarianceNote, string? DenominationsJson, DateTimeOffset ClosedAtUtc);

/// <summary><c>ExpectedCents</c> : en direct tant que la session est ouverte, figé par le comptage ensuite.</summary>
public sealed record CashSessionDto(Guid Id, string TerminalId, DateTimeOffset OpenedAtUtc, Guid OpenedByOperatorId, long OpeningFloatCents, long ExpectedCents,
    IReadOnlyList<CashMovementDto> Movements, CashCountDto? Count);

public sealed record CashZSummary(long FloatCents, long CountedCents, long VarianceCents);

public sealed record CashCountResult(long ExpectedCents, long CountedCents, long VarianceCents, bool AlertCreated);

/// <summary>Refus métier de la caisse (<c>cash_session_already_open</c>, <c>cash_session_closed</c>, <c>movement_invalid</c>, <c>variance_note_required</c>, <c>cash_invalid</c>).</summary>
public sealed class CashException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Sessions de caisse d'un poste : fond, sorties et entrées, comptage de clôture. Aucune écriture dans les reçus ni dans les chaînes fiscales.</summary>
public interface ICashSessionService
{
    Task<CashSessionDto> OpenAsync(string terminalId, Guid operatorId, long openingFloatCents, CancellationToken ct = default);
    Task<CashSessionDto?> CurrentAsync(string terminalId, CancellationToken ct = default);
    Task<CashSessionDto?> GetAsync(Guid sessionId, CancellationToken ct = default);
    Task<CashMovementDto?> AddMovementAsync(Guid sessionId, string kind, long amountCents, string reason, Guid operatorId, CancellationToken ct = default);
    /// <summary>Compte la session (idempotent : rejouer renvoie le comptage déjà figé). <c>null</c> si la session est inconnue.</summary>
    Task<CashCountResult?> CountAsync(Guid sessionId, long countedCents, string? denominationsJson, string? varianceNote, Guid operatorId, CancellationToken ct = default);
    /// <summary>Dernier comptage du poste clos dans [fromUtc, toUtc] (période d'un Z) ; <c>null</c> s'il n'y en a pas. Lecture seule, hors de tout contenu signé.</summary>
    Task<CashZSummary?> CashForPeriodAsync(string terminalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);

    Task<IReadOnlyList<CashSessionDto>> ListAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default);
}
