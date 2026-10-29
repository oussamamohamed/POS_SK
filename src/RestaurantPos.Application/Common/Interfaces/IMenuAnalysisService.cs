using System;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IMenuAnalysisService
{
    Task<MenuAnalysisReportDto> GetMenuAnalysisAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default);
}
