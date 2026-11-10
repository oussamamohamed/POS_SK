using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Api.Services;

namespace RestaurantPos.Api.Endpoints;

public static class BackupEndpoints
{
    public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/backup").WithTags("Backup").RequireAuthorization("RequireManagerOrAdmin");

        group.MapGet("/status", async (BackupService backup, CancellationToken ct) =>
            Results.Ok(await backup.GetStatusAsync(ct)));

        group.MapPost("/run", async (BackupService backup, CancellationToken ct) =>
        {
            if (!backup.IsConfigured)
            {
                return Results.Json(new { code = "backup_not_configured", message = "Aucune phrase de sauvegarde configurée sur le serveur (Backup__Passphrase)." }, statusCode: StatusCodes.Status409Conflict);
            }
            return Results.Ok(await backup.RunNowAsync(ct));
        });
    }
}
