using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class StaffEndpoints
{
    public static void MapStaffEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/staff")
                       .WithTags("Staff Management")
                       .RequireAuthorization();

        group.MapGet("/", async (IStaffManagementService staff) =>
        {
            var list = await staff.GetAllStaffAsync(includeInactive: true);
            return Results.Ok(list.Select(u => new
            {
                u.Id,
                u.Name,
                Role = u.Role.ToString(),
                u.IsActive,
                u.CreatedAtUtc
            }));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapGet("/operators", async (IStaffManagementService staff) =>
        {
            var list = await staff.GetAllStaffAsync(includeInactive: true);
            return Results.Ok(list.Select(u => new
            {
                u.Id,
                u.Name,
                Role = u.Role.ToString(),
                u.IsActive,
                u.CreatedAtUtc
            }));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/", async (CreateStaffRequest req, ClaimsPrincipal caller, IStaffManagementService staff) =>
        {
            if (!TryParseRole(req.Role, out var role))
            {
                return Results.BadRequest(new { Message = Texts.T("errors.invalid_role") });
            }
            // Seul un Admin crée un Admin.
            if (role == UserRole.Admin && !caller.IsInRole("Admin"))
            {
                return Forbidden();
            }
            var user = await staff.CreateStaffMemberAsync(req.Name, role, req.Pin);
            return Results.Ok(new { user.Id, user.Name, Role = user.Role.ToString(), user.IsActive });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPut("/{id:guid}", async (Guid id, UpdateStaffRequest req, ClaimsPrincipal caller, IStaffManagementService staff) =>
        {
            if (!TryParseRole(req.Role, out var role))
            {
                return Results.BadRequest(new { Message = Texts.T("errors.invalid_role") });
            }
            // Seul un Admin attribue le rôle Admin ou modifie (dont rétrograde) un Admin.
            if (!caller.IsInRole("Admin") && (role == UserRole.Admin || await TargetsAdminAsync(staff, id, caller)))
            {
                return Forbidden();
            }
            var user = await staff.UpdateStaffMemberAsync(id, req.Name, role, req.IsActive ?? true);
            if (!string.IsNullOrWhiteSpace(req.Pin))
            {
                await staff.ResetStaffPinAsync(id, req.Pin);
            }
            return Results.Ok(new { user.Id, user.Name, Role = user.Role.ToString(), user.IsActive });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal caller, IStaffManagementService staff) =>
        {
            if (await TargetsAdminAsync(staff, id, caller))
            {
                return Forbidden();
            }
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapDelete("/operators/{id:guid}", async (Guid id, ClaimsPrincipal caller, IStaffManagementService staff) =>
        {
            if (await TargetsAdminAsync(staff, id, caller))
            {
                return Forbidden();
            }
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ClaimsPrincipal caller, IStaffManagementService staff) =>
        {
            if (await TargetsAdminAsync(staff, id, caller))
            {
                return Forbidden();
            }
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        }).RequireAuthorization("RequireManagerOrAdmin");
    }

    private static IResult Forbidden() =>
        Results.Json(new { Message = Texts.T("errors.admin_role_requires_admin") }, statusCode: StatusCodes.Status403Forbidden);

    // Vrai si la cible est un Admin et que l'appelant ne l'est pas ; cible inexistante : faux.
    private static async Task<bool> TargetsAdminAsync(IStaffManagementService staff, Guid id, ClaimsPrincipal caller) =>
        !caller.IsInRole("Admin") && (await staff.GetStaffByIdAsync(id))?.Role == UserRole.Admin;

    // IsDefined écarte les valeurs numériques hors énumération ("99").
    private static bool TryParseRole(string? value, out UserRole role) =>
        Enum.TryParse(value, true, out role) && Enum.IsDefined(role);
}
