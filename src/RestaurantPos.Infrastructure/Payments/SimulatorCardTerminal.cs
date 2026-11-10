using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;

namespace RestaurantPos.Infrastructure.Payments;

/// <summary>TPE factice (démonstration, essais) : un montant dont les centimes valent 13 est refusé, tout autre est accepté.</summary>
public sealed class SimulatorCardTerminal : ICardTerminal
{
    public async Task<CardChargeResult> ChargeAsync(long amountCents, string reference, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken).ConfigureAwait(false);
        return amountCents % 100 == 13
            ? new CardChargeResult(CardChargeStatus.Declined, reference, "card_declined")
            : new CardChargeResult(CardChargeStatus.Approved, reference);
    }
}
