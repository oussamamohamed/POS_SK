using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

public static class TableEndpoints
{
    public static void MapTableEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tables")
                       .WithTags("Tables")
                       .RequireAuthorization();

        group.MapGet("/", async (ITableManagementService tableService) =>
        {
            var list = await tableService.GetFloorPlanTablesAsync();
            return Results.Ok(list);
        }).AllowAnonymous();

        group.MapPost("/", async (CreateTableRequest req, ITableManagementService tableService) =>
        {
            if (string.IsNullOrWhiteSpace(req.TableNumber))
            {
                return Results.BadRequest(new { Message = "Le numéro de table est requis." });
            }

            var created = await tableService.CreateTableAsync(req.TableNumber, req.Capacity <= 0 ? 2 : req.Capacity, req.PositionX ?? 0, req.PositionY ?? 0);
            return Results.Created($"/api/tables/{created.TableNumber}", created);
        });

        group.MapPost("/{tableNumber}/open", async (string tableNumber, OpenTableRequest req, ITableManagementService tableService) =>
        {
            var operatorId = req.OperatorId ?? Guid.Empty;
            var table = await tableService.OpenTableAsync(tableNumber, req.CoversCount <= 0 ? 2 : req.CoversCount, operatorId, req.WaiterName ?? "Serveur");
            return Results.Ok(table);
        });

        group.MapGet("/{tableNumber}/order", async (string tableNumber, ITableManagementService tableService) =>
        {
            var order = await tableService.GetActiveOrderForTableAsync(tableNumber);
            return order is not null ? Results.Ok(order) : Results.NotFound(new { Message = $"Aucune commande active sur la table {tableNumber}" });
        });

        group.MapPost("/{tableNumber}/items", async (string tableNumber, AddOrderItemsRequest req, ITableManagementService tableService) =>
        {
            var updated = await tableService.AddOrUpdateTableOrderItemsAsync(tableNumber, req.Items);
            return Results.Ok(updated);
        });

        group.MapPost("/{tableNumber}/dispatch", async (string tableNumber, ITableManagementService tableService, IKitchenRoutingService kds, AppDbContext db) =>
        {
            var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == tableNumber);
            if (table?.ActiveOrderId is not null)
            {
                await kds.SplitAndRouteOrderAsync(table.ActiveOrderId.Value);
            }

            var ok = await tableService.DispatchOrderLinesAsync(tableNumber);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        });
    }
}
