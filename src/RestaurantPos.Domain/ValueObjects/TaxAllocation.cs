using System.Numerics;

namespace RestaurantPos.Domain.ValueObjects;

/// <summary>
/// Répartit un montant en centimes au prorata de poids, par la méthode du plus fort reste : somme des parts exacte,
/// jamais de part négative ni supérieure à son poids, résultat déterministe (restes égaux départagés par l'ordre des groupes).
/// Les poids doivent être positifs ou nuls.
/// </summary>
public static class TaxAllocation
{
    public static long[] Allocate(long total, IReadOnlyList<long> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        var shares = new long[weights.Count];
        if (weights.Count == 0 || total <= 0)
        {
            return shares;
        }

        long sum = 0;
        foreach (long weight in weights)
        {
            sum += weight;
        }

        if (sum <= 0)
        {
            return shares;
        }

        if (total >= sum)
        {
            for (int i = 0; i < weights.Count; i++)
            {
                shares[i] = weights[i];
            }

            return shares;
        }

        var remainders = new (int Index, BigInteger Remainder)[weights.Count];
        long allocated = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            BigInteger product = (BigInteger)total * weights[i];
            shares[i] = (long)(product / sum);
            remainders[i] = (i, product % sum);
            allocated += shares[i];
        }

        long left = total - allocated;
        foreach (var (index, _) in remainders.OrderByDescending(r => r.Remainder).ThenBy(r => r.Index).Take((int)left))
        {
            shares[index]++;
        }

        return shares;
    }
}
