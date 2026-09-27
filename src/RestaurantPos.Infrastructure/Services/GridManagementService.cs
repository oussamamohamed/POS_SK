using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class GridManagementService : IGridManagementService
{
    private readonly AppDbContext _dbContext;

    public GridManagementService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<List<GridLayoutDto>> GetAllLayoutsAsync(CancellationToken cancellationToken = default)
    {
        var layouts = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .OrderBy(l => l.CategoryId)
            .ThenBy(l => l.PageIndex)
            .ToListAsync(cancellationToken);

        var totalPagesMap = layouts
            .GroupBy(l => l.CategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        return layouts.Select(l => MapToDto(l, totalPagesMap.GetValueOrDefault(l.CategoryId, 1))).ToList();
    }

    public async Task<GridLayoutDto?> GetLayoutByCategoryAsync(string categoryId, int pageIndex = 0, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return null;
        }

        var totalPages = await _dbContext.GridLayouts
            .CountAsync(l => l.CategoryId == categoryId, cancellationToken);

        var layout = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .FirstOrDefaultAsync(l => l.CategoryId == categoryId && l.PageIndex == pageIndex, cancellationToken);

        if (layout == null)
        {
            if (pageIndex == 0 && totalPages == 0)
            {
                // Seed default layout pages for existing category products
                layout = await CreateDefaultLayoutAsync(categoryId, cancellationToken);
                totalPages = await _dbContext.GridLayouts.CountAsync(l => l.CategoryId == categoryId, cancellationToken);
            }
            else
            {
                // Inherit dimensions from page 0 if exists
                var page0 = await _dbContext.GridLayouts.FirstOrDefaultAsync(l => l.CategoryId == categoryId && l.PageIndex == 0, cancellationToken);
                int cols = page0?.ColumnsCount ?? 4;
                int rows = page0?.RowsCount ?? 4;

                // Create a blank new page
                layout = new GridLayout
                {
                    CategoryId = categoryId,
                    ColumnsCount = cols,
                    RowsCount = rows,
                    PageIndex = pageIndex,
                    Version = 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };

                int totalSlots = cols * rows;
                for (int i = 0; i < totalSlots; i++)
                {
                    layout.Slots.Add(new GridSlot
                    {
                        GridLayoutId = layout.Id,
                        RowIndex = i / cols,
                        ColumnIndex = i % cols,
                        SlotIndex = i
                    });
                }

                _dbContext.GridLayouts.Add(layout);
                await _dbContext.SaveChangesAsync(cancellationToken);
                totalPages = await _dbContext.GridLayouts.CountAsync(l => l.CategoryId == categoryId, cancellationToken);
            }
        }

        return MapToDto(layout, Math.Max(1, totalPages));
    }

    public async Task<List<GridLayoutDto>> GetAllPagesByCategoryAsync(string categoryId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(categoryId))
        {
            return new List<GridLayoutDto>();
        }

        var layouts = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .Where(l => l.CategoryId == categoryId)
            .OrderBy(l => l.PageIndex)
            .ToListAsync(cancellationToken);

        if (layouts.Count == 0)
        {
            var first = await CreateDefaultLayoutAsync(categoryId, cancellationToken);
            layouts = await _dbContext.GridLayouts
                .Include(l => l.Slots)
                    .ThenInclude(s => s.Product)
                .Where(l => l.CategoryId == categoryId)
                .OrderBy(l => l.PageIndex)
                .ToListAsync(cancellationToken);
        }

        int totalPages = layouts.Count;
        return layouts.Select(l => MapToDto(l, totalPages)).ToList();
    }

    public async Task<GridLayoutDto> SaveLayoutAsync(UpdateGridLayoutRequest request, CancellationToken cancellationToken = default)
    {
        var layout = await _dbContext.GridLayouts
            .Include(l => l.Slots)
            .FirstOrDefaultAsync(l => l.CategoryId == request.CategoryId && l.PageIndex == request.PageIndex, cancellationToken);

        if (layout == null)
        {
            layout = new GridLayout
            {
                CategoryId = request.CategoryId,
                ColumnsCount = request.ColumnsCount,
                RowsCount = request.RowsCount,
                PageIndex = request.PageIndex,
                Version = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };
            _dbContext.GridLayouts.Add(layout);
        }
        else
        {
            layout.ColumnsCount = request.ColumnsCount;
            layout.RowsCount = request.RowsCount;
            layout.Version++;
            layout.UpdatedAtUtc = DateTimeOffset.UtcNow;
        }

        // Clear existing slots
        _dbContext.GridSlots.RemoveRange(layout.Slots);
        layout.Slots.Clear();

        int totalSlots = request.ColumnsCount * request.RowsCount;
        for (int i = 0; i < totalSlots; i++)
        {
            int r = i / request.ColumnsCount;
            int c = i % request.ColumnsCount;

            var item = request.Slots.FirstOrDefault(s => s.RowIndex == r && s.ColumnIndex == c);
            var slot = new GridSlot
            {
                GridLayoutId = layout.Id,
                RowIndex = r,
                ColumnIndex = c,
                SlotIndex = i,
                ProductId = item?.ProductId,
                CustomLabel = item?.CustomLabel,
                CustomColorHex = item?.CustomColorHex
            };

            layout.Slots.Add(slot);
            _dbContext.GridSlots.Add(slot);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        int totalPages = Math.Max(1, await _dbContext.GridLayouts.CountAsync(l => l.CategoryId == request.CategoryId, cancellationToken));

        var refreshed = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .FirstAsync(l => l.Id == layout.Id, cancellationToken);

        return MapToDto(refreshed, totalPages);
    }

    public async Task<GridLayoutDto?> SwapSlotsAsync(SwapGridSlotsRequest request, CancellationToken cancellationToken = default)
    {
        var layout = await _dbContext.GridLayouts
            .Include(l => l.Slots)
            .FirstOrDefaultAsync(l => l.Id == request.LayoutId, cancellationToken);

        if (layout == null)
        {
            return null;
        }

        var sourceSlot = layout.Slots.FirstOrDefault(s => s.RowIndex == request.SourceRow && s.ColumnIndex == request.SourceCol);
        var targetSlot = layout.Slots.FirstOrDefault(s => s.RowIndex == request.TargetRow && s.ColumnIndex == request.TargetCol);

        if (sourceSlot == null)
        {
            return null;
        }

        if (targetSlot == null)
        {
            targetSlot = new GridSlot
            {
                GridLayoutId = layout.Id,
                RowIndex = request.TargetRow,
                ColumnIndex = request.TargetCol,
                SlotIndex = (request.TargetRow * layout.ColumnsCount) + request.TargetCol
            };
            layout.Slots.Add(targetSlot);
            _dbContext.GridSlots.Add(targetSlot);
        }

        // Swap properties
        var tempProductId = sourceSlot.ProductId;
        var tempLabel = sourceSlot.CustomLabel;
        var tempColor = sourceSlot.CustomColorHex;
        var tempDisabled = sourceSlot.IsDisabled;

        sourceSlot.ProductId = targetSlot.ProductId;
        sourceSlot.CustomLabel = targetSlot.CustomLabel;
        sourceSlot.CustomColorHex = targetSlot.CustomColorHex;
        sourceSlot.IsDisabled = targetSlot.IsDisabled;

        targetSlot.ProductId = tempProductId;
        targetSlot.CustomLabel = tempLabel;
        targetSlot.CustomColorHex = tempColor;
        targetSlot.IsDisabled = tempDisabled;

        layout.Version++;
        layout.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(cancellationToken);

        int totalPages = Math.Max(1, await _dbContext.GridLayouts.CountAsync(l => l.CategoryId == layout.CategoryId, cancellationToken));

        var refreshed = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .FirstAsync(l => l.Id == layout.Id, cancellationToken);

        return MapToDto(refreshed, totalPages);
    }

    public async Task<List<GridLayoutDto>> UpdateDimensionsAsync(UpdateGridDimensionsRequest request, CancellationToken cancellationToken = default)
    {
        int cols = Math.Clamp(request.ColumnsCount, 2, 8);
        int rows = Math.Clamp(request.RowsCount, 2, 8);

        List<GridLayout> layoutsToUpdate;
        if (request.ApplyToAllCategories)
        {
            layoutsToUpdate = await _dbContext.GridLayouts
                .Include(l => l.Slots)
                    .ThenInclude(s => s.Product)
                .ToListAsync(cancellationToken);
        }
        else
        {
            layoutsToUpdate = await _dbContext.GridLayouts
                .Include(l => l.Slots)
                    .ThenInclude(s => s.Product)
                .Where(l => l.CategoryId == request.CategoryId)
                .ToListAsync(cancellationToken);

            if (layoutsToUpdate.Count == 0 && !string.IsNullOrWhiteSpace(request.CategoryId))
            {
                var first = await CreateDefaultLayoutAsync(request.CategoryId, cancellationToken);
                layoutsToUpdate.Add(first);
            }
        }

        foreach (var layout in layoutsToUpdate)
        {
            layout.ColumnsCount = cols;
            layout.RowsCount = rows;
            layout.Version++;
            layout.UpdatedAtUtc = DateTimeOffset.UtcNow;

            int totalSlots = cols * rows;
            // Existing assigned slots ordered by slotIndex
            var assignedSlots = layout.Slots
                .Where(s => s.ProductId != null)
                .OrderBy(s => s.SlotIndex)
                .ToList();

            // Clear old slots
            _dbContext.GridSlots.RemoveRange(layout.Slots);
            layout.Slots.Clear();

            // Re-create totalSlots for the new geometry
            for (int i = 0; i < totalSlots; i++)
            {
                int r = i / cols;
                int c = i % cols;
                var assigned = i < assignedSlots.Count ? assignedSlots[i] : null;

                var newSlot = new GridSlot
                {
                    GridLayoutId = layout.Id,
                    RowIndex = r,
                    ColumnIndex = c,
                    SlotIndex = i,
                    ProductId = assigned?.ProductId,
                    CustomLabel = assigned?.CustomLabel,
                    CustomColorHex = assigned?.CustomColorHex
                };
                layout.Slots.Add(newSlot);
                _dbContext.GridSlots.Add(newSlot);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Reload to get properly mapped DTOs with products
        var layoutIds = layoutsToUpdate.Select(l => l.Id).ToList();
        var reloaded = await _dbContext.GridLayouts
            .Include(l => l.Slots)
                .ThenInclude(s => s.Product)
            .Where(l => layoutIds.Contains(l.Id))
            .ToListAsync(cancellationToken);

        var allCategoryLayouts = await _dbContext.GridLayouts
            .Select(l => new { l.Id, l.CategoryId })
            .ToListAsync(cancellationToken);

        var totalPagesMap = allCategoryLayouts
            .GroupBy(l => l.CategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        return reloaded.Select(l => MapToDto(l, totalPagesMap.GetValueOrDefault(l.CategoryId, 1))).ToList();
    }

    private async Task<GridLayout> CreateDefaultLayoutAsync(string categoryId, CancellationToken cancellationToken)
    {
        List<Product> products;
        if (categoryId == "ALL")
        {
            products = await _dbContext.Products
                .Where(p => p.IsActive)
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }
        else
        {
            products = await _dbContext.Products
                .Where(p => p.CategoryId == categoryId && p.IsActive)
                .OrderBy(p => p.DisplayOrder)
                .ThenBy(p => p.Name)
                .ToListAsync(cancellationToken);
        }

        var anyLayout = await _dbContext.GridLayouts.FirstOrDefaultAsync(cancellationToken);
        int cols = anyLayout?.ColumnsCount ?? 4;
        int rows = anyLayout?.RowsCount ?? 4;
        int itemsPerPage = cols * rows;
        int pageCount = Math.Max(1, (int)Math.Ceiling(products.Count / (double)itemsPerPage));
        GridLayout? firstLayout = null;

        for (int p = 0; p < pageCount; p++)
        {
            var layout = new GridLayout
            {
                CategoryId = categoryId,
                Name = categoryId == "ALL" ? "Tout le Menu" : "Défaut",
                ColumnsCount = cols,
                RowsCount = rows,
                PageIndex = p,
                Version = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            var pageProducts = products.Skip(p * itemsPerPage).Take(itemsPerPage).ToList();

            for (int i = 0; i < itemsPerPage; i++)
            {
                int row = i / layout.ColumnsCount;
                int col = i % layout.ColumnsCount;
                var prod = i < pageProducts.Count ? pageProducts[i] : null;

                layout.Slots.Add(new GridSlot
                {
                    GridLayoutId = layout.Id,
                    ProductId = prod?.Id,
                    Product = prod,
                    RowIndex = row,
                    ColumnIndex = col,
                    SlotIndex = i
                });
            }

            _dbContext.GridLayouts.Add(layout);
            if (p == 0) firstLayout = layout;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return firstLayout!;
    }

    private static GridLayoutDto MapToDto(GridLayout layout, int totalPages = 1)
    {
        return new GridLayoutDto(
            layout.Id,
            layout.CategoryId,
            layout.Name,
            layout.ColumnsCount,
            layout.RowsCount,
            layout.PageIndex,
            Math.Max(1, totalPages),
            layout.Version,
            layout.UpdatedAtUtc,
            layout.Slots
                .OrderBy(s => s.SlotIndex)
                .Select(s => new GridSlotDto(
                    s.Id,
                    s.GridLayoutId,
                    s.ProductId,
                    s.RowIndex,
                    s.ColumnIndex,
                    s.SlotIndex,
                    s.CustomLabel,
                    s.CustomColorHex,
                    s.IsDisabled,
                    s.Product == null ? null : new ProductSummaryDto(
                        s.Product.Id,
                        s.Product.Name,
                        s.Product.Price.AmountInCents / 100m,
                        s.Product.PreparationStationId,
                        s.Product.ColorHex
                    )
                )).ToList()
        );
    }
}
