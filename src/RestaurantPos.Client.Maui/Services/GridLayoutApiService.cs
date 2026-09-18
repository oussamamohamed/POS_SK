using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Client.Maui.Contracts;

namespace RestaurantPos.Client.Maui.Services;

public class GridLayoutApiService : IGridLayoutApiService
{
    // Pointing to the local API base URL for layouts
    private const string ApiBaseUrl = "http://127.0.0.1:5000/api/grid-layouts";

    public async Task<GridLayoutDto?> GetLayoutByCategoryAsync(string categoryId, int pageIndex = 0)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.GetAsync($"{ApiBaseUrl}/{categoryId}?page={pageIndex}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<GridLayoutDto>();
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching grid layout: {ex.Message}");
            return null;
        }
    }

    public async Task<GridLayoutDto> SaveLayoutAsync(UpdateGridLayoutRequest request)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var response = await client.PostAsJsonAsync(ApiBaseUrl, request);
            response.EnsureSuccessStatusCode();
            var dto = await response.Content.ReadFromJsonAsync<GridLayoutDto>();
            return dto ?? throw new InvalidOperationException("Returned layout is null.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving grid layout: {ex.Message}");
            throw;
        }
    }
}
