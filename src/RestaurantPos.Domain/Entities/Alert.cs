using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public static class AlertRuleTypes
{
    public const string CashVariance = "cash_variance";
    public const string VoidsCount = "voids_count";
    public const string DiscountPercent = "discount_percent";
    public const string CompsTotal = "comps_total";

    public static readonly string[] All = [CashVariance, VoidsCount, DiscountPercent, CompsTotal];
}

/// <summary>Seuil d'une alerte : centimes (écart, offerts), nombre (annulations) ou pourcentage (remise). Désactivé par défaut.</summary>
public class AlertRule
{
    public required string Type { get; set; }
    public long Threshold { get; set; }
    public bool Enabled { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid? UpdatedBy { get; set; }
}

/// <summary>Alerte de pilotage : unique par <c>DedupeKey</c> (type, opérateur, période), clôturable avec une note.</summary>
public class Alert
{
    public const string Open = "open";
    public const string Resolved = "resolved";

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string RuleType { get; set; }
    public Guid? OperatorId { get; set; }
    public required string TerminalId { get; set; }
    /// <summary><c>terminalId:numéroDuDernierZ</c> : un « service » est la période entre deux Z du poste.</summary>
    public required string PeriodKey { get; set; }
    public required string DedupeKey { get; set; }
    public string DetailJson { get; set; } = "{}";
    public string State { get; set; } = Open;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public Guid? ResolvedBy { get; set; }
    public string? ResolutionNote { get; set; }
}
