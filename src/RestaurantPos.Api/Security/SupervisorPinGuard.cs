using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Security;

public sealed record SupervisorPinResult(bool Allowed, IResult? Rejection, Guid? OperatorId, string? OperatorName, UserRole? Role);

/// <summary>Seul point de validation d'un PIN superviseur (FloorManager ou Admin), limité en débit par IP.</summary>
public static class SupervisorPinGuard
{
    public static async Task<SupervisorPinResult> CheckAsync(
        string? pin,
        HttpContext ctx,
        IOperatorAuthenticationService auth,
        IPinRateLimiterService limiter,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pin))
        {
            return Reject(Results.BadRequest(new { Message = Texts.T("errors.supervisor_pin_required") }));
        }

        var key = "supervisor:" + (ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown-client");
        if (limiter.IsLocked(key))
        {
            var seconds = Math.Ceiling(limiter.GetRemainingLockout(key).TotalSeconds);
            return Reject(Results.Json(
                new { Message = Texts.T("errors.supervisor_rate_limited", ("seconds", seconds)) },
                statusCode: StatusCodes.Status429TooManyRequests));
        }

        var result = await auth.AuthenticatePinAsync(pin, ct);
        if (!result.IsSuccess || result.Role is not (UserRole.FloorManager or UserRole.Admin))
        {
            limiter.RecordFailedAttempt(key);
            return Reject(Results.Json(
                new { Message = Texts.T("errors.authorization_insufficient") },
                statusCode: StatusCodes.Status403Forbidden));
        }

        limiter.ResetAttempts(key);
        return new SupervisorPinResult(true, null, result.OperatorId, result.OperatorName, result.Role);
    }

    private static SupervisorPinResult Reject(IResult rejection) => new(false, rejection, null, null, null);
}
