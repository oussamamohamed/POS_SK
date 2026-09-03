using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IFecExportService
{
    Task<FecExportResult> GenerateFecAsync(FecExportRequest request, CancellationToken cancellationToken = default);
}
