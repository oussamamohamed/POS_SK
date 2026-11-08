using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;

namespace RestaurantPos.Api.Endpoints;

public sealed record IngredientRequest(string Name, string Unit, long UnitCostMilliCents, bool IsPreparation, long? YieldMilli, int AllergenMask, long? LowStockMilli, bool IsActive = true);

public sealed record SetRecipeRequest(RecipeLineInput[]? Lines);

public sealed record ReceiptRequest(Guid IngredientId, long QuantityMilli, long UnitCostMilliCents, string? Note);

public sealed record CountRequest(Guid IngredientId, long CountedMilli, string? Note);

public sealed record LossRequest(Guid IngredientId, long QuantityMilli, string? Note);

public sealed record ConsumptionItem(Guid IngredientId, long DeltaQuantityMilli);

public sealed record SetConsumptionRequest(ConsumptionItem[]? Items);

public static class StockEndpoints
{
    private static object Dto(Ingredient i, IReadOnlyDictionary<Guid, long>? levels = null) => new
    {
        i.Id,
        i.Name,
        i.Unit,
        i.UnitCostMilliCents,
        i.IsPreparation,
        i.YieldMilli,
        i.AllergenMask,
        i.LowStockMilli,
        i.IsActive,
        StockMilli = i.IsPreparation ? (long?)null : levels?.GetValueOrDefault(i.Id) ?? 0, // les préparations ne sont pas suivies
    };

    private static Guid? Operator(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var id) ? id : null;

    private static object MovementDto(StockMovement m) => new { m.Id, m.IngredientId, m.Kind, m.QuantityMilli, m.UnitCostMilliCents, m.OrderId, m.OperatorId, m.Note, m.CreatedAtUtc };

    private static async Task<IResult> Guarded(Func<Task<IResult>> action, IStockService stock, IHubContext<PosHub, IPosHubClient> hub)
    {
        try
        {
            var result = await action();
            await StockAvailabilityBroadcaster.PublishAsync(stock, hub);
            return result;
        }
        catch (IngredientException ex) { return Results.BadRequest(new { Code = ex.Code }); }
    }

    private static IngredientInput Input(IngredientRequest r) =>
        new(r.Name ?? "", r.Unit ?? "", r.UnitCostMilliCents, r.IsPreparation, r.YieldMilli, r.AllergenMask, r.LowStockMilli, r.IsActive);

    private static IResult Refuse(IngredientException ex) => ex.Code switch
    {
        "ingredient_in_use" => Results.Conflict(new { Code = ex.Code, Products = ex.Products }),
        "ingredient_name_taken" or "ingredient_has_movements" => Results.Conflict(new { Code = ex.Code }),
        _ => Results.BadRequest(new { Code = ex.Code }),
    };

    public static void MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        var stock = app.MapGroup("/api/stock").WithTags("Stock").RequireAuthorization("RequireManagerOrAdmin");

        stock.MapGet("/ingredients", async (IRecipeService svc, IStockService levels) =>
        {
            var current = await levels.LevelsAsync();
            return Results.Ok((await svc.ListIngredientsAsync()).Select(i => Dto(i, current)));
        });

        stock.MapPost("/ingredients", async (IngredientRequest req, IRecipeService svc) =>
        {
            try { var i = await svc.CreateIngredientAsync(Input(req)); return Results.Created($"/api/stock/ingredients/{i.Id}", Dto(i)); }
            catch (IngredientException ex) { return Refuse(ex); }
        });

        stock.MapPut("/ingredients/{id:guid}", async (Guid id, IngredientRequest req, IRecipeService svc) =>
        {
            try { var i = await svc.UpdateIngredientAsync(id, Input(req)); return i is null ? Results.NotFound() : Results.Ok(Dto(i)); }
            catch (IngredientException ex) { return Refuse(ex); }
        });

        stock.MapDelete("/ingredients/{id:guid}", async (Guid id, IRecipeService svc) =>
        {
            try { return await svc.DeleteIngredientAsync(id) ? Results.Ok(new { Success = true }) : Results.NotFound(); }
            catch (IngredientException ex) { return Refuse(ex); }
        });

        stock.MapGet("/ingredients/{id:guid}/recipe", async (Guid id, IRecipeService svc) =>
            await svc.GetPreparationRecipeAsync(id) is { } lines ? Results.Ok(new { Lines = lines }) : Results.NotFound());

        stock.MapPut("/ingredients/{id:guid}/recipe", async (Guid id, SetRecipeRequest req, IRecipeService svc) =>
        {
            try { return await svc.SetPreparationRecipeAsync(id, req.Lines ?? []) is { } i ? Results.Ok(Dto(i)) : Results.NotFound(); }
            catch (RecipeException ex) { return Results.BadRequest(new { Code = ex.Code }); }
        });

        stock.MapPost("/receipts", (ReceiptRequest req, IStockService svc, IHubContext<PosHub, IPosHubClient> hub, ClaimsPrincipal user) =>
            Guarded(async () => await svc.ReceiveAsync(req.IngredientId, req.QuantityMilli, req.UnitCostMilliCents, req.Note, Operator(user)) is { } m ? Results.Created($"/api/stock/movements?ingredientId={m.IngredientId}", MovementDto(m)) : Results.NotFound(), svc, hub));

        stock.MapPost("/counts", (CountRequest req, IStockService svc, IHubContext<PosHub, IPosHubClient> hub, ClaimsPrincipal user) =>
            Guarded(async () => await svc.CountAsync(req.IngredientId, req.CountedMilli, req.Note, Operator(user)) is { } r ? Results.Ok(r) : Results.NotFound(), svc, hub));

        stock.MapPost("/losses", (LossRequest req, IStockService svc, IHubContext<PosHub, IPosHubClient> hub, ClaimsPrincipal user) =>
            Guarded(async () => await svc.LossAsync(req.IngredientId, req.QuantityMilli, req.Note, Operator(user)) is { } m ? Results.Created($"/api/stock/movements?ingredientId={m.IngredientId}", MovementDto(m)) : Results.NotFound(), svc, hub));

        stock.MapGet("/movements", async (Guid? ingredientId, DateTimeOffset? from, DateTimeOffset? to, IStockService svc) =>
            Results.Ok((await svc.MovementsAsync(ingredientId, from, to)).Select(MovementDto)));

        stock.MapGet("/alerts", async (IStockService svc) => Results.Ok(await svc.AlertsAsync()));

        // Réglages du stock et des allergènes : hors /api/settings, dont le corps est signé dans le journal fiscal.
        stock.MapGet("/settings", async (IStockService svc) => Results.Ok(await svc.GetSettingsAsync()));

        stock.MapPut("/settings", (StockSettings req, IStockService svc, IHubContext<PosHub, IPosHubClient> hub) =>
            Guarded(async () => Results.Ok(await svc.UpdateSettingsAsync(req)), svc, hub));

        var options = app.MapGroup("/api/catalog/modifier-options/{id:guid}/consumption").WithTags("Stock").RequireAuthorization("RequireManagerOrAdmin");

        options.MapGet("/", async (Guid id, IStockService svc) => Results.Ok((await svc.ModifierConsumptionAsync(id)).Select(c => new { c.IngredientId, c.DeltaQuantityMilli })));

        options.MapPut("/", (Guid id, SetConsumptionRequest req, IStockService svc, IHubContext<PosHub, IPosHubClient> hub) =>
            Guarded(async () => await svc.SetModifierConsumptionAsync(id, (req.Items ?? []).Select(i => (i.IngredientId, i.DeltaQuantityMilli)).ToList()) is { } rows
                ? Results.Ok(rows.Select(c => new { c.IngredientId, c.DeltaQuantityMilli })) : Results.NotFound(), svc, hub));

        var recipe = app.MapGroup("/api/catalog/products/{id:guid}/recipe").WithTags("Stock").RequireAuthorization("RequireManagerOrAdmin");

        recipe.MapGet("/", async (Guid id, IRecipeService svc) =>
            await svc.GetProductRecipeAsync(id) is { } r ? Results.Ok(r) : Results.NotFound());

        recipe.MapPut("/", async (Guid id, SetRecipeRequest req, IRecipeService svc) =>
        {
            try { return await svc.SetProductRecipeAsync(id, req.Lines ?? []) is { } r ? Results.Ok(r) : Results.NotFound(); }
            catch (RecipeException ex) { return Results.BadRequest(new { Code = ex.Code }); }
        });
    }
}

/// <summary>Après une vente, un avoir, une réception ou un réglage : applique la rupture automatique et la diffuse aux postes.</summary>
public static class StockAvailabilityBroadcaster
{
    public static async Task PublishAsync(IStockService stock, IHubContext<PosHub, IPosHubClient> hub)
    {
        foreach (var (productId, isAvailable) in await stock.RefreshAutoAvailabilityAsync())
            await hub.Clients.All.OnProductAvailabilityChanged(productId, isAvailable);
    }
}

/// <summary>Filtre des routes qui encaissent ou annulent : la consommation du stock vient d'avoir lieu dans la transaction, on rafraîchit les ruptures.</summary>
public sealed class StockAvailabilityFilter(IStockService stock, IHubContext<PosHub, IPosHubClient> hub) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        try { await StockAvailabilityBroadcaster.PublishAsync(stock, hub); }
        catch (Exception) { /* la rupture automatique ne doit jamais faire échouer une vente déjà enregistrée */ }
        return result;
    }
}
