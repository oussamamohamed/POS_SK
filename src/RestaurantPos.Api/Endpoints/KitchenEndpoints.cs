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

        // Authentifié (comme le reste du groupe). Les 50 derniers bons sont choisis sur deux colonnes seulement, puis chargés avec leurs lignes :
        // SQLite ne trie pas un DateTimeOffset en SQL, et charger tout l'historique ralentirait chaque rafraîchissement de la cuisine.
        group.MapGet("/tickets", async (AppDbContext db) =>
        {
            var latest = (await db.KitchenTickets.AsNoTracking().Select(t => new { t.Id, t.DispatchedAtUtc }).ToListAsync())
                .OrderByDescending(t => t.DispatchedAtUtc).Take(50).Select(t => t.Id).ToList();
            var tickets = (await db.KitchenTickets.Include(t => t.Items).Where(t => latest.Contains(t.Id)).ToListAsync())
                .OrderByDescending(t => t.DispatchedAtUtc)
                .ToList();
            return Results.Ok(tickets);
        });

        group.MapPost("/tickets/{id:guid}/bump", async (Guid id, IKitchenRoutingService kds) =>
        {
            var updated = await kds.BumpTicketStateAsync(id);
            return updated is not null ? Results.Ok(updated) : Results.NotFound();
        }).RequireAuthorization("RequireKitchenOrAdmin");
    }
}
