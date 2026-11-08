using System;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/catalog")
                       .WithTags("Catalog")
                       .RequireAuthorization();

        // Categories
        group.MapGet("/categories", async (IBackOfficeCatalogService catalog) =>
        {
            var list = await catalog.GetAllCategoriesAsync(includeArchived: false);
            return Results.Ok(list);
        }).AllowAnonymous();

        group.MapPost("/categories", async (CreateCategoryRequest req, IBackOfficeCatalogService catalog) =>
        {
            if (!PreparationStations.IsKitchenStationOrEmpty(req.PreparationStationId))
                return Results.BadRequest(new { Message = Texts.T("errors.preparation_station_invalid") });
            var created = await catalog.CreateCategoryAsync(req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.PreparationStationId);
            return Results.Ok(created);
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPut("/categories/{id}", async (string id, UpdateCategoryRequest req, IBackOfficeCatalogService catalog) =>
        {
            if (!PreparationStations.IsKitchenStationOrEmpty(req.PreparationStationId))
                return Results.BadRequest(new { Message = Texts.T("errors.preparation_station_invalid") });
            var updated = await catalog.UpdateCategoryAsync(id, req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.IsActive ?? true, req.PreparationStationId);
            return Results.Ok(updated);
        }).RequireAuthorization("RequireManagerOrAdmin");

        // Products
        group.MapGet("/products", async (string? categoryId, HttpContext http, IAuthorizationService authz, IBackOfficeCatalogService catalog, RestaurantPos.Infrastructure.Persistence.AppDbContext db) =>
        {
            // Le coût de revient est une donnée de gestion : jamais servi aux appareils de caisse anonymes.
            var canSeeCost = (await authz.AuthorizeAsync(http.User, "RequireManagerOrAdmin")).Succeeded;

            var products = string.IsNullOrWhiteSpace(categoryId)
                ? await catalog.GetProductsByCategoryAsync(string.Empty)
                : await catalog.GetProductsByCategoryAsync(categoryId);

            var productIds = products.Select(p => p.Id).ToList();
            var modifierGroups = await db.ModifierGroups
                .Include(g => g.Options)
                .Where(g => productIds.Contains(g.ProductId))
                .OrderBy(g => g.DisplayOrder)
                .ToListAsync();

            var dtos = products.Select(p =>
            {
                var groups = modifierGroups.Where(g => g.ProductId == p.Id).Select(g => new
                {
                    g.Id,
                    g.GroupName,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsMandatory,
                    g.IsSingleChoice,
                    Options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new
                    {
                        o.Id,
                        o.Name,
                        ExtraPrice = o.ExtraPrice.ToDecimal(),
                        o.IsDefault
                    })
                }).ToList();

                return new
                {
                    p.Id,
                    p.Name,
                    p.CategoryId,
                    p.Description,
                    Price = p.Price.ToDecimal(),
                    Cost = canSeeCost ? p.Cost?.ToDecimal() : null,
                    p.TaxRatePercent,
                    p.ColorHex,
                    p.DisplayOrder,
                    p.IsQuickKey,
                    p.PreparationStationId,
                    p.IsAvailable,
                    p.AllergenMask,
                    p.AllergensDeclared,
                    p.AllergensOverridden,
                    p.IsActive,
                    HasModifiers = groups.Count > 0,
                    ModifierGroups = groups
                };
            });
            return Results.Ok(dtos);
        }).AllowAnonymous();

        group.MapGet("/products/{id:guid}/modifier-groups", async (Guid id, RestaurantPos.Infrastructure.Persistence.AppDbContext db) =>
        {
            var groups = await db.ModifierGroups
                .Include(g => g.Options)
                .Where(g => g.ProductId == id)
                .OrderBy(g => g.DisplayOrder)
                .Select(g => new
                {
                    g.Id,
                    g.ProductId,
                    g.GroupName,
                    g.MinSelections,
                    g.MaxSelections,
                    g.IsMandatory,
                    g.IsSingleChoice,
                    Options = g.Options.OrderBy(o => o.DisplayOrder).Select(o => new
                    {
                        o.Id,
                        o.Name,
                        ExtraPrice = o.ExtraPrice.ToDecimal(),
                        o.IsDefault
                    })
                })
                .ToListAsync();

            return Results.Ok(groups);
        }).AllowAnonymous();

        group.MapPost("/products", async (CreateProductRequest req, IBackOfficeCatalogService catalog) =>
        {
            if (req.Price < 0)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.product_price_negative") });
            }
            if (req.Cost < 0)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.product_cost_negative") });
            }

            var created = await catalog.CreateProductAsync(
                req.Name,
                req.CategoryId,
                req.Price,
                req.TaxRatePercent,
                req.Description,
                req.ColorHex,
                req.DisplayOrder,
                req.IsQuickKey,
                req.StationId,
                req.Cost
            );
            return Results.Ok(new
            {
                created.Id,
                created.Name,
                created.CategoryId,
                Price = created.Price.ToDecimal(),
                created.TaxRatePercent,
                created.IsQuickKey
            });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPut("/products/{id:guid}", async (Guid id, UpdateProductRequest req, IBackOfficeCatalogService catalog) =>
        {
            if (req.Price < 0)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.product_price_negative") });
            }
            if (req.Cost < 0)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.product_cost_negative") });
            }

            var updated = await catalog.UpdateProductAsync(
                id,
                req.Name,
                req.CategoryId,
                req.Price,
                req.TaxRatePercent,
                req.Description,
                req.ColorHex,
                req.DisplayOrder,
                req.IsAvailable,
                req.IsActive ?? true,
                req.IsQuickKey,
                req.StationId,
                req.Cost,
                req.ClearCost
            );
            return Results.Ok(new
            {
                updated.Id,
                updated.Name,
                updated.CategoryId,
                Price = updated.Price.ToDecimal(),
                updated.TaxRatePercent,
                updated.IsQuickKey,
                updated.PreparationStationId,
                updated.IsActive
            });
        }).RequireAuthorization("RequireManagerOrAdmin");

        // Rupture : tout opérateur authentifié (un serveur la signale en salle), diffusée à tous les postes.
        group.MapPut("/products/{id:guid}/availability", async (Guid id, SetAvailabilityRequest req, IBackOfficeCatalogService catalog, IHubContext<PosHub, IPosHubClient> hub) =>
        {
            var product = await catalog.SetProductAvailabilityAsync(id, req.IsAvailable);
            if (product is null) return Results.NotFound();
            await hub.Clients.All.OnProductAvailabilityChanged(product.Id, product.IsAvailable);
            return Results.Ok(new { product.Id, product.Name, product.IsAvailable });
        });

        group.MapPut("/products/{id:guid}/allergens", async (Guid id, SetAllergensRequest req, IBackOfficeCatalogService catalog) =>
        {
            if (!Allergens.IsValidMask(req.Mask))
            {
                return Results.BadRequest(new { Message = Texts.T("errors.allergen_mask_invalid") });
            }
            var product = await catalog.SetProductAllergensAsync(id, req.Declared, req.Mask, req.Overridden);
            return product is null
                ? Results.NotFound()
                : Results.Ok(new { product.Id, product.AllergenMask, product.AllergensDeclared, product.AllergensOverridden });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapDelete("/products/{id:guid}", async (Guid id, IBackOfficeCatalogService catalog) =>
        {
            var ok = await catalog.ArchiveProductAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}
