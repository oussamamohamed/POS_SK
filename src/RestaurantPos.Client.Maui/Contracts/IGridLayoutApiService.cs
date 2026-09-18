using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Client.Maui.Contracts;

public interface IGridLayoutApiService
{
    Task<GridLayoutDto?> GetLayoutByCategoryAsync(string categoryId, int pageIndex = 0);
    Task<GridLayoutDto> SaveLayoutAsync(UpdateGridLayoutRequest request);
}
