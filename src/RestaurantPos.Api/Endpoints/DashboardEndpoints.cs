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
            string? compare,
            IFinancialDashboardService dashboardService,
            CancellationToken ct) =>
        {
            // Au plus 92 jours : la courbe quotidienne et les agrégats se calculent en mémoire.
            if (to <= from || to - from > TimeSpan.FromDays(92)) return Results.BadRequest();
            return Results.Ok(await dashboardService.GetTakingsAsync(from, to, Math.Clamp(tzOffsetMinutes ?? 0, -14 * 60, 14 * 60), null, compare, ct));
        });

        group.MapGet("/receipts", async (
            DateTimeOffset from,
            DateTimeOffset to,
            IFinancialDashboardService dashboardService,
            CancellationToken ct) =>
            to <= from || to - from > TimeSpan.FromDays(92)
                ? Results.BadRequest()
                : Results.Ok(await dashboardService.GetReceiptsAsync(from, to, ct)));

        // Journal du jour en cours seulement : le début du jour se calcule côté serveur avec le décalage local du restaurant.
        group.MapGet("/journal", async (
            int? tzOffsetMinutes,
            IFinancialDashboardService dashboardService,
            TimeProvider clock,
            CancellationToken ct) =>
        {
            var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes ?? 0, -14 * 60, 14 * 60));
            var localNow = clock.GetUtcNow() + offset;
            var startUtc = new DateTimeOffset(localNow.UtcDateTime.Date, TimeSpan.Zero) - offset;
            return Results.Ok(await dashboardService.GetJournalAsync(startUtc, ct));
        });

        // Tendances du jour (carte « à surveiller ») : aucune persistance, comparées au même jour de la semaine passée à la même heure.
        group.MapGet("/insights", async (int? tzOffsetMinutes, IInsightsService insights, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await insights.GetInsightsAsync(Math.Clamp(tzOffsetMinutes ?? 0, -14 * 60, 14 * 60), clock.GetUtcNow(), ct)));

        // Résumé de fin de journée : totaux identiques au dernier Z du poste (ou de la date locale donnée).
        group.MapGet("/daily-summary", async (string? terminalId, DateOnly? date, int? tzOffsetMinutes, IInsightsService insights, CancellationToken ct) =>
        {
            var summary = await insights.GetDailySummaryAsync(string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId, Math.Clamp(tzOffsetMinutes ?? 0, -14 * 60, 14 * 60), date, ct);
            return summary is null ? Results.NotFound(new { Code = "no_closure" }) : Results.Ok(summary);
        });
    }
}
