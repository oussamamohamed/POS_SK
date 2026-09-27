using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/settings").WithTags("Settings").RequireAuthorization();

        group.MapGet("/", async (IRestaurantSettingsService settings) => Results.Ok(await settings.GetAsync()));

        group.MapPut("/", async (UpdateRestaurantSettingsRequest req, IRestaurantSettingsService settings) =>
        {
            var updated = await settings.UpdateAsync(req);
            return updated is null
                ? Results.BadRequest(new { message = Texts.T("errors.receipt_language_invalid") })
                : Results.Ok(updated);
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}
