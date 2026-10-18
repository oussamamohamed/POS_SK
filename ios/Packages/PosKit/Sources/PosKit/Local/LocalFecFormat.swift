import Foundation

/// Compte du plan comptable, numéro et libellé tels que `FecExportService` les écrit.
struct FecAccount: Equatable {
    let number: String
    let label: String
}

/// Formats, comptes et en-tête du FEC : miroir de `FecExportService.cs` (journal « VT », 18 colonnes séparées par des tabulations,
/// montants `0.00` à point décimal, dates `yyyyMMdd` du jour UTC). Toute modification côté .NET se reporte ici et impose de régénérer
/// `fec_reference.json` (voir `CLAUDE.md`).
enum LocalFecFormat {
    static let header = [
        "JournalCode", "JournalLib", "EcritureNum", "EcritureDate", "CompteNum", "CompteLib", "CompAuxNum", "CompAuxLib", "PieceRef",
        "PieceDate", "EcritureLib", "Debit", "Credit", "EcritureLet", "DateLet", "ValidDate", "Montantdevise", "Idevise",
    ].joined(separator: "\t")

    /// `TipsAccount` du .NET : excédent de règlement (pourboire) au-delà du TTC du reçu, dette envers le personnel.
    static let tipsAccount = FecAccount(number: "468600", label: "Pourboires et excedents de reglement a reverser")
    /// Contrepartie d'un reçu sans règlement enregistré.
    static let cashFallbackAccount = FecAccount(number: "530000", label: "Caisse Especes")

    /// Le .NET formate `CreatedAtUtc` (décalage +00:00) : jour UTC, jamais le fuseau de l'iPad.
    private static let utcCalendar: Calendar = {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .gmt
        return calendar
    }()

    /// `(cents / 100m).ToString("0.00", InvariantCulture)`.
    static func amount(_ cents: Int) -> String {
        let magnitude = cents.magnitude
        let fraction = magnitude % 100
        return (cents < 0 ? "-" : "") + "\(magnitude / 100)." + (fraction < 10 ? "0" : "") + "\(fraction)"
    }

    /// `{rate:0.#}` d'un `decimal` .NET : une décimale au plus, milieu arrondi loin de zéro, pas de zéro final (« 10 », « 5.5 »).
    static func rateLabel(_ rate: Decimal) -> String {
        var scaled = rate * 10
        var rounded = Decimal()
        NSDecimalRound(&rounded, &scaled, 0, .plain)
        let tenths = NSDecimalNumber(decimal: rounded).intValue
        let magnitude = tenths.magnitude
        return (tenths < 0 ? "-" : "") + "\(magnitude / 10)" + (magnitude % 10 == 0 ? "" : ".\(magnitude % 10)")
    }

    private enum RateBand { case twenty, ten, fiveFive, other }

    /// Plages du `switch` .NET : [19,5 ; 20,5], [9,5 ; 10,5], [5,0 ; 6,0], puis le reste.
    private static func band(_ rate: Decimal) -> RateBand {
        let tenths = rate * 10
        if tenths >= 195 && tenths <= 205 { return .twenty }
        if tenths >= 95 && tenths <= 105 { return .ten }
        if tenths >= 50 && tenths <= 60 { return .fiveFive }
        return .other
    }

    static func salesAccount(rate: Decimal) -> FecAccount {
        switch band(rate) {
        case .twenty: FecAccount(number: "706200", label: "Ventes Boissons Alcoolisees 20%")
        case .ten: FecAccount(number: "706100", label: "Ventes Restauration Sur Place 10%")
        case .fiveFive: FecAccount(number: "706550", label: "Ventes Restauration A Emporter 5.5%")
        case .other: FecAccount(number: "706000", label: "Ventes Prestations Restauration")
        }
    }

    static func vatAccount(rate: Decimal) -> FecAccount {
        switch band(rate) {
        case .twenty: FecAccount(number: "445720", label: "TVA Collectee 20%")
        case .ten: FecAccount(number: "445710", label: "TVA Collectee 10%")
        case .fiveFive: FecAccount(number: "445755", label: "TVA Collectee 5.5%")
        case .other: FecAccount(number: "445700", label: "TVA Collectee")
        }
    }

    /// `GetPaymentAccount` ; `method` est la valeur entière stockée (`PaymentTenders.Method`).
    static func paymentAccount(method: Int) -> FecAccount {
        switch PaymentMethod(rawValue: method) {
        case .creditCard: FecAccount(number: "512000", label: "Banque Cartes Bancaires")
        case .cash: FecAccount(number: "530000", label: "Caisse Especes")
        case .mealVoucher: FecAccount(number: "580000", label: "Titres Restaurant a Encaisser")
        case .roomCharge: FecAccount(number: "411000", label: "Clients - Facturation Chambre")
        case .giftCard: FecAccount(number: "419100", label: "Clients - Cartes Cadeaux")
        case nil: FecAccount(number: "580000", label: "Virements Internes POS")
        }
    }

    /// `PaymentMethod.ToString()` du .NET ; une valeur hors énumération s'écrit en chiffres, comme en C#.
    static func methodName(_ method: Int) -> String {
        switch PaymentMethod(rawValue: method) {
        case .cash: "Cash"
        case .creditCard: "CreditCard"
        case .mealVoucher: "MealVoucher"
        case .giftCard: "GiftCard"
        case .roomCharge: "RoomCharge"
        case nil: "\(method)"
        }
    }

    static func date(_ date: Date) -> String {
        let parts = utcCalendar.dateComponents([.year, .month, .day], from: date)
        return String(format: "%04d%02d%02d", parts.year ?? 0, parts.month ?? 0, parts.day ?? 0)
    }

    /// `{SIREN rogné, ou 000000000}FEC{fin}.txt` ; `end` est la borne la plus tardive (après inversion éventuelle).
    static func fileName(siren: String, end: Date) -> String {
        let trimmed = siren.trimmingCharacters(in: .whitespacesAndNewlines)
        return (trimmed.isEmpty ? "000000000" : trimmed) + "FEC" + date(end) + ".txt"
    }

    /// `ToString("D7")`.
    static func entryNumber(_ number: Int) -> String {
        let digits = String(number)
        return String(repeating: "0", count: max(0, 7 - digits.count)) + digits
    }

    /// `FormatFecLine` : journal des ventes, date de pièce et de validation = date d'écriture, colonnes auxiliaires vides, devise EUR.
    static func line(entry: String, date: String, account: FecAccount, pieceRef: String, label: String, debit: Int, credit: Int) -> String {
        [
            "VT", "Journal des Ventes", entry, date, account.number, account.label, "", "", pieceRef, date, label,
            amount(debit), amount(credit), "", "", date, "", "EUR",
        ].joined(separator: "\t")
    }
}
