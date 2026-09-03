using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Authentication");

        group.MapPost("/login", async (
            PinLoginRequest req,
            IOperatorAuthenticationService authService,
            IJwtTokenGeneratorService tokenGen,
            IPinRateLimiterService rateLimiter,
            HttpContext context,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("RestaurantPos.Api.Endpoints.AuthEndpoints");
            var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";

            if (rateLimiter.IsLocked(clientKey))
            {
                var remaining = rateLimiter.GetRemainingLockout(clientKey);
                AuthLogMessages.LoginRateLimited(logger, clientKey, Math.Ceiling(remaining.TotalSeconds));
                return Results.Json(new { Success = false, ErrorMessage = $"Trop de tentatives infructueuses. Veuillez patienter {Math.Ceiling(remaining.TotalSeconds)} secondes." }, statusCode: StatusCodes.Status429TooManyRequests);
            }

            var result = await authService.AuthenticatePinAsync(req.Pin);
            if (!result.IsSuccess)
            {
                rateLimiter.RecordFailedAttempt(clientKey);
                AuthLogMessages.LoginFailed(logger, clientKey);
                return Results.BadRequest(new { Success = false, result.ErrorMessage });
            }

            rateLimiter.ResetAttempts(clientKey);
            var role = result.Role.GetValueOrDefault();
            var token = tokenGen.GenerateToken(result.OperatorId.GetValueOrDefault(), result.OperatorName ?? "Opérateur", role);

            AuthLogMessages.LoginSucceeded(logger, result.OperatorName ?? "Opérateur", role, clientKey);

            context.Response.Cookies.Append("pos_jwt_token", token, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = false,
                Expires = DateTimeOffset.UtcNow.AddHours(12)
            });

            return Results.Ok(new
            {
                Success = true,
                result.OperatorId,
                result.OperatorName,
                Role = role.ToString(),
                Token = token
            });
        }).AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) =>
        {
            if (!user.Identity?.IsAuthenticated ?? true)
            {
                return Results.Unauthorized();
            }

            var sub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            var name = user.FindFirst(ClaimTypes.Name)?.Value;
            var role = user.FindFirst(ClaimTypes.Role)?.Value;

            return Results.Ok(new
            {
                OperatorId = sub,
                OperatorName = name,
                Role = role
            });
        }).RequireAuthorization();

        group.MapPost("/override", async (
            SupervisorOverrideRequest req,
            IOperatorAuthenticationService authService,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("RestaurantPos.Api.Endpoints.AuthEndpoints");

            var result = await authService.AuthenticatePinAsync(req.SupervisorPin);
            if (!result.IsSuccess)
            {
                AuthLogMessages.SupervisorOverrideInvalidPin(logger);
                return Results.Json(new { Authorized = false, Message = "Code PIN superviseur invalide." }, statusCode: StatusCodes.Status403Forbidden);
            }

            var role = result.Role.GetValueOrDefault();
            if (role != UserRole.FloorManager && role != UserRole.Admin)
            {
                AuthLogMessages.SupervisorOverrideInsufficientRole(logger, result.OperatorName ?? "Inconnu", role);
                return Results.Json(new { Authorized = false, Message = "Privilèges superviseur insuffisants." }, statusCode: StatusCodes.Status403Forbidden);
            }

            AuthLogMessages.SupervisorOverrideGranted(logger, result.OperatorName ?? "Inconnu", role, req.Action);

            return Results.Ok(new
            {
                Authorized = true,
                SupervisorId = result.OperatorId,
                SupervisorName = result.OperatorName,
                Role = role.ToString(),
                Action = req.Action
            });
        }).RequireAuthorization();
    }
}

internal static partial class AuthLogMessages
{
    [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Login attempt blocked by rate limiter for {ClientKey}. Remaining: {RemainingSeconds}s")]
    public static partial void LoginRateLimited(ILogger logger, string clientKey, double remainingSeconds);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Failed operator login attempt from {ClientKey}.")]
    public static partial void LoginFailed(ILogger logger, string clientKey);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Successful operator login: {OperatorName} (Role: {Role}) from {ClientKey}")]
    public static partial void LoginSucceeded(ILogger logger, string operatorName, UserRole role, string clientKey);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Warning, Message = "Supervisor override rejected: invalid PIN.")]
    public static partial void SupervisorOverrideInvalidPin(ILogger logger);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Supervisor override rejected: Operator {Name} has role {Role}, required FloorManager or Admin.")]
    public static partial void SupervisorOverrideInsufficientRole(ILogger logger, string name, UserRole role);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Information, Message = "Supervisor override granted by {Name} (Role: {Role}) for action: {Action}")]
    public static partial void SupervisorOverrideGranted(ILogger logger, string name, UserRole role, string action);
}
