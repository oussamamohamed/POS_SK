using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Security;

public sealed record SupervisorPinResult(bool Allowed, IResult? Rejection, Guid? OperatorId, string? OperatorName, UserRole? Role);

/// <summary>Seul point de validation d'un PIN superviseur (FloorManager ou Admin), limité en débit par IP.</summary>
public static partial class SupervisorPinGuard
{
    [LoggerMessage(EventId = 1010, Level = LogLevel.Warning, Message = "Supervisor PIN rejected for {ClientKey}: {Reason}.")]
    private static partial void Rejected(ILogger logger, string clientKey, string reason);

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
        var logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(SupervisorPinGuard).FullName!);

        // Sérialise verrou -> PBKDF2 -> échec/reset par clé : sinon une rafale parallèle dépasse la limite.
        // Borne aussi le coût CPU PBKDF2 d'une même IP.
        return await KeyedGate.RunAsync(key, async () =>
        {
            if (limiter.IsLocked(key))
            {
                Rejected(logger, key, "verrou");
                var seconds = Math.Ceiling(limiter.GetRemainingLockout(key).TotalSeconds);
                return Reject(Results.Json(
                    new { Message = Texts.T("errors.supervisor_rate_limited", ("seconds", seconds)) },
                    statusCode: StatusCodes.Status429TooManyRequests));
            }

            var result = await auth.AuthenticatePinAsync(pin, ct);
            if (!result.IsSuccess || result.Role is not (UserRole.FloorManager or UserRole.Admin))
            {
                limiter.RecordFailedAttempt(key);
                Rejected(logger, key, result.IsSuccess ? "rôle insuffisant" : "PIN invalide");
                return Reject(Results.Json(
                    new { Message = Texts.T("errors.authorization_insufficient") },
                    statusCode: StatusCodes.Status403Forbidden));
            }

            limiter.ResetAttempts(key);
            return new SupervisorPinResult(true, null, result.OperatorId, result.OperatorName, result.Role);
        }, ct);
    }

    private static SupervisorPinResult Reject(IResult rejection) => new(false, rejection, null, null, null);
}
