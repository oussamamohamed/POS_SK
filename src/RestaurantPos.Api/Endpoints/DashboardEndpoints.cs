using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard")
                       .WithTags("Financial Dashboard & KPIs")
                       .RequireAuthorization("RequireManagerOrAdmin");

        group.MapGet("/financial", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            IFinancialDashboardService dashboardService,
            CancellationToken ct) =>
        {
            var filter = new FinancialDashboardFilterDto(from, to);
            var result = await dashboardService.GetFinancialDashboardAsync(filter, ct);
            return Results.Ok(result);
        });

        group.MapGet("/takings", async (
            DateTimeOffset from,
            DateTimeOffset to,
            int? tzOffsetMinutes,
            IFinancialDashboardService dashboardService,
            CancellationToken ct) =>
            Results.Ok(await dashboardService.GetTakingsAsync(from, to, Math.Clamp(tzOffsetMinutes ?? 0, -14 * 60, 14 * 60), null, ct)));
    }
}
