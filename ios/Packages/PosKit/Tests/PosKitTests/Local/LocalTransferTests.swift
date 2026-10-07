import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : transfert et fusion")
struct LocalTransferTests {
    private func names(_ order: ActiveOrder?) -> [String] { order?.lines.map(\.productName) ?? [] }

    @Test func transferMovesTheOrderToAFreeTable() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 4, waiter: "Sophie", items: [localInput(burger)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: false)
        #expect(result.success == true && result.message == "Commande transférée de T1 vers T2")
        let tables = try await api.tables()
        let source = try #require(tables.first { $0.tableNumber == "T1" })
        let target = try #require(tables.first { $0.tableNumber == "T2" })
        #expect(source.status == .free && source.activeOrderId == nil && source.coversCount == 0 && source.assignedWaiterName == nil && source.openedAtUtc == nil)
        #expect(target.status == .occupied && target.coversCount == 4 && target.assignedWaiterName == "Sophie" && target.activeOrderId != nil)
        let order = try #require(try await api.activeOrder(table: "T2"))
        #expect(order.tableNumber == "T2" && names(order) == ["Burger Gourmet Rossini"])
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func transferOntoOccupiedTableIsRefusedAndKeepsBothOrders() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", items: [localInput(burger)])
        try await seatTable(api, "T2", items: [localInput(cafe)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: false)
        #expect(result.success == false && result.message == "Échec du transfert de table.")
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
        #expect(names(try await api.activeOrder(table: "T2")) == ["Café Gourmand"])
    }

    @Test func transferToSameTableIsRefused() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        #expect(try await api.transfer(from: "T1", to: "T1", merge: false).success == false)
        #expect(try await api.transfer(from: "T1", to: "T1", merge: true).success == false)
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
    }

    @Test func transferFromAnEmptyOrUnknownTableFails() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        #expect(try await api.transfer(from: "T3", to: "T4", merge: false).success == false)
        #expect(try await api.transfer(from: "T404", to: "T4", merge: false).success == false)
        // Cible inconnue : la commande source reste intacte.
        #expect(try await api.transfer(from: "T1", to: "T404", merge: false).success == false)
        #expect(names(try await api.activeOrder(table: "T1")) == ["Burger Gourmet Rossini"])
    }

    @Test func mergeCombinesLinesAndCovers() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", covers: 2, waiter: "Sophie", items: [localInput(burger)])
        try await seatTable(api, "T2", covers: 4, waiter: "Léa", items: [localInput(cafe)])
        let result = try await api.transfer(from: "T1", to: "T2", merge: true)
        #expect(result.success == true && result.message == "Tables T1 et T2 fusionnées")
        let merged = try #require(try await api.activeOrder(table: "T2"))
        #expect(Set(names(merged)) == ["Burger Gourmet Rossini", "Café Gourmand"] && merged.coversCount == 6 && merged.waiterName == "Léa")
        #expect(merged.totalTtcAmount == Money(cents: 1950 + 850))
        let source = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(source.status == .free && source.activeOrderId == nil && source.coversCount == 0)
    }

    @Test func mergeIntoAFreeTableBehavesLikeATransfer() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 3, items: [localInput(burger)])
        let result = try await api.transfer(from: "T1", to: "T3", merge: true)
        #expect(result.success == true && result.message == "Tables T1 et T3 fusionnées")
        let order = try #require(try await api.activeOrder(table: "T3"))
        #expect(order.coversCount == 3 && names(order) == ["Burger Gourmet Rossini"])
        #expect(try await api.activeOrder(table: "T1") == nil)
    }
}
