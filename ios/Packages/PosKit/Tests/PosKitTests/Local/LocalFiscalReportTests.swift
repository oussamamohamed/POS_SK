import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : rapports X et clôtures Z")
struct LocalFiscalReportTests {
    private func seatOrder(_ api: LocalPosAPI, table: String = "T1", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func xReportOnFreshDatabaseReturnsZeroTotals() async throws {
        let api = try await makeLocalAPI()
        let x = try await api.xReport(terminalId: "T01")
        #expect(x.totalSalesTtc == .zero)
        #expect(x.totalSalesHt == .zero)
        #expect(x.receiptCount == 0)
        #expect(x.vatBreakdown.isEmpty)
        #expect(x.paymentTotals.isEmpty)
        #expect(x.perpetualGrandTotal == .zero)
        #expect(x.closureSequence == nil)
        #expect(x.signatureHash == nil)
    }

    @Test func xReportAggregatesSalesCorrectlyAndIsReadOnly() async throws {
        let api = try await makeLocalAPI()
        let order = try await seatOrder(api, table: "T1", quantity: 1)

        // Burger = 19.50 € TTC, réglé par carte
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))

        let x1 = try await api.xReport(terminalId: "T01")
        #expect(x1.totalSalesTtc == Money(cents: 1950))
        #expect(x1.receiptCount == 1)
        #expect(x1.paymentTotals["CreditCard"] == Money(cents: 1950))
        #expect(x1.perpetualGrandTotal == Money(cents: 1950))

        // Appel en lecture seule : xReport répété renvoie exactement le même résultat
        let x2 = try await api.xReport(terminalId: "T01")
        #expect(x2.totalSalesTtc == x1.totalSalesTtc)
        #expect(x2.receiptCount == x1.receiptCount)
    }

    @Test func zClosureRefusedWhenOpenOrdersExist() async throws {
        let api = try await makeLocalAPI()
        _ = try await seatOrder(api, table: "T42", quantity: 1)

        await #expect(throws: APIError.self) {
            _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Manager")
        }
    }

    @Test func zClosureSealsPeriodAndIncrementsSequence() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order1 = try await seatOrder(api, table: "T1", quantity: 1)

        // Commande 1 : 19.50 € en carte
        _ = try await api.pay(localPayment(order1, table: "T1", tenders: [localTender(.creditCard, 1950)]))

        // Clôture Z n°1
        let managerId = UUID()
        let z1 = try await api.zClosure(terminalId: "T01", managerId: managerId, managerName: "Chef")
        #expect(z1.closureSequence == 1)
        #expect(z1.totalSalesTtc == Money(cents: 1950))
        #expect(z1.receiptCount == 1)
        #expect(z1.paymentTotals["CreditCard"] == Money(cents: 1950))
        #expect(z1.perpetualGrandTotal == Money(cents: 1950))
        #expect(z1.signatureHash != nil)

        // Après la clôture Z, le rapport X est réinitialisé à zéro pour la nouvelle période
        let xAfter = try await api.xReport(terminalId: "T01")
        #expect(xAfter.totalSalesTtc == .zero)
        #expect(xAfter.receiptCount == 0)
        #expect(xAfter.perpetualGrandTotal == Money(cents: 1950))

        // `latestClosure` retrouve la clôture scellée
        let latest = try await api.latestClosure(terminalId: "T01")
        #expect(latest?.closureSequence == 1)
        #expect(latest?.totalSalesTtc == Money(cents: 1950))
        #expect(latest?.perpetualGrandTotal == Money(cents: 1950))

        // Commande 2 : 39.00 € en espèces pour la 2ème période
        let order2 = try await seatOrder(api, table: "T2", quantity: 2)
        _ = try await api.pay(localPayment(order2, table: "T2", tenders: [localTender(.cash, 3900)]))

        // Clôture Z n°2
        let z2 = try await api.zClosure(terminalId: "T01", managerId: managerId, managerName: "Chef")
        #expect(z2.closureSequence == 2)
        #expect(z2.totalSalesTtc == Money(cents: 3900))
        #expect(z2.receiptCount == 1)
        #expect(z2.paymentTotals["Cash"] == Money(cents: 3900))
        #expect(z2.perpetualGrandTotal == Money(cents: 5850))

        // Les chaînes fiscales (reçus, clôtures, JET) sont 100% intègres
        let verification = try LocalFiscalRepository(db: db).verifyChains()
        #expect(verification.isEmpty)
    }

    @Test func reprintLatestClosureIncrementsDuplicateNumber() async throws {
        let api = try await makeLocalAPI()

        // Pas encore de clôture -> 404
        await #expect(throws: APIError.self) {
            _ = try await api.reprintLatestClosure(terminalId: "T01")
        }

        let order = try await seatOrder(api, table: "T5", quantity: 1)
        _ = try await api.pay(localPayment(order, table: "T5", tenders: [localTender(.cash, 1950)]))
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")

        let dup1 = try await api.reprintLatestClosure(terminalId: "T01")
        #expect(dup1.duplicateNumber == 1)
        #expect(!dup1.printQueued)

        let dup2 = try await api.reprintLatestClosure(terminalId: "T01")
        #expect(dup2.duplicateNumber == 2)
    }

    @Test func nonManagerCannotAccessXOrZReports() async throws {
        let api = try await makeLocalAPI(pin: "2468") // 2468 = serveur
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            _ = try await api.xReport(terminalId: "T01")
        }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Serveur")
        }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            _ = try await api.reprintLatestClosure(terminalId: "T01")
        }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            _ = try await api.verifyChains()
        }
    }

    @Test func verifyChainsReturnsStructuredValidChains() async throws {
        let (api, _) = try await makeLocalAPIWithDatabase()
        let order = try await seatOrder(api, table: "T1", quantity: 1)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")

        let result = try await api.verifyChains()
        #expect(result.isValid)
        #expect(result.chains.count == 3)
        let receiptChain = result.chains.first { $0.chain == "receipts" }
        #expect(receiptChain?.isValid == true)
        #expect(receiptChain?.checkedCount == 1)
        let closureChain = result.chains.first { $0.chain == "z_closures" }
        #expect(closureChain?.isValid == true)
        #expect(closureChain?.checkedCount == 1)
        let jetChain = result.chains.first { $0.chain == "journal" }
        #expect(jetChain?.isValid == true)
        #expect((jetChain?.checkedCount ?? 0) >= 1)
    }
}

