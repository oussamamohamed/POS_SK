using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;

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
        }).AllowAnonymous();

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
        }).AllowAnonymous();

        group.MapPost("/", async (CreateStaffRequest req, IStaffManagementService staff) =>
        {
            if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            {
                role = UserRole.Waiter;
            }
            var user = await staff.CreateStaffMemberAsync(req.Name, role, req.Pin);
            return Results.Ok(new { user.Id, user.Name, Role = user.Role.ToString(), user.IsActive });
        });

        group.MapPut("/{id:guid}", async (Guid id, UpdateStaffRequest req, IStaffManagementService staff) =>
        {
            if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            {
                role = UserRole.Waiter;
            }
            var user = await staff.UpdateStaffMemberAsync(id, req.Name, role, req.IsActive ?? true);
            if (!string.IsNullOrWhiteSpace(req.Pin))
            {
                await staff.ResetStaffPinAsync(id, req.Pin);
            }
            return Results.Ok(new { user.Id, user.Name, Role = user.Role.ToString(), user.IsActive });
        });

        group.MapDelete("/{id:guid}", async (Guid id, IStaffManagementService staff) =>
        {
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        });

        group.MapDelete("/operators/{id:guid}", async (Guid id, IStaffManagementService staff) =>
        {
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        });

        group.MapPost("/{id:guid}/deactivate", async (Guid id, IStaffManagementService staff) =>
        {
            var ok = await staff.DeactivateStaffMemberAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        });
    }
}
