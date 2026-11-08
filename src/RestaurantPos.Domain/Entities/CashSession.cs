using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

/// <summary>Session de caisse d'un poste, en ajout seulement (déclencheurs SQL) : ouverture avec un fond, clôturée par un <see cref="CashCount"/>.</summary>
public class CashSession
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string TerminalId { get; set; }
    public DateTimeOffset OpenedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid OpenedByOperatorId { get; set; }
    public long OpeningFloatCents { get; set; }
}

public static class CashMovementKinds
{
    public const string In = "in";
    public const string Out = "out";
}

/// <summary>Entrée ou sortie d'espèces avec son motif, en ajout seulement.</summary>
public class CashMovement
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid SessionId { get; set; }
    public required string Kind { get; set; }
    public long AmountCents { get; set; }
    public required string Reason { get; set; }
    public Guid OperatorId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Comptage de clôture (un seul par session) : l'attendu et l'écart sont figés ici. En ajout seulement.</summary>
public class CashCount
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid SessionId { get; set; }
    public DateTimeOffset ClosedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public Guid ClosedByOperatorId { get; set; }
    public long CountedCents { get; set; }
    public long ExpectedCents { get; set; }
    public long VarianceCents { get; set; }
    /// <summary>Liste facultative <c>[{"valueCents":2000,"quantity":5}]</c>.</summary>
    public string? DenominationsJson { get; set; }
    public string? VarianceNote { get; set; }
}
