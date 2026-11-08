using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public sealed record AlertDto(Guid Id, string RuleType, Guid? OperatorId, string? OperatorName, string TerminalId, string PeriodKey, string DetailJson, string State,
    DateTimeOffset CreatedAtUtc, DateTimeOffset? ResolvedAtUtc, string? ResolutionNote);

public sealed record AlertRuleDto(string Type, long Threshold, bool Enabled);

public sealed record AlertSettings(int TrendThresholdPercent, bool CashVarianceNoteRequired);

/// <summary>Refus métier d'une alerte (<c>note_required</c>, <c>alert_rule_invalid</c>).</summary>
public sealed class AlertException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Alertes de pilotage : une alerte par (type, opérateur, période) grâce à la clé de dédoublonnage, clôturable avec une note.</summary>
public interface IAlertService
{
    /// <summary>Évalue les seuils actifs sur la période en cours de chaque poste ; renvoie le nombre d'alertes créées. Idempotent.</summary>
    Task<int> EvaluateAsync(CancellationToken ct = default);

    /// <summary>Alerte d'écart de caisse pour un comptage ; <c>true</c> si une alerte a été créée.</summary>
    Task<bool> EvaluateCashCountAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>Vrai (une fois) si des alertes ont changé depuis le dernier appel : sert à diffuser <c>OnAlertsChanged</c>.</summary>
    bool TakeChanged();

    Task<IReadOnlyList<AlertDto>> ListAsync(string? state, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default);
    Task<AlertDto?> ResolveAsync(Guid id, string note, Guid operatorId, CancellationToken ct = default);
    Task<IReadOnlyList<AlertRuleDto>> RulesAsync(CancellationToken ct = default);
    Task<AlertRuleDto?> SetRuleAsync(string type, long threshold, bool enabled, Guid? operatorId, CancellationToken ct = default);
    Task<AlertSettings> GetSettingsAsync(CancellationToken ct = default);
    Task<AlertSettings> UpdateSettingsAsync(AlertSettings settings, CancellationToken ct = default);
}
