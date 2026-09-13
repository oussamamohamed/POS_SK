using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

public static class HappyHourEndpoints
{
    public static void MapHappyHourEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/happy-hour")
                       .WithTags("Happy Hour Pricing")
                       .RequireAuthorization();

        // 1. Status Check
        group.MapGet("/status", async (string? terminalId, IHappyHourPricingService hhService) =>
        {
            var term = !string.IsNullOrWhiteSpace(terminalId) ? terminalId : "POS_MAIN";
            var status = await hhService.GetCurrentStatusAsync(term);
            return Results.Ok(status);
        }).AllowAnonymous();

        // 2. Pricing Table
        group.MapGet("/pricing-table", async (string? terminalId, IHappyHourPricingService hhService) =>
        {
            var term = !string.IsNullOrWhiteSpace(terminalId) ? terminalId : "POS_MAIN";
            var table = await hhService.GetPricingTableAsync(term);
            return Results.Ok(table);
        }).AllowAnonymous();

        // 3. Supervisor Override: Activate / Extend
        group.MapPost("/override/activate", async (ActivateOverrideRequest req, IHappyHourPricingService hhService, Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.PosHub, RestaurantPos.Api.Hubs.IPosHubClient> hub) =>
        {
            var res = await hhService.ActivateOverrideAsync(req);
            if (!res.Success)
            {
                return Results.Json(new { Message = res.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            var status = await hhService.GetCurrentStatusAsync(req.TerminalId);
            await hub.Clients.All.OnHappyHourStatusChanged(status);
            return Results.Ok(res);
        });
        group.MapPost("/override/stop", async (StopOverrideRequest req, IHappyHourPricingService hhService, Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.PosHub, RestaurantPos.Api.Hubs.IPosHubClient> hub) =>
        {
            var res = await hhService.StopOverrideAsync(req);
            if (!res.Success)
            {
                return Results.Json(new { Message = res.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            var status = await hhService.GetCurrentStatusAsync(req.TerminalId);
            await hub.Clients.All.OnHappyHourStatusChanged(status);
            return Results.Ok(res);
        });
        group.MapGet("/schedules", async (AppDbContext db) =>
        {
            var schedules = await db.HappyHourSchedules
                .Include(s => s.PriceRules)
                .AsNoTracking()
                .ToListAsync();

            return Results.Ok(schedules.Select(s => new HappyHourScheduleDto(
                s.Id,
                s.Name,
                s.DaysOfWeek,
                s.StartTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                s.EndTime.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
                s.IsActive,
                s.AppliesToTakeaway,
                s.Priority,
                s.PriceRules.Select(r => new HappyHourRuleDto(
                    r.Id,
                    r.TargetType,
                    r.TargetId,
                    r.TargetName,
                    r.PricingMode,
                    r.FixedPrice?.ToDecimal(),
                    r.DiscountPercent
                )).ToList()
            )));
        }).AllowAnonymous();

        group.MapPost("/schedules", async (HappyHourScheduleDto dto, AppDbContext db) =>
        {
            if (!TimeOnly.TryParse(dto.StartTime, out var startTime) || !TimeOnly.TryParse(dto.EndTime, out var endTime))
            {
                return Results.BadRequest(new { Message = "Format horaire invalide (HH:mm requis)." });
            }

            var schedule = new HappyHourSchedule
            {
                Name = dto.Name,
                DaysOfWeek = dto.DaysOfWeek ?? new List<DayOfWeek>(),
                StartTime = startTime,
                EndTime = endTime,
                IsActive = dto.IsActive,
                AppliesToTakeaway = dto.AppliesToTakeaway,
                Priority = dto.Priority
            };

            if (dto.PriceRules != null)
            {
                foreach (var r in dto.PriceRules)
                {
                    schedule.PriceRules.Add(new HappyHourPriceRule
                    {
                        TargetType = r.TargetType,
                        TargetId = r.TargetId,
                        TargetName = r.TargetName,
                        PricingMode = r.PricingMode,
                        FixedPrice = r.FixedPrice.HasValue ? Money.FromEuros(r.FixedPrice.Value) : null,
                        DiscountPercent = r.DiscountPercent
                    });
                }
            }

            db.HappyHourSchedules.Add(schedule);
            await db.SaveChangesAsync();

            return Results.Ok(new { schedule.Id, Message = "Plage Happy Hour créée avec succès." });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapDelete("/schedules/{id:guid}", async (Guid id, AppDbContext db) =>
        {
            var schedule = await db.HappyHourSchedules.FindAsync(id);
            if (schedule == null)
            {
                return Results.NotFound(new { Message = "Plage horaire introuvable." });
            }

            db.HappyHourSchedules.Remove(schedule);
            await db.SaveChangesAsync();

            return Results.Ok(new { Message = "Plage horaire supprimée." });
        }).RequireAuthorization("RequireManagerOrAdmin");

        // 6. Batch Apply Price Rules
        group.MapPost("/schedules/{scheduleId:guid}/rules/batch", async (
            Guid scheduleId,
            BatchPriceRulesRequestDto dto,
            IHappyHourPricingService hhService,
            Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.PosHub, RestaurantPos.Api.Hubs.IPosHubClient> hub) =>
        {
            var res = await hhService.ApplyBatchPriceRulesAsync(scheduleId, dto);
            if (!res.Success)
            {
                return Results.BadRequest(new { res.Message });
            }

            var status = await hhService.GetCurrentStatusAsync("POS_MAIN");
            await hub.Clients.All.OnHappyHourStatusChanged(status);

            return Results.Ok(res);
        }).RequireAuthorization("RequireManagerOrAdmin");

        // 7. Batch Delete Price Rules
        group.MapDelete("/schedules/{scheduleId:guid}/rules/batch", async (
            Guid scheduleId,
            [Microsoft.AspNetCore.Mvc.FromBody] BatchDeleteRulesRequestDto dto,
            IHappyHourPricingService hhService,
            Microsoft.AspNetCore.SignalR.IHubContext<RestaurantPos.Api.Hubs.PosHub, RestaurantPos.Api.Hubs.IPosHubClient> hub) =>
        {
            var res = await hhService.DeleteBatchPriceRulesAsync(scheduleId, dto);
            if (!res.Success)
            {
                return Results.BadRequest(new { res.Message });
            }

            var status = await hhService.GetCurrentStatusAsync("POS_MAIN");
            await hub.Clients.All.OnHappyHourStatusChanged(status);

            return Results.Ok(res);
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}
