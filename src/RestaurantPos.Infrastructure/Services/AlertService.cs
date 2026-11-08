using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

/// <summary>
/// Alertes de pilotage. Un « service » est la période entre deux Z du poste : <c>PeriodKey = terminalId:numéroDuDernierZ</c>. Une alerte est unique
/// par (type, opérateur, période) (<c>DedupeKey</c>, index unique) : relancer l'évaluation ne crée jamais de doublon. Un seuil est franchi quand la
/// valeur le <b>dépasse</b> (strictement). Miroir Swift : AlertRules / LocalAlertRepository.
/// </summary>
public sealed class AlertService(AppDbContext db, IFiscalJournal? journal = null) : IAlertService
{
    private readonly IFiscalJournal _journal = journal ?? new FiscalJournalService(db);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private bool _changed;

    public bool TakeChanged() => _changed ? !(_changed = false) : false;

    private async Task<List<AlertRule>> EnsureRulesAsync(CancellationToken ct)
    {
        var rules = await db.AlertRules.ToListAsync(ct).ConfigureAwait(false);
        var defaults = new Dictionary<string, long> { [AlertRuleTypes.CashVariance] = 1000, [AlertRuleTypes.VoidsCount] = 3, [AlertRuleTypes.DiscountPercent] = 30, [AlertRuleTypes.CompsTotal] = 5000 };
        foreach (var (type, threshold) in defaults.Where(d => rules.All(r => r.Type != d.Key)))
        {
            var rule = new AlertRule { Type = type, Threshold = threshold, Enabled = false };
            db.AlertRules.Add(rule);
            rules.Add(rule);
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rules;
    }

    public async Task<IReadOnlyList<AlertRuleDto>> RulesAsync(CancellationToken ct = default) =>
        (await EnsureRulesAsync(ct).ConfigureAwait(false)).OrderBy(r => Array.IndexOf(AlertRuleTypes.All, r.Type)).Select(r => new AlertRuleDto(r.Type, r.Threshold, r.Enabled)).ToList();

    public async Task<AlertRuleDto?> SetRuleAsync(string type, long threshold, bool enabled, Guid? operatorId, CancellationToken ct = default)
    {
        if (!AlertRuleTypes.All.Contains(type)) return null;
        if (threshold < 0) throw new AlertException("alert_rule_invalid");
        var rule = (await EnsureRulesAsync(ct).ConfigureAwait(false)).First(r => r.Type == type);
        rule.Threshold = threshold;
        rule.Enabled = enabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rule.UpdatedBy = operatorId;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new AlertRuleDto(rule.Type, rule.Threshold, rule.Enabled);
    }

    public async Task<AlertSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new AlertSettings(s?.TrendThresholdPercent ?? 15, s?.CashVarianceNoteRequired ?? true);
    }

    public async Task<AlertSettings> UpdateSettingsAsync(AlertSettings settings, CancellationToken ct = default)
    {
        if (settings.TrendThresholdPercent is < 1 or > 100) throw new AlertException("alert_rule_invalid");
        var row = await db.RestaurantSettings.FindAsync([RestaurantSettings.SingletonId], ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new RestaurantSettings { ReceiptLanguage = "fr", KitchenTicketLanguage = "fr" };
            db.RestaurantSettings.Add(row);
        }
        row.TrendThresholdPercent = settings.TrendThresholdPercent;
        row.CashVarianceNoteRequired = settings.CashVarianceNoteRequired;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetSettingsAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Période en cours d'un poste : clé et début (fin du dernier Z, ou le début des temps).</summary>
    private async Task<(string Key, DateTimeOffset Start)> PeriodAsync(string terminalId, CancellationToken ct)
    {
        var closures = await db.DailyFiscalClosures.AsNoTracking().Where(c => c.TerminalId == terminalId).ToListAsync(ct).ConfigureAwait(false);
        var last = closures.OrderByDescending(c => c.ClosureSequence).FirstOrDefault();
        return ($"{terminalId}:{last?.ClosureSequence ?? 0}", last?.PeriodEndUtc ?? DateTimeOffset.MinValue);
    }

    public async Task<int> EvaluateAsync(CancellationToken ct = default)
    {
        var rules = (await EnsureRulesAsync(ct).ConfigureAwait(false)).Where(r => r.Enabled).ToDictionary(r => r.Type);
        var enabledOrderRules = rules.Keys.Where(k => k != AlertRuleTypes.CashVariance).ToList();
        if (enabledOrderRules.Count == 0) return 0;

        // Reçus : six colonnes seulement. Les commandes (avec leurs lignes) ne sont chargées que pour la période en cours de chaque poste,
        // jamais pour tout l'historique (SQLite ne compare pas un DateTimeOffset en SQL : le filtre de période se fait en mémoire).
        var receipts = await db.FiscalReceipts.AsNoTracking().Select(r => new { r.Id, r.TerminalId, r.OrderId, r.VoidedReceiptId, r.TotalTtcAmount, r.CreatedAtUtc }).ToListAsync(ct).ConfigureAwait(false);
        var periods = new List<(string Terminal, string Key, HashSet<Guid> Ids)>();
        foreach (var terminal in receipts.Select(r => r.TerminalId).Distinct())
        {
            var (key, start) = await PeriodAsync(terminal, ct).ConfigureAwait(false);
            periods.Add((terminal, key, receipts.Where(r => r.TerminalId == terminal && r.CreatedAtUtc >= start).Select(r => r.Id).ToHashSet()));
        }
        var inAnyPeriod = periods.SelectMany(p => p.Ids).ToHashSet();
        var orderIds = receipts.Where(r => inAnyPeriod.Contains(r.Id)).Select(r => r.OrderId).Distinct().ToList();
        var orders = (await db.Orders.AsNoTracking().Include(o => o.Items).Where(o => orderIds.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(o => o.Id);
        var created = 0;

        foreach (var (terminal, periodKey, ids) in periods)
        {
            var inPeriod = receipts.Where(r => ids.Contains(r.Id)).ToList();

            if (rules.TryGetValue(AlertRuleTypes.VoidsCount, out var voids))
            {
                foreach (var g in inPeriod.Where(r => r.VoidedReceiptId != null && orders.ContainsKey(r.OrderId)).GroupBy(r => orders[r.OrderId].OperatorId).Where(g => g.Count() > voids.Threshold))
                    created += await RaiseAsync(AlertRuleTypes.VoidsCount, g.Key, terminal, periodKey, new { count = g.Count(), totalCents = g.Sum(r => Math.Abs(r.TotalTtcAmount.AmountInCents)), receiptIds = g.Select(r => r.Id) }, ct).ConfigureAwait(false);
            }

            var sold = inPeriod.Where(r => r.VoidedReceiptId == null && orders.ContainsKey(r.OrderId)).Select(r => orders[r.OrderId]).DistinctBy(o => o.Id).ToList();
            if (rules.TryGetValue(AlertRuleTypes.DiscountPercent, out var discount))
            {
                foreach (var g in sold.Select(o => (Order: o, Percent: DiscountPercentOf(o))).Where(x => x.Percent > discount.Threshold).GroupBy(x => x.Order.OperatorId))
                    created += await RaiseAsync(AlertRuleTypes.DiscountPercent, g.Key, terminal, periodKey, new { count = g.Count(), maxPercent = g.Max(x => x.Percent), orderIds = g.Select(x => x.Order.Id) }, ct).ConfigureAwait(false);
            }
            if (rules.TryGetValue(AlertRuleTypes.CompsTotal, out var comps))
            {
                foreach (var g in sold.Select(o => (Order: o, Cents: CompCentsOf(o))).Where(x => x.Cents > 0).GroupBy(x => x.Order.OperatorId).Where(g => g.Sum(x => x.Cents) > comps.Threshold))
                    created += await RaiseAsync(AlertRuleTypes.CompsTotal, g.Key, terminal, periodKey, new { totalCents = g.Sum(x => x.Cents), orderIds = g.Select(x => x.Order.Id) }, ct).ConfigureAwait(false);
            }
        }
        return created;
    }

    /// <summary>
    /// Remise de commande en % : le plus fort entre la remise de la note (un montant fixe est ramené en pourcentage ; offerte = 100 %)
    /// et la plus forte remise de ligne (une ligne offerte n'est pas une remise : elle va dans les « offerts »).
    /// </summary>
    internal static int DiscountPercentOf(Order o)
    {
        var line = o.GlobalDiscountType == DiscountType.Comp ? 0 : (int)Math.Round(o.Items.Where(i => !i.IsComp).Select(i => i.DiscountPercent).DefaultIfEmpty(0m).Max(), MidpointRounding.AwayFromZero);
        var subtotal = o.Subtotal.AmountInCents;
        if (subtotal <= 0 || o.GlobalDiscountType is null) return line;
        var global = o.GlobalDiscountType switch
        {
            DiscountType.Percentage => (int)Math.Round(o.GlobalDiscountValue, MidpointRounding.AwayFromZero),
            DiscountType.Comp => 100,
            DiscountType.FixedAmount => (int)Math.Min(100, Math.Round(o.GlobalDiscountValue * 100m * 100m / subtotal, MidpointRounding.AwayFromZero)),
            _ => 0,
        };
        return Math.Max(global, line);
    }

    /// <summary>Valeur (centimes) des offerts d'une commande : lignes offertes, ou toute la commande si elle est offerte.</summary>
    internal static long CompCentsOf(Order o)
    {
        long Line(OrderItem i) => (i.UnitPrice.AmountInCents + i.ModifiersPriceExtra.AmountInCents) * i.Quantity;
        if (o.GlobalDiscountType == DiscountType.Comp) return o.Items.Sum(Line);
        return o.Items.Where(i => i.IsComp).Sum(Line);
    }

    public async Task<bool> EvaluateCashCountAsync(Guid sessionId, CancellationToken ct = default)
    {
        var rule = (await EnsureRulesAsync(ct).ConfigureAwait(false)).First(r => r.Type == AlertRuleTypes.CashVariance);
        if (!rule.Enabled) return false;
        var session = await db.CashSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct).ConfigureAwait(false);
        var count = await db.CashCounts.AsNoTracking().FirstOrDefaultAsync(c => c.SessionId == sessionId, ct).ConfigureAwait(false);
        if (session is null || count is null || Math.Abs(count.VarianceCents) <= rule.Threshold) return false;
        var (periodKey, _) = await PeriodAsync(session.TerminalId, ct).ConfigureAwait(false);
        return await RaiseAsync(AlertRuleTypes.CashVariance, session.OpenedByOperatorId, session.TerminalId, periodKey,
            new { sessionId, varianceCents = count.VarianceCents, expectedCents = count.ExpectedCents, countedCents = count.CountedCents }, ct).ConfigureAwait(false) > 0;
    }

    /// <summary>Crée l'alerte si sa clé de dédoublonnage est libre ; 1 si créée, 0 sinon.</summary>
    private async Task<int> RaiseAsync(string ruleType, Guid? operatorId, string terminalId, string periodKey, object detail, CancellationToken ct)
    {
        var dedupe = $"{ruleType}|{operatorId}|{periodKey}";
        var detailJson = JsonSerializer.Serialize(detail, Json);
        var existing = await db.Alerts.FirstOrDefaultAsync(a => a.DedupeKey == dedupe, ct).ConfigureAwait(false);
        if (existing is not null) return await RefreshOpenAsync(existing, detailJson, ct).ConfigureAwait(false);

        var alert = new Alert { RuleType = ruleType, OperatorId = operatorId, TerminalId = terminalId, PeriodKey = periodKey, DedupeKey = dedupe, DetailJson = detailJson };
        db.Alerts.Add(alert);
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateException)
        {
            db.Entry(alert).State = EntityState.Detached;
            // Course avec un autre appel : seule une alerte déjà présente est une issue normale ; toute autre panne remonte.
            existing = await db.Alerts.AsNoTracking().FirstOrDefaultAsync(a => a.DedupeKey == dedupe, ct).ConfigureAwait(false);
            if (existing is null) throw;
            return 0;
        }
        _changed = true;
        try { await _journal.AppendAsync(JournalEventTypes.AlertCreated, new { alertId = alert.Id, ruleType, periodKey }, terminalId, operatorId, ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is not OperationCanceledException) { /* l'alerte est créée : un journal indisponible ne la défait pas */ }
        return 1;
    }

    /// <summary>Une alerte ouverte reflète l'état courant (4 annulations puis 12 : « 12 ») ; une alerte résolue reste figée. Ne compte pas comme une création.</summary>
    private async Task<int> RefreshOpenAsync(Alert existing, string detailJson, CancellationToken ct)
    {
        if (existing.State != Alert.Open || existing.DetailJson == detailJson) return 0;
        existing.DetailJson = detailJson;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        _changed = true;
        return 0;
    }

    public async Task<IReadOnlyList<AlertDto>> ListAsync(string? state, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        var rows = (await db.Alerts.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .Where(a => (state is null || a.State == state) && (fromUtc is null || a.CreatedAtUtc >= fromUtc) && (toUtc is null || a.CreatedAtUtc < toUtc))
            .OrderByDescending(a => a.CreatedAtUtc).ToList();
        var ids = rows.Where(a => a.OperatorId != null).Select(a => a.OperatorId!.Value).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);
        return rows.Select(a => ToDto(a, a.OperatorId is { } id ? names.GetValueOrDefault(id) : null)).ToList();
    }

    public async Task<AlertDto?> ResolveAsync(Guid id, string note, Guid operatorId, CancellationToken ct = default)
    {
        var alert = await db.Alerts.FirstOrDefaultAsync(a => a.Id == id, ct).ConfigureAwait(false);
        if (alert is null) return null;
        if (alert.State == Alert.Open)
        {
            var text = (note ?? "").Trim();
            if (text.Length == 0) throw new AlertException("note_required");
            alert.State = Alert.Resolved;
            alert.ResolvedAtUtc = DateTimeOffset.UtcNow;
            alert.ResolvedBy = operatorId;
            alert.ResolutionNote = text;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            _changed = true;
            await _journal.AppendAsync(JournalEventTypes.AlertResolved, new { alertId = id }, alert.TerminalId, operatorId, ct).ConfigureAwait(false);
        }
        var name = alert.OperatorId is { } op ? (await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == op, ct).ConfigureAwait(false))?.Name : null;
        return ToDto(alert, name);
    }

    private static AlertDto ToDto(Alert a, string? operatorName) =>
        new(a.Id, a.RuleType, a.OperatorId, operatorName, a.TerminalId, a.PeriodKey, a.DetailJson, a.State, a.CreatedAtUtc, a.ResolvedAtUtc, a.ResolutionNote);
}
