import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : salle et réglages")
struct LocalFloorTests {
    @Test func seedsEightTablesInOrder() async throws {
        let api = try await makeLocalAPI()
        let tables = try await api.tables()
        #expect(tables.map(\.tableNumber) == ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8"])
        #expect(tables.map(\.capacity) == [2, 4, 6, 2, 8, 4, 2, 4])
        #expect(tables.allSatisfy { $0.status == .free && $0.activeOrderId == nil && $0.activeOrderTotalTtc == .zero })
    }

    @Test func tableCreationNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.createTable(number: "T9", capacity: 2) }
    }

    @Test func createTableRejectsDuplicates() async throws {
        let api = try await makeLocalAPI()
        try await api.createTable(number: "T10", capacity: 6)
        #expect(try await api.tables().last?.tableNumber == "T10")
        await #expect(throws: APIError.server(status: 400, message: "Table existante")) { try await api.createTable(number: "T10", capacity: 2) }
    }

    @Test func settingsDefaultsAndPartialSave() async throws {
        let api = try await makeLocalAPI()
        let initial = try await api.settings()
        #expect(initial.receiptLanguage == "fr" && initial.companyName == "RESTAURANT L'ANTIGRAVITE")
        #expect(initial.fiscalYearStartMonth == 1 && initial.fiscalYearStartDay == 1)

        let saved = try await api.saveSettings(RestaurantSettings(receiptLanguage: "en", kitchenTicketLanguage: "ar", siret: "11122233344455"))
        #expect(saved.receiptLanguage == "en" && saved.kitchenTicketLanguage == "ar" && saved.siret == "11122233344455")
        #expect(saved.companyName == "RESTAURANT L'ANTIGRAVITE")
    }

    @Test func settingsValidationAndPermissions() async throws {
        let manager = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 400, message: "Langue non prise en charge.")) {
            try await manager.saveSettings(RestaurantSettings(receiptLanguage: "de"))
        }
        let waiter = try await makeLocalAPI(pin: "2468")
        _ = try await waiter.settings()
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await waiter.saveSettings(RestaurantSettings(receiptLanguage: "fr"))
        }
    }
}
