using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

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
            var created = await catalog.CreateCategoryAsync(req.Name, req.ColorHex, req.DisplayOrder, req.IconName);
            return Results.Ok(created);
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPut("/categories/{id}", async (string id, UpdateCategoryRequest req, IBackOfficeCatalogService catalog) =>
        {
            var updated = await catalog.UpdateCategoryAsync(id, req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.IsActive ?? true);
            return Results.Ok(updated);
        }).RequireAuthorization("RequireManagerOrAdmin");

        // Products
        group.MapGet("/products", async (string? categoryId, IBackOfficeCatalogService catalog, RestaurantPos.Infrastructure.Persistence.AppDbContext db) =>
        {
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
                    p.TaxRatePercent,
                    p.ColorHex,
                    p.DisplayOrder,
                    p.IsQuickKey,
                    p.PreparationStationId,
                    p.IsAvailable,
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
            var created = await catalog.CreateProductAsync(
                req.Name,
                req.CategoryId,
                req.Price,
                req.TaxRatePercent,
                req.Description,
                req.ColorHex,
                req.DisplayOrder,
                req.IsQuickKey,
                req.StationId
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
            var updated = await catalog.UpdateProductAsync(
                id,
                req.Name,
                req.CategoryId,
                req.Price,
                req.TaxRatePercent,
                req.Description,
                req.ColorHex,
                req.DisplayOrder,
                req.IsAvailable ?? true,
                req.IsActive ?? true,
                req.IsQuickKey,
                req.StationId
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

        group.MapDelete("/products/{id:guid}", async (Guid id, IBackOfficeCatalogService catalog) =>
        {
            var ok = await catalog.ArchiveProductAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}
