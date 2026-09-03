using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

public static class KitchenEndpoints
{
    public static void MapKitchenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/kds")
                       .WithTags("Kitchen Display System")
                       .RequireAuthorization();

        group.MapGet("/tickets", async (AppDbContext db) =>
        {
            var tickets = (await db.KitchenTickets
                .Include(t => t.Items)
                .ToListAsync())
                .OrderByDescending(t => t.DispatchedAtUtc)
                .Take(50)
                .ToList();
            return Results.Ok(tickets);
        }).AllowAnonymous();

        group.MapPost("/tickets/{id:guid}/bump", async (Guid id, IKitchenRoutingService kds) =>
        {
            var updated = await kds.BumpTicketStateAsync(id);
            return updated is not null ? Results.Ok(updated) : Results.NotFound();
        }).RequireAuthorization("RequireKitchenOrAdmin");
    }
}
