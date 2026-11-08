using Microsoft.Extensions.Logging;
using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using RestaurantPos.Api.Hubs;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Api.Endpoints;

public sealed record OpenCashSessionRequest(long OpeningFloatCents);

public sealed record CashMovementRequest(string Kind, long AmountCents, string Reason);

public sealed record CashDenomination(long ValueCents, int Quantity);

public sealed record CashCountRequest(long CountedCents, CashDenomination[]? Denominations, string? VarianceNote);

public sealed record AlertNoteRequest(string Note);

public sealed record AlertRuleRequest(long Threshold, bool Enabled);

/// <summary>Après un règlement ou une annulation : évalue les seuils d'alerte. Une panne ne remet jamais en cause l'opération déjà enregistrée.</summary>
public sealed class AlertEvaluationFilter(IAlertService alerts, IHubContext<PosHub, IPosHubClient> hub, ILogger<AlertEvaluationFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var result = await next(context);
        try
        {
            await alerts.EvaluateAsync();
            if (alerts.TakeChanged()) await hub.Clients.All.OnAlertsChanged();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AlertLogMessages.EvaluationFailed(logger, ex); } // jamais propagé : la vente est déjà enregistrée
        return result;
    }
}

internal static partial class AlertLogMessages
{
    [LoggerMessage(EventId = 2001, Level = LogLevel.Error, Message = "Alert evaluation failed; the sale itself is saved but no alert was raised.")]
    public static partial void EvaluationFailed(ILogger logger, Exception exception);
}

public static class CashEndpoints
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static Guid Operator(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value, out var id) ? id : Guid.Empty;

    private static async Task<bool> IsManagerAsync(HttpContext http, IAuthorizationService authz) => (await authz.AuthorizeAsync(http.User, "RequireManagerOrAdmin")).Succeeded;

    private static IResult Refuse(CashException ex) => ex.Code is "cash_session_already_open" or "cash_session_closed"
        ? Results.Conflict(new { Code = ex.Code }) : Results.BadRequest(new { Code = ex.Code });

    public static void MapCashEndpoints(this IEndpointRouteBuilder app)
    {
        var cash = app.MapGroup("/api/cash").WithTags("Cash").RequireAuthorization();

        cash.MapPost("/sessions", async (OpenCashSessionRequest req, ICashSessionService svc, HttpContext http, ClaimsPrincipal user) =>
        {
            try { var s = await svc.OpenAsync(RequireDeviceFilter.PairedDevice(http).TerminalId, Operator(user), req.OpeningFloatCents); return Results.Created($"/api/cash/sessions/{s.Id}", s); }
            catch (CashException ex) { return Refuse(ex); }
        }).RequirePairedDevice();

        cash.MapGet("/sessions/current", async (ICashSessionService svc, HttpContext http, ClaimsPrincipal user, IAuthorizationService authz) =>
        {
            var session = await svc.CurrentAsync(RequireDeviceFilter.PairedDevice(http).TerminalId);
            if (session is null) return Results.NoContent();
            // Lisible par l'opérateur qui l'a ouverte et par le responsable : un serveur ne voit pas la session d'un autre.
            return session.OpenedByOperatorId == Operator(user) || await IsManagerAsync(http, authz) ? Results.Ok(session) : Results.Forbid();
        }).RequirePairedDevice();

        cash.MapPost("/sessions/{id:guid}/movements", async (Guid id, CashMovementRequest req, ICashSessionService svc, HttpContext http, ClaimsPrincipal user, IAuthorizationService authz) =>
        {
            var session = await svc.GetAsync(id);
            if (session is null) return Results.NotFound();
            if (session.TerminalId != RequireDeviceFilter.PairedDevice(http).TerminalId || (session.OpenedByOperatorId != Operator(user) && !await IsManagerAsync(http, authz))) return Results.Forbid();
            try { var m = await svc.AddMovementAsync(id, req.Kind, req.AmountCents, req.Reason, Operator(user)); return m is null ? Results.NotFound() : Results.Created($"/api/cash/sessions/{id}", m); }
            catch (CashException ex) { return Refuse(ex); }
        }).RequirePairedDevice();

        cash.MapPost("/sessions/{id:guid}/count", async (Guid id, CashCountRequest req, ICashSessionService svc, IAlertService alerts, IHubContext<PosHub, IPosHubClient> hub, HttpContext http, ClaimsPrincipal user, IAuthorizationService authz) =>
        {
            var session = await svc.GetAsync(id);
            if (session is null) return Results.NotFound();
            if (session.TerminalId != RequireDeviceFilter.PairedDevice(http).TerminalId || (session.OpenedByOperatorId != Operator(user) && !await IsManagerAsync(http, authz))) return Results.Forbid();
            try
            {
                var denominations = req.Denominations is { Length: > 0 } d ? JsonSerializer.Serialize(d, Web) : null;
                var result = await svc.CountAsync(id, req.CountedCents, denominations, req.VarianceNote, Operator(user));
                if (alerts.TakeChanged()) await hub.Clients.All.OnAlertsChanged();
                return result is null ? Results.NotFound() : Results.Ok(result);
            }
            catch (CashException ex) { return Refuse(ex); }
        }).RequirePairedDevice();

        cash.MapGet("/sessions", async (DateTimeOffset? from, DateTimeOffset? to, ICashSessionService svc) => Results.Ok(await svc.ListAsync(from, to)))
            .RequireAuthorization("RequireManagerOrAdmin");
    }

    public static void MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alerts").WithTags("Alerts").RequireAuthorization("RequireManagerOrAdmin");

        group.MapGet("/", async (string? state, DateTimeOffset? from, DateTimeOffset? to, IAlertService svc) =>
            state is not (null or Domain.Entities.Alert.Open or Domain.Entities.Alert.Resolved) ? Results.BadRequest(new { Code = "alert_rule_invalid" }) : Results.Ok(await svc.ListAsync(state, from, to)));

        group.MapPost("/{id:guid}/resolve", async (Guid id, AlertNoteRequest req, IAlertService svc, IHubContext<PosHub, IPosHubClient> hub, ClaimsPrincipal user) =>
        {
            try
            {
                var alert = await svc.ResolveAsync(id, req.Note, Operator(user));
                if (svc.TakeChanged()) await hub.Clients.All.OnAlertsChanged();
                return alert is null ? Results.NotFound() : Results.Ok(alert);
            }
            catch (AlertException ex) { return Results.BadRequest(new { Code = ex.Code }); }
        });

        group.MapGet("/rules", async (IAlertService svc) => Results.Ok(await svc.RulesAsync()));

        group.MapPut("/rules/{type}", async (string type, AlertRuleRequest req, IAlertService svc, ClaimsPrincipal user) =>
        {
            try { return await svc.SetRuleAsync(type, req.Threshold, req.Enabled, Operator(user)) is { } rule ? Results.Ok(rule) : Results.NotFound(); }
            catch (AlertException ex) { return Results.BadRequest(new { Code = ex.Code }); }
        });

        // Réglages de la caisse et des tendances : hors /api/settings, dont le corps est signé dans le journal fiscal.
        group.MapGet("/settings", async (IAlertService svc) => Results.Ok(await svc.GetSettingsAsync()));

        group.MapPut("/settings", async (AlertSettings req, IAlertService svc) =>
        {
            try { return Results.Ok(await svc.UpdateSettingsAsync(req)); }
            catch (AlertException ex) { return Results.BadRequest(new { Code = ex.Code }); }
        });
    }
}
