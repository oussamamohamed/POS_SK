using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IGridManagementService
{
    Task<List<GridLayoutDto>> GetAllLayoutsAsync(CancellationToken cancellationToken = default);
    Task<GridLayoutDto?> GetLayoutByCategoryAsync(string categoryId, int pageIndex = 0, CancellationToken cancellationToken = default);
    Task<List<GridLayoutDto>> GetAllPagesByCategoryAsync(string categoryId, CancellationToken cancellationToken = default);
    Task<GridLayoutDto> SaveLayoutAsync(UpdateGridLayoutRequest request, CancellationToken cancellationToken = default);
    Task<GridLayoutDto?> SwapSlotsAsync(SwapGridSlotsRequest request, CancellationToken cancellationToken = default);
    Task<List<GridLayoutDto>> UpdateDimensionsAsync(UpdateGridDimensionsRequest request, CancellationToken cancellationToken = default);
}
