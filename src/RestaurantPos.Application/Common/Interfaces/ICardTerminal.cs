using System.Threading;
using System.Threading.Tasks;

namespace RestaurantPos.Application.Common.Interfaces;

public enum CardChargeStatus
{
    Approved,
    Declined,
    /// <summary>Terminal injoignable, occupé ou mal configuré : rien n'a été débité.</summary>
    Unavailable
}

public sealed record CardChargeResult(CardChargeStatus Status, string? Reference = null, string? Reason = null);

/// <summary>
/// Terminal de paiement (TPE) piloté par le serveur : envoie le montant, attend la décision du client.
/// Un adaptateur par marque ; l'annulation du jeton (délai dépassé) doit annuler la transaction sur le terminal.
/// </summary>
public interface ICardTerminal
{
    Task<CardChargeResult> ChargeAsync(long amountCents, string reference, CancellationToken cancellationToken);
}
