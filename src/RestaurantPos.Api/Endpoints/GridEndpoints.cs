using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class GridEndpoints
{
    public static void MapGridEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/grid-layouts")
                       .WithTags("Touch Grid Management")
                       .RequireAuthorization();

        group.MapGet("/", async (IGridManagementService gridService) =>
        {
            var layouts = await gridService.GetAllLayoutsAsync();
            return Results.Ok(layouts);
        }).AllowAnonymous();

        group.MapGet("/{categoryId}", async (string categoryId, int? page, IGridManagementService gridService) =>
        {
            int pageIndex = page.GetValueOrDefault(0);
            var layout = await gridService.GetLayoutByCategoryAsync(categoryId, pageIndex);
            return layout is not null ? Results.Ok(layout) : Results.NotFound(new { Message = Texts.T("errors.grid_not_found_for_category", ("category", categoryId), ("page", pageIndex)) });
        }).AllowAnonymous();

        group.MapGet("/{categoryId}/pages", async (string categoryId, IGridManagementService gridService) =>
        {
            var pages = await gridService.GetAllPagesByCategoryAsync(categoryId);
            return Results.Ok(pages);
        }).AllowAnonymous();

        group.MapPost("/", async (UpdateGridLayoutRequest req, IGridManagementService gridService, IHubContext<KitchenHub> hubContext) =>
        {
            var saved = await gridService.SaveLayoutAsync(req);
            await hubContext.Clients.All.SendAsync("OnGridLayoutUpdated", saved);
            return Results.Ok(saved);
        });

        group.MapPost("/swap", async (SwapGridSlotsRequest req, IGridManagementService gridService, IHubContext<KitchenHub> hubContext) =>
        {
            var swapped = await gridService.SwapSlotsAsync(req);
            if (swapped is null)
            {
                return Results.NotFound(new { Message = Texts.T("errors.grid_swap_not_found") });
            }
            await hubContext.Clients.All.SendAsync("OnGridLayoutUpdated", swapped);
            return Results.Ok(swapped);
        });

        group.MapPost("/dimensions", async (UpdateGridDimensionsRequest req, IGridManagementService gridService, IHubContext<KitchenHub> hubContext) =>
        {
            var updatedList = await gridService.UpdateDimensionsAsync(req);
            foreach (var layout in updatedList)
            {
                await hubContext.Clients.All.SendAsync("OnGridLayoutUpdated", layout);
            }
            return Results.Ok(updatedList);
        });
    }
}
