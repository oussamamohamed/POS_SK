using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IFinancialDashboardService
{
    Task<FinancialDashboardReportDto> GetFinancialDashboardAsync(
        FinancialDashboardFilterDto filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Encaissements de la période [fromUtc, toUtc) : totaux, panier par couvert, série (heures d'un jour ou jours), comparaison
    /// (<paramref name="compare"/> : <c>lastWeek</c> ou <c>previous</c> ; défaut selon la durée), moyens de paiement, serveurs, pourboires,
    /// remises, catégories et TVA. <paramref name="tzOffsetMinutes"/> : décalage local du restaurant.
    /// </summary>
    Task<TakingsReportDto> GetTakingsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int tzOffsetMinutes = 0,
        DateTimeOffset? nowUtc = null,
        string? compare = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reçus de la période, du plus récent au plus ancien (300 au plus).</summary>
    Task<IReadOnlyList<TakingsReceiptDto>> GetReceiptsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Journal technique depuis <paramref name="fromUtc"/> (le début du jour courant côté appelant), du plus récent au plus ancien.</summary>
    Task<IReadOnlyList<JournalEntryDto>> GetJournalAsync(
        DateTimeOffset fromUtc,
        CancellationToken cancellationToken = default);
}
