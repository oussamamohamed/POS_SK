using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using QRCoder;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices");

        group.MapPost("/pairing-codes", async (CreatePairingCodeRequest req, IDeviceService devices, HttpContext http, CancellationToken ct) =>
        {
            var name = req.Name?.Trim() ?? string.Empty;
            if (name.Length is 0 or > 64
                || !Enum.TryParse<DeviceRole>(req.Role, ignoreCase: true, out var role)
                || !Enum.IsDefined(role)
                || int.TryParse(req.Role, out _))
            {
                return Results.BadRequest(new { Message = "Nom (64 caractères max) et rôle (Caisse, Serveur, Cuisine, BackOffice) obligatoires." });
            }

            var operatorId = Guid.TryParse(http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : Guid.Empty;
            var created = await devices.CreatePairingCodeAsync(name, role, operatorId, ct);

            // Le gérant consulte le back-office depuis le réseau local : l'hôte vu ici est joignable par l'iPad.
            var serverUrl = $"{http.Request.Scheme}://{http.Request.Host}";
            var payload = $"posdevice://pair?url={Uri.EscapeDataString(serverUrl)}&code={created.Code}";
            var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, 8);

            return Results.Ok(new PairingCodeResponse(created.Code, created.ExpiresAtUtc, payload, Convert.ToBase64String(png)));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/pair", async (PairRequest req, IDeviceService devices, IPinRateLimiterService rateLimiter, IConfiguration config, HttpContext http, CancellationToken ct) =>
        {
            var clientKey = "pair:" + (http.Connection.RemoteIpAddress?.ToString() ?? "unknown-client");
            if (rateLimiter.IsLocked(clientKey))
            {
                var wait = Math.Ceiling(rateLimiter.GetRemainingLockout(clientKey).TotalSeconds);
                return Results.Json(
                    new { code = "rate_limited", message = $"Trop de tentatives. Patientez {wait} secondes." },
                    statusCode: StatusCodes.Status429TooManyRequests);
            }

            var paired = await devices.PairAsync(req.Code ?? string.Empty, ct);
            if (paired is null)
            {
                rateLimiter.RecordFailedAttempt(clientKey);
                return Results.BadRequest(new { code = "pairing_code_invalid", message = "Code invalide ou expiré" });
            }

            rateLimiter.ResetAttempts(clientKey);
            return Results.Ok(new PairResponse(paired.DeviceId, paired.Token, paired.TerminalId, paired.Name, paired.Role.ToString(), BonjourAdvertiserService.ServerName(config)));
        }).AllowAnonymous();

        group.MapGet("", async (IDeviceService devices, CancellationToken ct) =>
        {
            var list = await devices.ListAsync(ct);
            return Results.Ok(list.Select(d => new DeviceDto(d.Id, d.Name, d.Role.ToString(), d.TerminalId, d.PairedAtUtc, d.LastSeenUtc, d.RevokedAtUtc is not null)));
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/{id:guid}/revoke", async (Guid id, IDeviceService devices, CancellationToken ct) =>
            await devices.RevokeAsync(id, ct) ? Results.NoContent() : Results.NotFound())
            .RequireAuthorization("RequireManagerOrAdmin");
    }
}
