import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : terminal du poste autonome")
struct LocalTerminalTests {
    @Test func requestedTerminalIsIgnoredForTablePayments() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)], terminal: "T09"))
        #expect(result.receiptNumber == "NF-T01-000001")
        #expect(LocalPosAPI.standaloneTerminalId == "T01")
    }
}
