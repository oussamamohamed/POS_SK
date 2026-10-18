import Foundation

/// Ventilation d'un reçu à un taux. Valeurs absolues (le sens vient de l'annulation, à l'écriture), sauf le HT de l'absorbeur d'un
/// reçu historique hors règle, qui peut être négatif : l'écriture en prend la valeur absolue, comme le .NET.
struct FecVatItem: Equatable {
    let rate: Decimal
    let htCents: Int
    let vatCents: Int
}

/// HT cohérents avec une TVA à un taux (`[lo ; hi]`) et le plus proche de TVA / taux (`est`).
struct FecHtRange: Equatable {
    let lo: Int
    let hi: Int
    let est: Int
}

/// Miroir de `ParseVatBreakdown`, `ParseExactBreakdown`, `IsCoherent`, `ParseByRate` et `FeasibleHt` (`FecExportService.cs`).
/// Reçu récent : `HtBreakdownJson` (colonne non signée) donne le HT exact par taux s'il colle au reçu signé. Sinon (reçu historique,
/// colonne incohérente ou illisible) le HT est déduit de la TVA ; un taux 0 % ou, à défaut, le plus petit taux absorbe le reste.
/// L'ordre des taux est celui du JSON : le .NET énumère ses dictionnaires dans l'ordre d'insertion.
enum LocalFecBreakdown {
    private typealias RateCents = (rate: Decimal, cents: Int)

    static func items(ttcCents: Int, htCents: Int, taxBreakdownJson: String, htBreakdownJson: String?) -> [FecVatItem] {
        if let exact = exact(ttcCents: ttcCents, taxJson: taxBreakdownJson, htJson: htBreakdownJson) { return exact }
        let totalHt = abs(htCents)
        let totalVat = abs(ttcCents) - totalHt
        if !isBlank(taxBreakdownJson), taxBreakdownJson != "{}", let entries = FiscalJSON.parseIntObject(taxBreakdownJson) {
            // Une clé qui n'est pas un taux est ignorée (`decimal.TryParse` échoue), le reste est gardé.
            let parsed: [RateCents] = entries.compactMap { entry in rate(entry.0).map { (rate: $0, cents: abs(entry.1)) } }
            if !parsed.isEmpty { return deduced(parsed, totalHt: totalHt) }
        }
        return [FecVatItem(rate: 10, htCents: totalHt, vatCents: totalVat)]
    }

    /// HT possibles pour une TVA donnée (TVA = TTC − HT, HT = round(TTC / (1 + t))). Plage vide : estimation seule.
    static func feasibleHt(rate: Decimal, vatCents: Int) -> FecHtRange {
        let estimate = Money.roundAwayFromZero(Decimal(vatCents) / (rate / 100))
        var quotient = 100 / rate
        var ceiling = Decimal()
        NSDecimalRound(&ceiling, &quotient, 0, .up)
        let window = NSDecimalNumber(decimal: ceiling).intValue + 2
        var feasible: [Int] = []
        for base in max(0, estimate - window)...(estimate + window) {
            let parts = OrderMath.split(ttc: Money(cents: base + vatCents), ratePercent: rate)
            if parts.ht.cents == base && parts.vat.cents == vatCents { feasible.append(base) }
        }
        guard let first = feasible.first, let last = feasible.last else { return FecHtRange(lo: estimate, hi: estimate, est: estimate) }
        var closest = first // à égalité, le plus petit (`OrderBy` stable)
        for base in feasible where abs(base - estimate) < abs(closest - estimate) { closest = base }
        return FecHtRange(lo: first, hi: last, est: closest)
    }

    // MARK: HT exact (HtBreakdownJson)

    private static func exact(ttcCents: Int, taxJson: String, htJson: String?) -> [FecVatItem]? {
        guard let htJson, !isBlank(htJson), let ht = byRate(htJson), !ht.isEmpty else { return nil }
        let vat: [RateCents]
        if isBlank(taxJson) {
            vat = []
        } else {
            guard let parsed = byRate(taxJson) else { return nil }
            vat = parsed
        }
        guard coherent(ttcCents: ttcCents, ht: ht, vat: vat) else { return nil }
        // Mêmes taux des deux côtés (`coherent` l'impose) : la branche .NET « TVA sans HT » ne peut pas servir.
        return ht.map { h in FecVatItem(rate: h.rate, htCents: abs(h.cents), vatCents: abs(cents(at: h.rate, in: vat))) }
    }

    /// La colonne n'est pas signée : on ne la croit que si elle colle au reçu signé (mêmes taux, Σ|HT| = |TTC| − Σ|TVA|,
    /// et chaque HT à taux > 0 dans la plage cohérente avec sa TVA).
    private static func coherent(ttcCents: Int, ht: [RateCents], vat: [RateCents]) -> Bool {
        let sameRates = ht.allSatisfy { h in vat.contains { $0.rate == h.rate } } && vat.allSatisfy { v in ht.contains { $0.rate == v.rate } }
        let htSum = ht.reduce(0) { $0 + abs($1.cents) }
        let vatSum = vat.reduce(0) { $0 + abs($1.cents) }
        guard sameRates, htSum == abs(ttcCents) - vatSum else { return false }
        return ht.allSatisfy { h in
            guard h.rate > 0 else { return true }
            let range = feasibleHt(rate: h.rate, vatCents: abs(cents(at: h.rate, in: vat)))
            return (range.lo...range.hi).contains(abs(h.cents))
        }
    }

    /// `ParseByRate` : taux decimal → centimes, doublons (« 10 » et « 10.0 ») additionnés à la place de la première clé.
    /// `nil` si le JSON est illisible ou si une clé n'est pas un taux.
    private static func byRate(_ json: String) -> [RateCents]? {
        guard let entries = FiscalJSON.parseIntObject(json) else { return nil }
        var result: [RateCents] = []
        for (key, value) in entries {
            guard let rate = rate(key) else { return nil }
            if let index = result.firstIndex(where: { $0.rate == rate }) {
                result[index].cents += value
            } else {
                result.append((rate: rate, cents: value))
            }
        }
        return result
    }

    // MARK: HT déduit de la TVA (reçus historiques)

    private static func deduced(_ parsed: [RateCents], totalHt: Int) -> [FecVatItem] {
        let absorber = parsed.firstIndex { $0.rate == 0 } ?? parsed.indices.min { parsed[$0].rate < parsed[$1].rate } ?? 0
        let ranges = parsed.map { $0.rate > 0 ? feasibleHt(rate: $0.rate, vatCents: $0.cents) : FecHtRange(lo: 0, hi: 0, est: 0) }
        var ht = ranges.map(\.est)
        ht[absorber] = 0
        var residual = totalHt - ht.reduce(0, +)
        // Reste négatif (estimations trop hautes) : repris sur les taux les plus bas d'abord, dans leur plage cohérente (tri stable).
        let order = parsed.indices.filter { $0 != absorber }.sorted {
            parsed[$0].rate != parsed[$1].rate ? parsed[$0].rate < parsed[$1].rate : $0 < $1
        }
        for index in order where residual < 0 {
            let shift = min(-residual, ht[index] - ranges[index].lo)
            ht[index] -= shift
            residual += shift
        }
        ht[absorber] = residual
        return parsed.indices.map { FecVatItem(rate: parsed[$0].rate, htCents: ht[$0], vatCents: parsed[$0].cents) }
    }

    // MARK: Outils

    private static func cents(at rate: Decimal, in list: [RateCents]) -> Int {
        list.first { $0.rate == rate }?.cents ?? 0
    }

    private static func isBlank(_ text: String) -> Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    /// Clé de taux (« 10 », « 5.5 », « 10.0 »).
    /// ponytail: nombres décimaux simples seulement ; `decimal.TryParse(NumberStyles.Any)` accepte aussi « 1e1 », « +5 » ou des
    /// séparateurs de milliers, que ni le .NET ni l'iPad n'écrivent. Élargir ici si une clé de cette forme apparaît.
    private static func rate(_ key: String) -> Decimal? {
        let text = key.trimmingCharacters(in: .whitespaces)
        guard text.range(of: #"^-?[0-9]+(\.[0-9]+)?$"#, options: .regularExpression) != nil else { return nil }
        return Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))
    }
}
