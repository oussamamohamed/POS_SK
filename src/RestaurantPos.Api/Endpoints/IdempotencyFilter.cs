using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

/// <summary>
/// Route d'encaissement rejouable : avec un en-tête <c>Idempotency-Key</c> (UUID), la même clé et la même demande rendent la réponse de
/// succès déjà enregistrée, sans reçu, débit TPE ni impression de plus. Autre demande avec la même clé : 409. Sans en-tête : inchangé.
/// À placer après <see cref="RequireDeviceFilter"/> (le poste fait partie de la demande). Un verrou par clé fait attendre une requête
/// identique en cours, que le client a abandonnée sur délai : elle relit alors la réponse gardée au lieu d'encaisser une seconde fois.
/// </summary>
public sealed class IdempotencyFilter<TRequest>(IServiceScopeFactory scopes, IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions> json, ILogger<IdempotencyFilter<TRequest>> logger) : IEndpointFilter
{
    // Un seul processus sert le restaurant : un verrou mémoire suffit. Dénombré pour ne jamais retirer un verrou encore attendu.
    private static readonly Dictionary<Guid, (SemaphoreSlim Gate, int Users)> Gates = [];

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var header = http.Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(header)) return await next(context);
        if (!Guid.TryParse(header, out var key))
            return Results.BadRequest(new { Success = false, Code = "idempotency_key_invalid", Message = Texts.T("errors.idempotency_key_invalid") });

        var body = JsonSerializer.Serialize(context.Arguments.OfType<TRequest>().First(), json.Value.SerializerOptions);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{http.Request.Path}|{RequireDeviceFilter.PairedDevice(http).TerminalId}|{body}")));

        var gate = Enter(key);
        try
        {
            await gate.WaitAsync(http.RequestAborted);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var done = await db.IdempotentRequests.AsNoTracking().FirstOrDefaultAsync(r => r.IdempotencyKey == key, http.RequestAborted);
                if (done is not null)
                {
                    return done.RequestHash == hash
                        ? Results.Content(done.ResponseJson, "application/json")
                        : Results.Conflict(new { Success = false, Code = "idempotency_key_reuse", Message = Texts.T("errors.idempotency_key_reuse") });
                }

                var result = await next(context);
                if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK } and IValueHttpResult { Value: { } value })
                    await RememberAsync(db, key, hash, JsonSerializer.Serialize(value, json.Value.SerializerOptions));
                return result;
            }
            finally { gate.Release(); }
        }
        finally { Leave(key); }
    }

    // Le paiement est déjà enregistré : perdre la clé ne fait que perdre la protection au rejeu, jamais échouer la vente.
    private async Task RememberAsync(AppDbContext db, Guid key, string hash, string responseJson)
    {
        try
        {
            var cutoff = DateTime.UtcNow.AddDays(-IdempotentRequest.RetentionDays);
            db.IdempotentRequests.RemoveRange(await db.IdempotentRequests.Where(r => r.CreatedAtUtc < cutoff).ToListAsync(CancellationToken.None));
            db.IdempotentRequests.Add(new IdempotentRequest { IdempotencyKey = key, RequestHash = hash, ResponseJson = responseJson });
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            IdempotencyLogMessages.NotStored(logger, key, ex);
        }
    }

    private static SemaphoreSlim Enter(Guid key)
    {
        lock (Gates)
        {
            var (gate, users) = Gates.TryGetValue(key, out var e) ? e : (new SemaphoreSlim(1, 1), 0);
            Gates[key] = (gate, users + 1);
            return gate;
        }
    }

    private static void Leave(Guid key)
    {
        lock (Gates)
        {
            var (gate, users) = Gates[key];
            if (users == 1) Gates.Remove(key); else Gates[key] = (gate, users - 1);
        }
    }
}

internal static partial class IdempotencyLogMessages
{
    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "Idempotency key {Key} could not be stored; the payment itself is recorded but a retry would not be recognised.")]
    public static partial void NotStored(ILogger logger, Guid key, Exception exception);
}
