import Foundation
import Testing
@testable import PosKit

@Suite("FEC : formats et comptes (miroir de FecExportService)")
struct LocalFecFormatTests {
    private func decimal(_ text: String) -> Decimal { Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))! }

    @Test func headerHasTheEighteenOfficialColumns() {
        let columns = LocalFecFormat.header.split(separator: "\t", omittingEmptySubsequences: false)
        #expect(columns.count == 18)
        #expect(columns.first == "JournalCode")
        #expect(columns[11] == "Debit")
        #expect(columns.last == "Idevise")
    }

    @Test func amountsUseTwoDecimalsAndAPoint() {
        #expect(LocalFecFormat.amount(0) == "0.00")
        #expect(LocalFecFormat.amount(5) == "0.05")
        #expect(LocalFecFormat.amount(3850) == "38.50")
        #expect(LocalFecFormat.amount(123456) == "1234.56")
    }

    @Test func rateLabelsFollowTheDotNetZeroHashFormat() {
        #expect(LocalFecFormat.rateLabel(10) == "10")
        #expect(LocalFecFormat.rateLabel(decimal("10.0")) == "10")
        #expect(LocalFecFormat.rateLabel(decimal("5.5")) == "5.5")
        #expect(LocalFecFormat.rateLabel(decimal("2.1")) == "2.1")
        #expect(LocalFecFormat.rateLabel(0) == "0")
        #expect(LocalFecFormat.rateLabel(decimal("8.25")) == "8.3") // milieu arrondi loin de zéro, comme un decimal .NET
    }

    @Test func salesAndVatAccountsFollowTheRateBands() {
        #expect(LocalFecFormat.salesAccount(rate: 20) == FecAccount(number: "706200", label: "Ventes Boissons Alcoolisees 20%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("19.6")).number == "706200")
        #expect(LocalFecFormat.salesAccount(rate: 10) == FecAccount(number: "706100", label: "Ventes Restauration Sur Place 10%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("5.5")) == FecAccount(number: "706550", label: "Ventes Restauration A Emporter 5.5%"))
        #expect(LocalFecFormat.salesAccount(rate: decimal("2.1")) == FecAccount(number: "706000", label: "Ventes Prestations Restauration"))
        #expect(LocalFecFormat.salesAccount(rate: 0).number == "706000")
        #expect(LocalFecFormat.vatAccount(rate: 20) == FecAccount(number: "445720", label: "TVA Collectee 20%"))
        #expect(LocalFecFormat.vatAccount(rate: 10) == FecAccount(number: "445710", label: "TVA Collectee 10%"))
        #expect(LocalFecFormat.vatAccount(rate: decimal("5.5")) == FecAccount(number: "445755", label: "TVA Collectee 5.5%"))
        #expect(LocalFecFormat.vatAccount(rate: decimal("2.1")) == FecAccount(number: "445700", label: "TVA Collectee"))
    }

    @Test func paymentAccountsAndNamesMatchTheDotNetEnum() {
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.creditCard.rawValue) == FecAccount(number: "512000", label: "Banque Cartes Bancaires"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.cash.rawValue) == FecAccount(number: "530000", label: "Caisse Especes"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.mealVoucher.rawValue) == FecAccount(number: "580000", label: "Titres Restaurant a Encaisser"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.roomCharge.rawValue) == FecAccount(number: "411000", label: "Clients - Facturation Chambre"))
        #expect(LocalFecFormat.paymentAccount(method: PaymentMethod.giftCard.rawValue) == FecAccount(number: "419100", label: "Clients - Cartes Cadeaux"))
        #expect(LocalFecFormat.paymentAccount(method: 9) == FecAccount(number: "580000", label: "Virements Internes POS"))
        #expect([0, 1, 2, 3, 4, 9].map(LocalFecFormat.methodName) == ["Cash", "CreditCard", "MealVoucher", "GiftCard", "RoomCharge", "9"])
    }

    @Test func datesAreUtcDaysWhateverTheLocalTimeZone() throws {
        let lateEvening = try #require(SQLDate.parse("2026-09-02T23:30:00.000Z")) // 1 h 30 le 3 à Paris
        #expect(LocalFecFormat.date(lateEvening) == "20260902")
    }

    @Test func fileNameTrimsTheSirenOrUsesNineZeros() throws {
        let end = try #require(SQLDate.parse("2026-09-03T09:00:00.000Z"))
        #expect(LocalFecFormat.fileName(siren: " 987654321 ", end: end) == "987654321FEC20260903.txt")
        #expect(LocalFecFormat.fileName(siren: "  ", end: end) == "000000000FEC20260903.txt")
        #expect(LocalFecFormat.fileName(siren: "", end: end) == "000000000FEC20260903.txt")
    }

    @Test func entryNumbersHaveSevenDigits() {
        #expect(LocalFecFormat.entryNumber(1) == "0000001")
        #expect(LocalFecFormat.entryNumber(1234567) == "1234567")
    }

    @Test func aLineHasTheEighteenFieldsOfFormatFecLine() {
        let line = LocalFecFormat.line(
            entry: "0000001", date: "20260903", account: FecAccount(number: "512000", label: "Banque Cartes Bancaires"),
            pieceRef: "T01-000001", label: "Reglement CreditCard T01-000001", debit: 2000, credit: 0
        )
        #expect(line == "VT\tJournal des Ventes\t0000001\t20260903\t512000\tBanque Cartes Bancaires\t\t\tT01-000001\t20260903\tReglement CreditCard T01-000001\t20.00\t0.00\t\t\t20260903\t\tEUR")
    }
}
