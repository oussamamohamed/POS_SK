using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Api.Services;

/// <summary>Résultat d'un encaissement TPE : <see cref="Failure"/> non nul = réponse à renvoyer, rien n'est écrit ; sinon <see cref="Reference"/> (null si aucun TPE).</summary>
public sealed record CardChargeOutcome(IResult? Failure, string? Reference = null);

/// <summary>
/// Envoie au TPE le montant des règlements par carte et attend la décision, avant toute écriture de reçu.
/// Sans TPE configuré (<c>CardTerminal:Provider</c> absent) : ne fait rien, la carte reste saisie à la main comme avant.
/// Un seul encaissement à la fois : un second appui pendant l'attente est refusé, jamais débité deux fois.
/// ponytail: un seul lecteur pour tout le restaurant ; un lecteur par appareil = clé de config par terminalId.
/// </summary>
public sealed partial class CardTerminalCharger : IDisposable
{
    private readonly ICardTerminal? _terminal;
    private readonly ILogger<CardTerminalCharger> _logger;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _busy = new(1, 1);

    public CardTerminalCharger(IConfiguration configuration, ILogger<CardTerminalCharger> logger, ICardTerminal? terminal = null)
    {
        _terminal = terminal;
        _logger = logger;
        _timeout = TimeSpan.FromSeconds(configuration.GetValue("CardTerminal:TimeoutSeconds", 60));
    }

    public async Task<CardChargeOutcome> ChargeAsync(IEnumerable<PaymentTenderRequest> tenders, Guid orderId, CancellationToken cancellationToken)
    {
        long amountCents = tenders.Where(t => t.Method == PaymentMethod.CreditCard).Sum(t => t.AmountInCents);
        if (_terminal is null || amountCents <= 0) return new CardChargeOutcome(null);

        if (!await _busy.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return Refused("errors.card_terminal_busy");
        }
        try
        {
            var reference = $"{orderId:N}-{Guid.NewGuid():N}";
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                var result = await _terminal.ChargeAsync(amountCents, reference, timeout.Token).ConfigureAwait(false);
                return result.Status switch
                {
                    CardChargeStatus.Approved => new CardChargeOutcome(null, result.Reference ?? reference),
                    CardChargeStatus.Declined => Refused("errors.card_declined"),
                    _ => Refused("errors.card_terminal_unavailable")
                };
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Refused("errors.card_timeout");
            }
        }
        finally
        {
            _busy.Release();
        }
    }

    public void Dispose() => _busy.Dispose();

    /// <summary>Carte débitée mais reçu non écrit (conflit résiduel) : à rembourser à la main depuis le tableau de bord du TPE.</summary>
    public void LogApprovedButNotRecorded(Guid orderId, string? reference) => LogUnrecorded(_logger, orderId, reference);

    private static CardChargeOutcome Refused(string key) =>
        new(Results.Json(new { code = key["errors.".Length..], message = Texts.T(key) }, statusCode: StatusCodes.Status402PaymentRequired));

    [LoggerMessage(EventId = 3031, Level = LogLevel.Error, Message = "Carte débitée sans reçu : commande {OrderId}, référence TPE {Reference}. Rembourser le client.")]
    private static partial void LogUnrecorded(ILogger logger, Guid orderId, string? reference);
}
