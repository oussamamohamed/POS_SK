using System.Globalization;
using System.Text.Json;
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
/// donc aucun effet sur le grand livre. <c>LineVoids</c> : annulations (ligne envoyée) et corrections (ligne non envoyée) de lignes de la
/// commande, jouées à leur phase (<see cref="DayLineVoid"/>). Les champs inutiles pour un type sont nuls.
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
    string? Ref = null,
    IReadOnlyList<DayLineVoid>? LineVoids = null,
    int? TipPaymentIndex = null,
    IReadOnlyList<DayReceiptVoid>? ReceiptVoids = null);

/// <summary>
/// Annulation (ou tentative d'annulation) d'un reçu fiscal (<c>POST /api/checkout/void/{id}</c>) pendant ou après les règlements d'une
/// commande. <c>AfterPayment</c> = index du règlement après lequel l'appel est joué (<c>null</c> = après tous les règlements) ;
/// <c>Stage</c> = 0 (avant <c>part_voided_still_has_receipt</c>) ou 1 (après, avant <c>all_voided_reopened</c>) ;
/// <c>VoidSelf</c> = tente d'annuler le reçu <c>-VOID-</c> issu de <c>PaymentIndex</c> ; <c>FromTerminal</c> = autre poste (refus inter-poste, .NET seul).
/// </summary>
public sealed record DayReceiptVoid(
    int? AfterPayment,
    int PaymentIndex,
    string By = "manager",
    int Expect = 200,
    string? Code = null,
    int Stage = 0,
    bool VoidSelf = false,
    string? FromTerminal = null);

/// <summary>
/// Annulation ou correction d'une ligne (<c>POST /api/orders/{id}/items/{ligne}/void</c>). <c>Phase</c> (dans cet ordre) :
/// <c>draft</c> (lignes enregistrées, avant l'envoi en cuisine : correction), <c>sent</c> (après l'envoi, avant tout règlement),
/// <c>reopened</c> (après le premier règlement puis l'annulation de son reçu : la commande redevient modifiable), <c>part_paid</c> (après le
/// premier règlement), <c>part_paid_two</c> (après le deuxième règlement), <c>part_voided_still_has_receipt</c> (après annulation du premier
/// reçu alors que le deuxième reste actif), <c>all_voided_reopened</c> (après annulation de tous les reçus), <c>paid</c> (après le dernier),
/// <c>cancelled</c> (après annulation complète de la commande). <c>Line</c> = index dans <c>Lines</c>, puis dans les lignes rajoutées
/// (<c>ReAdd</c> ou <c>AddDraft</c>), dans l'ordre. <c>By</c> = <c>manager</c>, <c>admin</c>, <c>waiter</c> ou <c>kitchen</c>.
/// </summary>
public sealed record DayLineVoid(
    string Phase,
    int Line,
    int Quantity,
    string? Reason = "Erreur de saisie",
    string By = "manager",
    int Expect = 200,
    string? Code = null,
    int ReAdd = 0,
    bool Legacy = false,
    bool UnknownLine = false,
    bool UnknownOrder = false,
    DayLine? AddDraft = null);

/// <summary>Annulation acceptée d'une ligne envoyée, telle que la ligne <c>OrderLineVoids</c> et le JET <c>ORDER_LINE_VOIDED</c> doivent la porter.</summary>
public sealed record DayExpectedLineVoid(
    string Terminal,
    string Ref,
    string Table,
    string ProductName,
    int Quantity,
    long UnitPriceCents,
    string Reason,
    string ServerName,
    bool HasTicket,
    string By = "manager");

/// <summary>Grand livre attendu d'un poste (ou de la salle) tel que publié dans le JSON partagé ; clés de TVA = taux invariant.</summary>
public sealed record DayExpected(
    long TtcCents,
    long HtCents,
    int ReceiptCount,
    IReadOnlyDictionary<string, long> VatByRate,
    IReadOnlyDictionary<string, long> TenderByMethod);

/// <summary>Article de bon attendu d'une ligne envoyée (index de ligne comme <see cref="DayLineVoid.Line"/>) : quantité envoyée et quantité annulée.</summary>
public sealed record DayExpectedTicketLine(int Line, int Sent, int Cancelled);

/// <summary>
/// Contenu de <c>day_scenario.json</c> : le scénario, son grand livre (clés de <c>Expected</c> = clés des postes du scénario), les annulations
/// acceptées, le total TTC attendu après chaque étape (nul pour un refus) et les articles de bon attendus, par commande (<c>Ref</c>).
/// </summary>
public sealed record DayScenarioFile(
    int Seed,
    IReadOnlyList<DayTerminal> Terminals,
    IReadOnlyList<DayOperation> Operations,
    IReadOnlyDictionary<string, DayExpected> Expected,
    DayExpected Total,
    IReadOnlyList<DayExpectedLineVoid> ExpectedLineVoids,
    IReadOnlyDictionary<string, IReadOnlyList<long?>> ExpectedLineVoidTotals,
    IReadOnlyDictionary<string, IReadOnlyList<DayExpectedTicketLine>> ExpectedTicketLines);

/// <summary>Décimaux écrits en chaînes (<c>"5.5"</c>) comme dans <c>fiscal_reference.json</c> : le Swift les lit en <c>Decimal</c> sans passer par un flottant.</summary>
internal sealed class DecimalAsStringConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? decimal.Parse(reader.GetString()!, CultureInfo.InvariantCulture) : reader.GetDecimal();

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = [typeof(DecimalAsStringConverter)])]
[JsonSerializable(typeof(DayScenario))]
[JsonSerializable(typeof(DayScenarioFile))]
internal sealed partial class DayScenarioJsonContext : JsonSerializerContext;
