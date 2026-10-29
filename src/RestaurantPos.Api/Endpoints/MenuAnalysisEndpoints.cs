using System;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Api.Endpoints;

public static class MenuAnalysisEndpoints
{
    public static void MapMenuAnalysisEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/menu-analysis")
                       .WithTags("Menu Analysis")
                       .RequireAuthorization("RequireManagerOrAdmin");

        group.MapGet("/", async (
            DateTimeOffset? from,
            DateTimeOffset? to,
            IMenuAnalysisService service,
            CancellationToken ct) => Results.Ok(await service.GetMenuAnalysisAsync(from, to, ct)));
    }
}
