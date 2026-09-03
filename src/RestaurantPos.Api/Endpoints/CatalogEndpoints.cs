using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        });

        group.MapPut("/categories/{id}", async (string id, UpdateCategoryRequest req, IBackOfficeCatalogService catalog) =>
        {
            var updated = await catalog.UpdateCategoryAsync(id, req.Name, req.ColorHex, req.DisplayOrder, req.IconName, req.IsActive ?? true);
            return Results.Ok(updated);
        });

        // Products
        group.MapGet("/products", async (string? categoryId, IBackOfficeCatalogService catalog) =>
        {
            var products = string.IsNullOrWhiteSpace(categoryId)
                ? await catalog.GetProductsByCategoryAsync(string.Empty)
                : await catalog.GetProductsByCategoryAsync(categoryId);

            var dtos = products.Select(p => new
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
                p.IsActive
            });
            return Results.Ok(dtos);
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
        });

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
        });

        group.MapDelete("/products/{id:guid}", async (Guid id, IBackOfficeCatalogService catalog) =>
        {
            var ok = await catalog.ArchiveProductAsync(id);
            return ok ? Results.Ok(new { Success = true }) : Results.NotFound();
        });
    }
}
