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
/// annulation par PIN responsable), <c>x_report</c>, <c>z_closure</c>, <c>void_receipt</c>. L'annulation et la réimpression d'un reçu sont
/// portées par la commande (<c>VoidPaymentIndex</c>, <c>Reprint</c>). Le pourboire (<c>TipCents</c>) est encaissé avec le dernier règlement.
/// <c>Ref</c> nomme une commande ; <c>void_receipt</c> (<c>Ref</c> + <c>VoidPaymentIndex</c>) tente d'annuler après coup le reçu d'une
/// commande : placée après le <c>z_closure</c> du poste, elle doit être REFUSÉE (409 <c>void_after_closure</c>) et n'a aucun effet fiscal,
/// donc aucun effet sur le grand livre. Les champs inutiles pour un type sont nuls.
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
    bool HoldAndRecall = false,
    string? Ref = null);

/// <summary>Grand livre attendu d'un poste (ou de la salle) tel que publié dans le JSON partagé ; clés de TVA = taux invariant.</summary>
public sealed record DayExpected(
    long TtcCents,
    long HtCents,
    int ReceiptCount,
    IReadOnlyDictionary<string, long> VatByRate,
    IReadOnlyDictionary<string, long> TenderByMethod);

/// <summary>Contenu de <c>day_scenario.json</c> : le scénario et son grand livre (clés de <c>Expected</c> = clés des postes du scénario).</summary>
public sealed record DayScenarioFile(
    int Seed,
    IReadOnlyList<DayTerminal> Terminals,
    IReadOnlyList<DayOperation> Operations,
    IReadOnlyDictionary<string, DayExpected> Expected,
    DayExpected Total);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DayScenario))]
[JsonSerializable(typeof(DayScenarioFile))]
internal sealed partial class DayScenarioJsonContext : JsonSerializerContext;
