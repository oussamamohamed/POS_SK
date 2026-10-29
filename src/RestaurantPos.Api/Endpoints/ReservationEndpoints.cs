using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Endpoints;

public static class ReservationEndpoints
{
    public static void MapReservationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reservations")
                       .WithTags("Reservations")
                       .RequireAuthorization();

        group.MapGet("/", async (DateTimeOffset? from, DateTimeOffset? to, HttpContext http, IAuthorizationService authz, IReservationService service, TimeProvider clock, CancellationToken ct) =>
        {
            var start = from ?? new DateTimeOffset(clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            var end = to ?? start.AddDays(1);
            return Results.Ok(await service.ListAsync(start, end, await CanSeePhone(http, authz), ct));
        });

        group.MapPost("/", (ReservationRequest req, HttpContext http, IAuthorizationService authz, IReservationService service, IHubContext<PosHub, IPosHubClient> hub, CancellationToken ct) =>
            Run(http, authz, hub, async includePhone => Results.Created("/api/reservations", await service.CreateAsync(ToInput(req), includePhone, ct))));

        group.MapPut("/{id:guid}", (Guid id, ReservationRequest req, HttpContext http, IAuthorizationService authz, IReservationService service, IHubContext<PosHub, IPosHubClient> hub, CancellationToken ct) =>
            Run(http, authz, hub, async includePhone => Results.Ok(await service.UpdateAsync(id, ToInput(req), includePhone, ct))));

        group.MapPost("/{id:guid}/seat", (Guid id, SeatReservationRequest req, HttpContext http, IAuthorizationService authz, IReservationService service, IHubContext<PosHub, IPosHubClient> hub, CancellationToken ct) =>
            Run(http, authz, hub, async includePhone => Results.Ok(await service.SeatAsync(id, req.TableNumber, req.OperatorId ?? Guid.Empty, req.WaiterName ?? "Serveur", includePhone, ct))));

        group.MapPost("/{id:guid}/status", (Guid id, ReservationStatusRequest req, HttpContext http, IAuthorizationService authz, IReservationService service, IHubContext<PosHub, IPosHubClient> hub, CancellationToken ct) =>
            Run(http, authz, hub, async includePhone => Results.Ok(await service.SetStatusAsync(id, req.Status, includePhone, ct))));
    }

    private static ReservationInput ToInput(ReservationRequest r) =>
        new(r.GuestName, r.Covers, r.StartsAtUtc, r.DurationMinutes, r.TableNumber, r.Note, r.Phone, r.IsWaitlist);

    private static async Task<bool> CanSeePhone(HttpContext http, IAuthorizationService authz) =>
        (await authz.AuthorizeAsync(http.User, "RequireManagerOrAdmin")).Succeeded;

    /// <summary>Exécute une mutation, diffuse le changement et traduit les refus métier en 400/404/409 localisés.</summary>
    private static async Task<IResult> Run(HttpContext http, IAuthorizationService authz, IHubContext<PosHub, IPosHubClient> hub, Func<bool, Task<IResult>> action)
    {
        try
        {
            var result = await action(await CanSeePhone(http, authz));
            await hub.Clients.All.OnReservationsChanged();
            return result;
        }
        catch (ReservationException ex)
        {
            var body = new { Message = Texts.T("errors.reservation_" + ex.Error.ToString().ToLowerInvariant()) };
            return ex.Error switch
            {
                ReservationError.NotFound => Results.NotFound(body),
                ReservationError.TableConflict or ReservationError.TableOccupied or ReservationError.InvalidState => Results.Conflict(body),
                _ => Results.BadRequest(body)
            };
        }
    }
}
