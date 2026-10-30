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
    /// Encaissements du jour [fromUtc, toUtc) : totaux, panier par couvert, courbe horaire et comparaison avec le même jour de la semaine
    /// précédente. <paramref name="tzOffsetMinutes"/> : décalage local du restaurant, pour ranger les reçus par heure locale.
    /// </summary>
    Task<TakingsReportDto> GetTakingsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int tzOffsetMinutes = 0,
        DateTimeOffset? nowUtc = null,
        CancellationToken cancellationToken = default);
}
