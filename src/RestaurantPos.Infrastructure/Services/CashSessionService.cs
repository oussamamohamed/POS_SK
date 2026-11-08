using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

/// <summary>
/// Attendu = fond + espèces gardées (Σ <c>Amount</c> des règlements <c>Cash</c> du poste depuis l'ouverture : le montant imputé exclut déjà le rendu,
/// les avoirs sont négatifs) + entrées − sorties. Écart = compté − attendu. Miroir Swift : CashMath / LocalCashRepository.
/// </summary>
public sealed class CashSessionService(AppDbContext db, IAlertService? alerts = null, IFiscalJournal? journal = null) : ICashSessionService
{
    private readonly IFiscalJournal _journal = journal ?? new FiscalJournalService(db);

    public async Task<CashSessionDto> OpenAsync(string terminalId, Guid operatorId, long openingFloatCents, CancellationToken ct = default)
    {
        if (openingFloatCents < 0 || string.IsNullOrWhiteSpace(terminalId)) throw new CashException("cash_invalid");
        if (await CurrentRowAsync(terminalId, ct).ConfigureAwait(false) is not null) throw new CashException("cash_session_already_open");
        var session = new CashSession { TerminalId = terminalId, OpenedByOperatorId = operatorId, OpeningFloatCents = openingFloatCents };
        db.CashSessions.Add(session);
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateException) { throw new CashException("cash_session_already_open"); } // déclencheur SQL : deux ouvertures simultanées
        await _journal.AppendAsync(JournalEventTypes.CashSessionOpened, new { sessionId = session.Id, openingFloatCents }, terminalId, operatorId, ct).ConfigureAwait(false);
        return await ToDtoAsync(session, ct).ConfigureAwait(false);
    }

    private async Task<CashSession?> CurrentRowAsync(string terminalId, CancellationToken ct)
    {
        var sessions = await db.CashSessions.AsNoTracking().Where(s => s.TerminalId == terminalId).ToListAsync(ct).ConfigureAwait(false);
        var counted = (await db.CashCounts.AsNoTracking().Select(c => c.SessionId).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        return sessions.Where(s => !counted.Contains(s.Id)).OrderByDescending(s => s.OpenedAtUtc).FirstOrDefault();
    }

    public async Task<CashSessionDto?> CurrentAsync(string terminalId, CancellationToken ct = default)
    {
        var session = await CurrentRowAsync(terminalId, ct).ConfigureAwait(false);
        return session is null ? null : await ToDtoAsync(session, ct).ConfigureAwait(false);
    }

    public async Task<CashSessionDto?> GetAsync(Guid sessionId, CancellationToken ct = default)
    {
        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        return session is null ? null : await ToDtoAsync(session, ct).ConfigureAwait(false);
    }

    public async Task<CashMovementDto?> AddMovementAsync(Guid sessionId, string kind, long amountCents, string reason, Guid operatorId, CancellationToken ct = default)
    {
        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        if (session is null) return null;
        var text = (reason ?? "").Trim();
        if (amountCents <= 0 || (kind != CashMovementKinds.In && kind != CashMovementKinds.Out) || text.Length is < 3 or > 200) throw new CashException("movement_invalid");
        if (await db.CashCounts.AnyAsync(c => c.SessionId == sessionId, ct).ConfigureAwait(false)) throw new CashException("cash_session_closed");
        var movement = new CashMovement { SessionId = sessionId, Kind = kind, AmountCents = amountCents, Reason = text, OperatorId = operatorId };
        db.CashMovements.Add(movement);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _journal.AppendAsync(JournalEventTypes.CashMovement, new { sessionId, kind, amountCents, reason = text }, session.TerminalId, operatorId, ct).ConfigureAwait(false);
        return ToDto(movement);
    }

    public async Task<CashCountResult?> CountAsync(Guid sessionId, long countedCents, string? denominationsJson, string? varianceNote, Guid operatorId, CancellationToken ct = default)
    {
        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        if (session is null) return null;
        var existing = await db.CashCounts.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == sessionId, ct).ConfigureAwait(false);
        if (existing is not null) return new CashCountResult(existing.ExpectedCents, existing.CountedCents, existing.VarianceCents, false); // idempotent
        if (countedCents < 0) throw new CashException("cash_invalid");

        var now = DateTimeOffset.UtcNow;
        var expected = await ExpectedAsync(session, now, ct).ConfigureAwait(false);
        var variance = countedCents - expected;
        var note = string.IsNullOrWhiteSpace(varianceNote) ? null : varianceNote.Trim();
        var rule = await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(r => r.Type == AlertRuleTypes.CashVariance, ct).ConfigureAwait(false);
        var settings = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (rule is { Enabled: true } && (settings?.CashVarianceNoteRequired ?? true) && Math.Abs(variance) > rule.Threshold && note is null)
            throw new CashException("variance_note_required");

        db.CashCounts.Add(new CashCount { SessionId = sessionId, ClosedAtUtc = now, ClosedByOperatorId = operatorId, CountedCents = countedCents, ExpectedCents = expected, VarianceCents = variance, DenominationsJson = denominationsJson, VarianceNote = note });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        await _journal.AppendAsync(JournalEventTypes.CashCounted, new { sessionId, expectedCents = expected, countedCents, varianceCents = variance }, session.TerminalId, operatorId, ct).ConfigureAwait(false);

        // Après le COMMIT : une panne d'alerte ne remet jamais en cause le comptage.
        var created = false;
        if (alerts is not null)
        {
            try { created = await alerts.EvaluateCashCountAsync(sessionId, ct).ConfigureAwait(false); }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* journalisé par AlertService */ }
        }
        return new CashCountResult(expected, countedCents, variance, created);
    }

    public async Task<CashZSummary?> CashForPeriodAsync(string terminalId, DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        // Le poste principal couvre tous les postes (comme pour les ventes du rapport). Tous les comptages de la période comptent : deux services = deux comptages.
        var allTerminals = string.IsNullOrWhiteSpace(terminalId) || terminalId == "POS_MAIN_TERM";
        var sessions = (await db.CashSessions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).Where(s => allTerminals || s.TerminalId == terminalId).ToDictionary(s => s.Id);
        var counts = (await db.CashCounts.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .Where(c => sessions.ContainsKey(c.SessionId) && c.ClosedAtUtc >= fromUtc && c.ClosedAtUtc <= toUtc).ToList();
        return counts.Count == 0
            ? null
            : new CashZSummary(counts.Sum(c => sessions[c.SessionId].OpeningFloatCents), counts.Sum(c => c.CountedCents), counts.Sum(c => c.VarianceCents));
    }

    public async Task<IReadOnlyList<CashSessionDto>> ListAsync(DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        var sessions = (await db.CashSessions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .Where(s => (fromUtc is null || s.OpenedAtUtc >= fromUtc) && (toUtc is null || s.OpenedAtUtc < toUtc))
            .OrderByDescending(s => s.OpenedAtUtc).ToList();
        var result = new List<CashSessionDto>();
        foreach (var s in sessions) result.Add(await ToDtoAsync(s, ct).ConfigureAwait(false));
        return result;
    }

    /// <summary>Espèces attendues à l'instant <paramref name="until"/>.</summary>
    private async Task<long> ExpectedAsync(CashSession session, DateTimeOffset until, CancellationToken ct)
    {
        var receipts = (await db.FiscalReceipts.AsNoTracking().Include(r => r.Tenders).Where(r => r.TerminalId == session.TerminalId).ToListAsync(ct).ConfigureAwait(false))
            .Where(r => r.CreatedAtUtc >= session.OpenedAtUtc && r.CreatedAtUtc <= until);
        var cash = receipts.SelectMany(r => r.Tenders).Where(t => t.Method == PaymentMethod.Cash).Sum(t => t.Amount.AmountInCents);
        var movements = await db.CashMovements.AsNoTracking().Where(m => m.SessionId == session.Id).ToListAsync(ct).ConfigureAwait(false);
        var net = movements.Sum(m => m.Kind == CashMovementKinds.In ? m.AmountCents : -m.AmountCents);
        return session.OpeningFloatCents + cash + net;
    }

    private async Task<CashSessionDto> ToDtoAsync(CashSession s, CancellationToken ct)
    {
        var count = await db.CashCounts.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == s.Id, ct).ConfigureAwait(false);
        var movements = (await db.CashMovements.AsNoTracking().Where(m => m.SessionId == s.Id).ToListAsync(ct).ConfigureAwait(false)).OrderBy(m => m.CreatedAtUtc).Select(ToDto).ToList();
        var expected = count?.ExpectedCents ?? await ExpectedAsync(s, DateTimeOffset.UtcNow, ct).ConfigureAwait(false);
        return new CashSessionDto(s.Id, s.TerminalId, s.OpenedAtUtc, s.OpenedByOperatorId, s.OpeningFloatCents, expected, movements,
            count is null ? null : new CashCountDto(count.ExpectedCents, count.CountedCents, count.VarianceCents, count.VarianceNote, count.DenominationsJson, count.ClosedAtUtc));
    }

    private static CashMovementDto ToDto(CashMovement m) => new(m.Id, m.Kind, m.AmountCents, m.Reason, m.OperatorId, m.CreatedAtUtc);
}
