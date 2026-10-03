using System;
using System.Security.Claims;
using System.Threading;
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

        group.MapGet("/", async (IRestaurantSettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetAsync(ct)));

        group.MapPut("/", async (
            UpdateRestaurantSettingsRequest req,
            IRestaurantSettingsService settings,
            ClaimsPrincipal user,
            CancellationToken ct) =>
        {
            Guid? operatorId = null;
            if (Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var parsed))
            {
                operatorId = parsed;
            }

            var result = await settings.UpdateAsync(req, operatorId, ct);
            return result.Status switch
            {
                UpdateSettingsStatus.Success => Results.Ok(result.Settings),
                UpdateSettingsStatus.InvalidLanguage => Results.BadRequest(new { message = Texts.T("errors.receipt_language_invalid") }),
                UpdateSettingsStatus.InvalidSiret => Results.BadRequest(new { message = Texts.T("errors.siret_invalid") }),
                UpdateSettingsStatus.InvalidVat => Results.BadRequest(new { message = Texts.T("errors.vat_number_invalid") }),
                UpdateSettingsStatus.InvalidFiscalDate => Results.BadRequest(new { message = Texts.T("errors.fiscal_date_invalid") }),
                UpdateSettingsStatus.FiscalYearLocked => Results.Json(new { code = "fiscal_year_locked", message = Texts.T("errors.fiscal_year_locked") }, statusCode: StatusCodes.Status409Conflict),
                _ => Results.BadRequest()
            };
        }).RequireAuthorization("RequireManagerOrAdmin");
    }
}

