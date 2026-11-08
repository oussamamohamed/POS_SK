using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

/// <summary>
/// Une tendance à surveiller. <c>Metric</c> : <c>cumulativeTtc</c>, <c>avgTicket</c>, <c>perCover</c> (centimes) ou <c>takeawayShare</c> (centièmes de pour cent).
/// <c>DeltaPercent</c> = (courant − référence) / référence, arrondi à 0,1 (demi éloigné de zéro).
/// </summary>
public sealed record InsightCardDto(string Metric, long CurrentCents, long ReferenceCents, decimal DeltaPercent);

public sealed record InsightsDto(IReadOnlyList<InsightCardDto> Cards, bool InsufficientData);

public sealed record ServerTipSummary(string ServerName, long TipsCents);

public sealed record DailySummaryCash(long FloatCents, long CountedCents, long VarianceCents);

/// <summary>Résumé de fin de journée : totaux du dernier Z du poste (donc identiques), pourboires par serveur, comptage de caisse et alertes ouvertes de la période.</summary>
public sealed record DailySummaryDto(string TerminalId, long ClosureSequence, DateTimeOffset ClosedAtUtc, long TotalSalesTtcCents, long TotalSalesHtCents, int ReceiptCount, int Covers,
    long AverageTicketCents, IReadOnlyDictionary<string, long> PaymentTotalsCents, IReadOnlyList<ServerTipSummary> TipsByServer, DailySummaryCash? Cash, IReadOnlyList<AlertDto> OpenAlerts);

public interface IInsightsService
{
    /// <summary>Tendances du jour local (<paramref name="tzOffsetMinutes"/>) comparées au même jour de la semaine précédente à la même heure. Aucune carte sous 3 jours de données.</summary>
    Task<InsightsDto> GetInsightsAsync(int tzOffsetMinutes, DateTimeOffset? nowUtc = null, CancellationToken ct = default);

    /// <summary>Résumé du dernier Z du poste (ou du Z de la date locale donnée) ; <c>null</c> s'il n'y en a pas.</summary>
    Task<DailySummaryDto?> GetDailySummaryAsync(string terminalId, int tzOffsetMinutes, DateOnly? localDay = null, CancellationToken ct = default);

    /// <summary>Delta en pourcentage, miroir Swift : <c>InsightsMath.deltaPercent</c>.</summary>
    static decimal DeltaPercent(long current, long reference) => reference == 0 ? 0m : Math.Round((current - reference) * 100m / reference, 1, MidpointRounding.AwayFromZero);
}
