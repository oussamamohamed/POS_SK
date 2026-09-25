using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Api.Endpoints;

/// <summary>
/// Exige un poste appairé sur les routes qui écrivent un ticket fiscal.
/// Le TerminalId vient du poste, jamais du corps de la requête.
/// </summary>
public sealed class RequireDeviceFilter : IEndpointFilter
{
    public const string HeaderName = "X-Device-Token";
    private const string ItemKey = "PairedDevice";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var devices = http.RequestServices.GetRequiredService<IDeviceService>();
        var device = await devices.AuthenticateAsync(http.Request.Headers[HeaderName].ToString(), http.RequestAborted);
        if (device is null)
        {
            return Results.Json(
                new { code = "device_not_paired", message = "Ce poste n'est pas appairé au serveur." },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        http.Items[ItemKey] = device;
        return await next(context);
    }

    public static Device PairedDevice(HttpContext http) => (Device)http.Items[ItemKey]!;
}

public static class RequireDeviceFilterExtensions
{
    public static RouteHandlerBuilder RequirePairedDevice(this RouteHandlerBuilder builder) =>
        builder.AddEndpointFilter<RequireDeviceFilter>();
}
