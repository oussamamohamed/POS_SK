using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IFinancialDashboardService
{
    Task<FinancialDashboardReportDto> GetFinancialDashboardAsync(
        FinancialDashboardFilterDto filter,
        CancellationToken cancellationToken = default);
}
