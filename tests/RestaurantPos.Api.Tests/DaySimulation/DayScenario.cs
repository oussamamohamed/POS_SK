using System.Text.Json.Serialization;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Une journée décrite comme des données : rejouable par le .NET (API réelle) et, plus tard, par l'iPad (Swift).</summary>
public sealed record DayScenario(int Seed, IReadOnlyList<DayTerminal> Terminals, IReadOnlyList<DayOperation> Operations);

/// <summary>Un poste de caisse appairé ; ses reçus portent l'identifiant de terminal attribué par l'appairage (T01, T02…).</summary>
public sealed record DayTerminal(string Key, string Name);

public sealed record DayLine(
    string Name,
    int Quantity,
    long UnitPriceCents,
    decimal TaxRatePercent,
    decimal? TaxRateTakeawayPercent = null,
    long ModifiersExtraCents = 0,
    bool Comp = false);

/// <summary>
/// Un moyen de paiement : <c>Method</c> = nom de <c>PaymentMethod</c> (Cash, CreditCard, MealVoucher…), <c>AmountCents</c> = montant imputé
/// (pourboire compris sur le dernier règlement), <c>ExtraTenderedCents</c> = espèces remises en plus (rendu monnaie, hors comptes).
/// </summary>
public sealed record DayTender(string Method, long AmountCents, long ExtraTenderedCents = 0);

/// <summary>Remise de commande : <c>Type</c> = Percentage (Value en %) ou FixedAmount (Value en euros).</summary>
public sealed record DayOrderDiscount(string Type, decimal Value);

/// <summary>
/// Opérations : <c>table_order</c> (sur place), <c>counter_order</c> (comptoir / à emporter), <c>hold_then_void</c> (mise en attente puis
/// annulation par PIN responsable), <c>x_report</c>, <c>z_closure</c>. L'annulation et la réimpression d'un reçu sont portées par la
/// commande (<c>VoidPaymentIndex</c>, <c>Reprint</c>). Le pourboire (<c>TipCents</c>) est encaissé avec le dernier règlement.
/// Les champs inutiles pour un type sont nuls.
/// </summary>
public sealed record DayOperation(
    string Type,
    string? Terminal = null,
    string? Waiter = null,
    string? Table = null,
    int Covers = 0,
    string? Destination = null,
    IReadOnlyList<DayLine>? Lines = null,
    DayOrderDiscount? Discount = null,
    long TipCents = 0,
    IReadOnlyList<IReadOnlyList<DayTender>>? Payments = null,
    int? VoidPaymentIndex = null,
    bool Reprint = false,
    bool HoldAndRecall = false);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(DayScenario))]
internal sealed partial class DayScenarioJsonContext : JsonSerializerContext;
