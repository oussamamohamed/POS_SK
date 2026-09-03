using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (PinLoginRequest req, IOperatorAuthenticationService authService, IJwtTokenGeneratorService tokenGen) =>
        {
            var result = await authService.AuthenticatePinAsync(req.Pin);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Success = false, result.ErrorMessage });
            }
            
            var token = tokenGen.GenerateToken(result.OperatorId.GetValueOrDefault(), result.OperatorName ?? "Opérateur", result.Role.GetValueOrDefault());
            return Results.Ok(new { Success = true, result.OperatorId, result.OperatorName, Role = result.Role?.ToString(), Token = token });
        });
    }
}
